#!/usr/bin/env bash
# Run this ON THE MAC (wherever the dev stack lives). Produces a single archive
# holding the database dump and the media tree, ready to hand to import-data.sh.
#
#   ./deploy/export-local.sh [output-dir]
#
# Needs nothing but docker and the dev stack running — no lxc, no VM, no network.
# That is the point: the Mac and the LXD host are usually different machines.

set -euo pipefail

OUT_DIR="${1:-.}"
LOCAL_DB_CONTAINER="${LOCAL_DB_CONTAINER:-noto-db}"
LOCAL_MEDIA="${LOCAL_MEDIA:-$HOME/noto-data/media}"

say() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
die() { printf '\033[31merror: %s\033[0m\n' "$*" >&2; exit 1; }

command -v docker >/dev/null || die "docker not found"
docker ps --format '{{.Names}}' | grep -qx "$LOCAL_DB_CONTAINER" \
    || die "local db container '$LOCAL_DB_CONTAINER' is not running — start the dev stack first"
[[ -d "$LOCAL_MEDIA" ]] || die "media directory not found: $LOCAL_MEDIA"

mkdir -p "$OUT_DIR"
OUT="$(cd "$OUT_DIR" && pwd)/noto-export-$(date +%Y%m%d-%H%M%S).tar"

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

say "Dumping the database"
docker exec "$LOCAL_DB_CONTAINER" pg_dump -U noto -d noto --format=custom --no-owner --no-acl \
    > "$STAGE/noto.dump"

say "Packing media ($(du -sh "$LOCAL_MEDIA" | cut -f1))"
tar -C "$LOCAL_MEDIA" -cf "$STAGE/media.tar" .

# Embeddings travel inside the dump, but they are derived data — if the target
# ends up with a different model the admin page's "re-embed everything" rebuilds
# them. Nothing here needs to be kept in sync by hand.
tar -C "$STAGE" -cf "$OUT" noto.dump media.tar

say "Done"
cat <<EOF
  $OUT  ($(du -h "$OUT" | cut -f1))

Move it to wherever you run lxc, then import:

  # if LXD is on another machine
  scp "$OUT" you@lxd-host:/tmp/

  # then, on the LXD host (or inside the VM over Tailscale SSH):
  ./deploy/import-data.sh /tmp/$(basename "$OUT")
EOF
