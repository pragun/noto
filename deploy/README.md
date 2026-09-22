# Deploying noto to an LXD VM on the tailnet

The shape: noto runs in a VM, reachable only over Tailscale, with `tailscale serve`
terminating TLS. Ollama stays on the laptop — the VM reaches it over the tailnet
when the laptop is awake, and shows a backlog on `/admin` when it isn't.

    iPhone ─┐
            ├─ tailnet ─→ noto VM (app + postgres) ─ tailnet ─→ laptop (ollama)
    laptop ─┘

No public exposure, so no auth — the tailnet is the boundary. Don't put this on a
public IP without building authentication first; there is none.

## Why a VM and not a container

Docker inside unprivileged LXC needs `security.nesting=true`, and Tailscale needs
`/dev/net/tun` passed through. Both are solvable and neither is fun. `--vm` avoids
the whole class of problem for a few hundred MB of RAM, and noto's entire
footprint is around 110MB of data.

## First run

```bash
cp deploy/env.example deploy/.env
$EDITOR deploy/.env                 # password, embedding endpoint, API keys

TS_AUTHKEY=tskey-auth-... ./deploy/provision.sh
./deploy/migrate-data.sh
```

`provision.sh` creates the VM, waits out cloud-init (Docker + Tailscale + firewall),
joins the tailnet, copies the repo in, builds the image, starts the stack, and
publishes it on HTTPS. It's re-runnable — run it again to redeploy after a commit.

`migrate-data.sh` dumps the laptop's database and media and restores them into the
VM. One-directional and destructive on the target; it asks before doing it.

## Before you start

**Enable MagicDNS *and* HTTPS certificates** for your tailnet at
[the DNS admin page](https://login.tailscale.com/admin/dns). Without the second one
`tailscale serve` can't get a certificate, and iOS won't install the PWA or grant
microphone access over a bad cert.

**Make Ollama listen beyond loopback** on the Mac, or the VM can't reach it:

```bash
sudo launchctl setenv OLLAMA_HOST 0.0.0.0    # then relaunch Ollama
```

Point `EMBEDDINGS_ENDPOINT` in `deploy/.env` at the Mac's tailnet name
(`tailscale status` will tell you it).

**Drain the capture queue in the old PWA first.** IndexedDB is per-origin, so notes
queued on `noto.localhost` do not follow you to `noto.<tailnet>.ts.net`. Sync, then
install the new one.

## Afterwards

| | |
|---|---|
| logs | `lxc exec noto -- docker logs -f noto-app` |
| redeploy | `./deploy/provision.sh` |
| shell | `lxc exec noto -- bash` |
| db | `lxc exec noto -- docker exec -it noto-db psql -U noto -d noto` |

**Backups are now your problem.** The only copy of the archive lives on a VM you
will stop thinking about. `pg_dump` plus `/srv/noto-data/media` on a schedule.

**Run one app instance at a time against a given database.** The embedding
provider's endpoint is stored in Postgres and rewritten from `EMBEDDINGS_ENDPOINT`
at startup, so two instances with different config will fight over that row.
