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

## Which script runs where

These usually are not the same machine, so each script says so up front.

| script | runs on | needs |
|---|---|---|
| `provision.sh` | the **LXD host** | `lxc`, a clone of this repo, `deploy/.env` |
| `export-local.sh` | the **Mac** | docker + the dev stack running |
| `import-data.sh` | the **LXD host**, or **inside the VM** | the archive from `export-local.sh` |

`provision.sh` drives the VM through `lxc`, so it has to run where LXD is. Clone
the repo there and put `deploy/.env` next to it.

## First run

On the LXD host:

```bash
cp deploy/env.example deploy/.env
$EDITOR deploy/.env                 # password, embedding endpoint, API keys

TS_AUTHKEY=tskey-auth-... ./deploy/provision.sh
```

It creates the VM, waits out cloud-init (Docker + Tailscale + firewall), joins the
tailnet, issues the certificate, copies the repo in, builds the image and starts
the stack behind Traefik. Re-runnable — run it again to redeploy after a commit.

Then move the data across. On the Mac:

```bash
./deploy/export-local.sh            # -> noto-export-<timestamp>.tar
scp noto-export-*.tar you@lxd-host:/tmp/
```

And on the LXD host (or scp it straight to the VM over Tailscale SSH and run it
there — `import-data.sh` detects which side it is on):

```bash
./deploy/import-data.sh /tmp/noto-export-*.tar
```

Destructive on the target: the database is dropped and recreated. It asks first.

## Before you start

**Enable MagicDNS *and* HTTPS certificates** for your tailnet at
[the DNS admin page](https://login.tailscale.com/admin/dns). Without the second one
`tailscale cert` can't issue the certificate Traefik serves, and iOS won't install
the PWA or grant microphone access over a bad cert.

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
