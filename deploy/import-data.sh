#!/usr/bin/env bash
# Restore an archive from export-local.sh into the noto VM.
#
#   ./deploy/import-data.sh <archive.tar> [vm-name]
#
# Runs in either place, and works out which by itself:
#   - on the LXD host  → reaches the VM with `lxc exec`
#   - inside the VM    → operates on the local containers directly
#     (handy over Tailscale SSH: scp the archive straight to the VM and run it there)
#
# Destructive on the target: the database is dropped and recreated, and media
# files are overwritten. Anything captured on the VM since the export is lost,
# so drain the phone's queue before exporting, not after.

set -euo pipefail

ARCHIVE="${1:-}"
NAME="${2:-noto}"

say() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
die() { printf '\033[31merror: %s\033[0m\n' "$*" >&2; exit 1; }

[[ -n "$ARCHIVE" ]] || die "usage: import-data.sh <archive.tar> [vm-name]"
[[ -f "$ARCHIVE" ]] || die "archive not found: $ARCHIVE"
ARCHIVE="$(cd "$(dirname "$ARCHIVE")" && pwd)/$(basename "$ARCHIVE")"

# ---------------------------------------------------------------- where am I --
if docker ps --format '{{.Names}}' 2>/dev/null | grep -qx noto-db; then
    MODE=local
    run() { bash -lc "$1"; }
    stage_file() { cp "$1" "$2"; }
elif command -v lxc >/dev/null && lxc info "$NAME" >/dev/null 2>&1; then
    MODE=lxc
    run() { lxc exec "$NAME" -- bash -lc "$1"; }
    stage_file() { lxc file push "$1" "$NAME$2"; }
else
    die "found neither a local noto-db container nor an LXD VM called '$NAME' — run this on the LXD host or inside the VM"
fi
say "Importing via: $MODE"

run 'docker ps --format "{{.Names}}" | grep -qx noto-db' \
    || die "noto-db is not running on the target — run provision.sh first"

say "Confirm"
printf '    archive: %s (%s)\n' "$ARCHIVE" "$(du -h "$ARCHIVE" | cut -f1)"
printf '    target:  %s\n' "$([[ $MODE == lxc ]] && echo "VM '$NAME'" || echo 'this machine')"
read -r -p "Replace the database and media on the target with this? [y/N] " reply
[[ "$reply" == "y" || "$reply" == "Y" ]] || die "aborted"

say "Staging the archive"
run 'rm -rf /tmp/noto-import && mkdir -p /tmp/noto-import'
stage_file "$ARCHIVE" "/tmp/noto-import/export.tar"
run 'cd /tmp/noto-import && tar -xf export.tar && test -s noto.dump && test -s media.tar' \
    || die "archive is missing noto.dump or media.tar — was it made by export-local.sh?"

say "Restoring (app stopped so nothing writes mid-restore)"
run '
    set -euo pipefail
    cd /opt/noto/deploy
    docker compose -f docker-compose.prod.yml stop app
    docker exec -i noto-db psql -U noto -d postgres -v ON_ERROR_STOP=1 \
        -c "DROP DATABASE IF EXISTS noto WITH (FORCE);" -c "CREATE DATABASE noto OWNER noto;"
    docker exec -i noto-db pg_restore -U noto -d noto --no-owner --no-acl < /tmp/noto-import/noto.dump
'

say "Unpacking media"
run '
    set -euo pipefail
    mkdir -p /srv/noto-data/media
    tar -xf /tmp/noto-import/media.tar -C /srv/noto-data/media
    rm -rf /tmp/noto-import
'

say "Restarting the app"
run 'cd /opt/noto/deploy && docker compose -f docker-compose.prod.yml start app'

# The app is not published to the host — traefik is the only way in — so the
# health check has to go through it, resolving the cert's hostname to loopback.
TS_FQDN="$(run 'tailscale status --json | python3 -c "import json,sys; print(json.load(sys.stdin)[\"Self\"][\"DNSName\"].rstrip(\".\"))"' 2>/dev/null || true)"
[[ -n "$TS_FQDN" ]] || die "could not read the target's tailnet DNS name"

for _ in $(seq 1 60); do
    code="$(run "curl -s -o /dev/null -w '%{http_code}' --resolve '$TS_FQDN:443:127.0.0.1' 'https://$TS_FQDN/' || true")"
    [[ "$code" == "200" ]] && break
    sleep 5
done
[[ "${code:-}" == "200" ]] || die "app did not come back up (last status: ${code:-none}) — check 'docker logs noto-app' on the target"

say "Done"
cat <<EOF
  Open https://${TS_FQDN:-<your-vm>}/admin and check the embedded count matches the laptop.

  The embedding endpoint is stored per-provider in the database, so the dump
  carried the laptop's value across. The app rewrites it from EMBEDDINGS_ENDPOINT
  on startup; if /admin shows the wrong endpoint, fix deploy/.env and re-run
  provision.sh.
EOF
