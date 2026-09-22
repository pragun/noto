#!/usr/bin/env bash
# Copy the local noto database and media into the provisioned VM.
# Run from the Mac, with the dev stack running (it is the source of the dump).
#
#   ./deploy/migrate-data.sh [vm-name]
#
# Safe to re-run: the restore drops and recreates the target database, so the
# VM ends up matching the laptop exactly. That also means it is one-directional —
# anything captured on the VM since the last run is destroyed.

set -euo pipefail

NAME="${1:-noto}"
LOCAL_DB_CONTAINER="${LOCAL_DB_CONTAINER:-noto-db}"
LOCAL_MEDIA="${LOCAL_MEDIA:-$HOME/noto-data/media}"

say() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
die() { printf '\033[31merror: %s\033[0m\n' "$*" >&2; exit 1; }

command -v lxc >/dev/null || die "lxc not found"
lxc info "$NAME" >/dev/null 2>&1 || die "VM '$NAME' not found — run provision.sh first"
docker ps --format '{{.Names}}' | grep -qx "$LOCAL_DB_CONTAINER" \
    || die "local db container '$LOCAL_DB_CONTAINER' is not running"
[[ -d "$LOCAL_MEDIA" ]] || die "media directory not found: $LOCAL_MEDIA"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

say "Dumping the local database"
docker exec "$LOCAL_DB_CONTAINER" pg_dump -U noto -d noto --format=custom --no-owner --no-acl \
    > "$TMP/noto.dump"
printf '    %s\n' "$(du -h "$TMP/noto.dump" | cut -f1) dump"

say "Confirm"
read -r -p "Replace the database and media on '$NAME' with this? [y/N] " reply
[[ "$reply" == "y" || "$reply" == "Y" ]] || die "aborted"

say "Uploading"
lxc file push "$TMP/noto.dump" "$NAME/tmp/noto.dump"

say "Restoring (app stopped so nothing writes mid-restore)"
lxc exec "$NAME" -- bash -lc '
    set -euo pipefail
    cd /opt/noto/deploy
    docker compose -f docker-compose.prod.yml stop app
    docker exec -i noto-db psql -U noto -d postgres -v ON_ERROR_STOP=1 \
        -c "DROP DATABASE IF EXISTS noto WITH (FORCE);" -c "CREATE DATABASE noto OWNER noto;"
    docker exec -i noto-db pg_restore -U noto -d noto --no-owner --no-acl < /tmp/noto.dump
    rm -f /tmp/noto.dump
'

say "Syncing media ($(du -sh "$LOCAL_MEDIA" | cut -f1))"
# tar over lxc exec: no ssh, no rsync daemon, and it preserves the tree layout.
tar -C "$LOCAL_MEDIA" -cf - . \
    | lxc exec "$NAME" -- bash -lc 'mkdir -p /srv/noto-data/media && tar -x -C /srv/noto-data/media'

say "Restarting the app"
lxc exec "$NAME" -- bash -lc 'cd /opt/noto/deploy && docker compose -f docker-compose.prod.yml start app'

for _ in $(seq 1 60); do
    code="$(lxc exec "$NAME" -- bash -lc 'curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:5100/ || true')"
    [[ "$code" == "200" ]] && break
    sleep 5
done
[[ "${code:-}" == "200" ]] || die "app did not come back up — check 'lxc exec $NAME -- docker logs noto-app'"

TS_NAME="$(lxc exec "$NAME" -- bash -lc 'tailscale status --json | python3 -c "import json,sys; print(json.load(sys.stdin)[\"Self\"][\"DNSName\"].rstrip(\".\"))"')"

say "Done"
cat <<EOF
  Check https://$TS_NAME/admin — embedded count should match the laptop.

  The embedding endpoint is stored per-provider in the database, so the dump
  carried the laptop's value across. The app rewrites it from EMBEDDINGS_ENDPOINT
  on startup; if /admin shows the wrong endpoint, fix deploy/.env and re-run
  provision.sh.
EOF
