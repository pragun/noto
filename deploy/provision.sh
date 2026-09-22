#!/usr/bin/env bash
# Provision a noto instance in an LXD VM: create it, join the tailnet, issue a
# TLS certificate, build and start the stack behind Traefik.
#
# A VM (--vm) rather than a system container: Docker inside unprivileged LXC
# needs security.nesting, and Tailscale needs /dev/net/tun punched through.
# Both are avoidable for the price of a little RAM.
#
# Re-runnable. If the VM already exists it is reused and the stack redeployed.
#
#   TS_AUTHKEY=tskey-auth-... ./deploy/provision.sh
#
# Env:
#   NAME        VM name           (default: noto)
#   TS_AUTHKEY  tailnet auth key  (required on first run; generate at
#               https://login.tailscale.com/admin/settings/keys)
#   TS_HOSTNAME hostname on the tailnet (default: $NAME)
#   MEMORY      (default: 4GiB)
#   DISK        (default: 40GiB)
#   IMAGE       (default: ubuntu:24.04)

set -euo pipefail

NAME="${NAME:-noto}"
TS_HOSTNAME="${TS_HOSTNAME:-$NAME}"
MEMORY="${MEMORY:-4GiB}"
DISK="${DISK:-40GiB}"
IMAGE="${IMAGE:-ubuntu:24.04}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY_DIR="$REPO_ROOT/deploy"

say() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
die() { printf '\033[31merror: %s\033[0m\n' "$*" >&2; exit 1; }

inside() { lxc exec "$NAME" -- "$@"; }
inside_sh() { lxc exec "$NAME" -- bash -lc "$1"; }

command -v lxc >/dev/null || die "lxc not found — install LXD first"
[[ -f "$DEPLOY_DIR/.env" ]] || die "missing deploy/.env — copy deploy/env.example and fill it in"

# Fail early on the values with no safe default, rather than halfway through.
# shellcheck disable=SC1091
set -a; source "$DEPLOY_DIR/.env"; set +a
[[ -n "${POSTGRES_PASSWORD:-}" ]]   || die "POSTGRES_PASSWORD is empty in deploy/.env"
[[ -n "${EMBEDDINGS_ENDPOINT:-}" ]] || die "EMBEDDINGS_ENDPOINT is empty in deploy/.env"

# ---------------------------------------------------------------- create VM --
if lxc info "$NAME" >/dev/null 2>&1; then
    say "VM '$NAME' already exists — reusing it"
    [[ "$(lxc info "$NAME" | awk '/^Status:/{print tolower($2)}')" == "running" ]] || lxc start "$NAME"
else
    say "Launching $IMAGE VM '$NAME' ($MEMORY, $DISK)"
    lxc launch "$IMAGE" "$NAME" --vm \
        -c limits.memory="$MEMORY" \
        -c "cloud-init.user-data=$(cat "$DEPLOY_DIR/cloud-init.yaml")" \
        -d root,size="$DISK"
fi

say "Waiting for the agent and cloud-init (installing docker + tailscale, a few minutes)"
for _ in $(seq 1 120); do inside true 2>/dev/null && break; sleep 5; done
inside true 2>/dev/null || die "VM agent never came up — check 'lxc console $NAME'"

inside cloud-init status --wait || die "cloud-init failed — check 'lxc exec $NAME -- cloud-init status --long'"
inside test -f /var/lib/cloud/noto-ready || die "cloud-init finished but the noto marker is missing"

# ------------------------------------------------------------------ tailnet --
if inside_sh 'tailscale status --json 2>/dev/null | grep -q "\"BackendState\":\"Running\""'; then
    say "Already on the tailnet"
else
    [[ -n "${TS_AUTHKEY:-}" ]] || die "not on the tailnet yet and TS_AUTHKEY is unset"
    say "Joining the tailnet as '$TS_HOSTNAME'"
    inside tailscale up --authkey "$TS_AUTHKEY" --hostname "$TS_HOSTNAME" --ssh
fi

# Traefik terminates TLS here, so make sure nothing is left over from a previous
# `tailscale serve` deployment fighting it for port 443.
inside_sh 'tailscale serve reset >/dev/null 2>&1 || true'

DISCOVERED_FQDN="$(inside_sh 'tailscale status --json | python3 -c "import json,sys; print(json.load(sys.stdin)[\"Self\"][\"DNSName\"].rstrip(\".\"))"')"
[[ -n "$DISCOVERED_FQDN" ]] || die "could not read the tailnet DNS name"
TS_FQDN="${TS_FQDN:-$DISCOVERED_FQDN}"
say "Serving as $TS_FQDN"

# -------------------------------------------------------------- push source --
say "Copying the repo into the VM"
inside mkdir -p /opt/noto
# git archive keeps the build context clean: tracked files only, no bin/obj/.git.
git -C "$REPO_ROOT" archive --format=tar HEAD \
    | inside_sh 'rm -rf /opt/noto/src /opt/noto/deploy && tar -x -C /opt/noto'

# .env is gitignored, so it travels separately — with TS_FQDN resolved, because
# Traefik's routing rule and the certificate both key off it.
tmp_env="$(mktemp)"
trap 'rm -f "$tmp_env"' EXIT
grep -v '^TS_FQDN=' "$DEPLOY_DIR/.env" > "$tmp_env"
printf 'TS_FQDN=%s\n' "$TS_FQDN" >> "$tmp_env"
lxc file push "$tmp_env" "$NAME/opt/noto/deploy/.env" --mode 0600

# ------------------------------------------------------- certificate + timer --
# Traefik's ACME resolver cannot work here: HTTP-01 needs the box to be publicly
# reachable, and it deliberately isn't. Tailscale issues a real Let's Encrypt
# cert for the ts.net name through its own control plane instead.
say "Issuing the TLS certificate"
inside mkdir -p /etc/noto/certs
inside_sh "printf 'TS_FQDN=%s\n' '$TS_FQDN' > /etc/noto/cert.env"
lxc file push "$DEPLOY_DIR/systemd/noto-cert.service" "$NAME/etc/systemd/system/noto-cert.service"
lxc file push "$DEPLOY_DIR/systemd/noto-cert.timer" "$NAME/etc/systemd/system/noto-cert.timer"
inside systemctl daemon-reload
inside systemctl enable --now noto-cert.timer

if ! inside systemctl start noto-cert.service; then
    inside journalctl -u noto-cert.service -n 30 --no-pager || true
    die "tailscale cert failed — enable HTTPS certificates at https://login.tailscale.com/admin/dns"
fi
inside test -s /etc/noto/certs/noto.crt || die "certificate file is empty after issuance"

# ------------------------------------------------------------------- deploy --
say "Building the image and starting the stack (first build is slow)"
inside_sh 'cd /opt/noto/deploy && docker compose -f docker-compose.prod.yml up -d --build'

say "Waiting for noto to answer through Traefik"
for _ in $(seq 1 60); do
    code="$(inside_sh "curl -s -o /dev/null -w '%{http_code}' --resolve '$TS_FQDN:443:127.0.0.1' 'https://$TS_FQDN/' || true")"
    [[ "$code" == "200" ]] && break
    sleep 5
done
if [[ "${code:-}" != "200" ]]; then
    inside_sh 'cd /opt/noto/deploy && docker compose -f docker-compose.prod.yml logs --tail=40 traefik app' || true
    die "noto did not come up through Traefik (last status: ${code:-none})"
fi

cat <<EOF

$(printf '\033[1m==> noto is up\033[0m')

  https://$TS_FQDN
  phone capture:  https://$TS_FQDN/m.html
  admin:          https://$TS_FQDN/admin

Next:
  1. Move your data across:   ./deploy/migrate-data.sh $NAME
  2. Make Ollama reachable on the Mac so embeddings can run:
       sudo launchctl setenv OLLAMA_HOST 0.0.0.0   (then relaunch Ollama)
     and check EMBEDDINGS_ENDPOINT in deploy/.env points at the Mac's tailnet name.
  3. Drain the capture queue in the OLD PWA before installing the new one —
     IndexedDB is per-origin and queued notes do not follow you to $TS_FQDN.

Handy:
  logs      lxc exec $NAME -- docker logs -f noto-app
  traefik   lxc exec $NAME -- docker logs -f noto-traefik
  cert      lxc exec $NAME -- systemctl list-timers noto-cert.timer
  redeploy  ./deploy/provision.sh
  shell     lxc exec $NAME -- bash
EOF
