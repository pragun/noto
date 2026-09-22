# Deploying noto to an LXD VM on the tailnet

The shape: noto runs in a VM, reachable only over Tailscale, with Traefik
terminating TLS on a certificate issued by `tailscale cert`. Ollama stays on the laptop — the VM reaches it over the tailnet
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
| `provision.sh` | **magpie** (the LXD host) | `lxc`, this repo at `~/noto`, `deploy/.env` |
| `export-local.sh` | the **Mac** | docker + the dev stack running |
| `import-data.sh` | **magpie**, or **inside the VM** | the archive from `export-local.sh` |

`provision.sh` drives the VM through `lxc`, so it runs on magpie, from the clone
at `~/noto`. It feeds the VM via `git archive HEAD`, so whatever you have checked
out there is what gets deployed.

## First run

    ssh magpie
    cd ~/noto && git pull

Then:

```bash
cp deploy/env.example deploy/.env
$EDITOR deploy/.env                 # embedding endpoint + API keys

# Put the auth key in a file rather than on the command line — an inline
# key lands in your shell history and in `ps` output. provision.sh reads it
# automatically and deletes it once the VM has joined.
printf %s 'tskey-auth-...' > deploy/.ts-authkey && chmod 600 deploy/.ts-authkey

./deploy/provision.sh
```

It creates the VM, waits out cloud-init (Docker + Tailscale + firewall), joins the
tailnet, issues the certificate, copies the repo in, builds the image and starts
the stack behind Traefik. Re-runnable — run it again to redeploy after a commit.

## Redeploying

    ssh magpie && cd ~/noto && git pull && ./deploy/provision.sh

It reuses the existing VM, re-pushes the source and rebuilds.

## Moving the data

On the Mac:

```bash
./deploy/export-local.sh            # -> noto-export-<timestamp>.tar
scp noto-export-*.tar you@lxd-host:/tmp/
```

And on magpie (or scp it straight to the VM over Tailscale SSH and run it there —
`import-data.sh` detects which side it is on):

```bash
./deploy/import-data.sh /tmp/noto-export-*.tar
```

Destructive on the target: the database is dropped and recreated. It asks first.

## Before you start

**Enable MagicDNS *and* HTTPS certificates** for your tailnet at
[the DNS admin page](https://login.tailscale.com/admin/dns). Without the second one
`tailscale cert` can't issue the certificate Traefik serves, and iOS won't install
the PWA or grant microphone access over a bad cert.

**Check Ollama is reachable from the tailnet.** If it's the docker container from
the dev compose, it already publishes on `0.0.0.0:11434` and there is nothing to
do — confirm with `curl http://$(tailscale ip -4):11434/api/tags` on the Mac.

Only the *native* macOS Ollama app binds loopback. For that, use the app's own
network-exposure setting or a user LaunchAgent; `sudo launchctl setenv` is refused
by SIP and will not work.

Either way, point `EMBEDDINGS_ENDPOINT` in `deploy/.env` at the Mac's tailnet name.

Note the coupling: if Ollama lives in the dev compose, stopping the dev stack stops
embeddings on the VM. Harmless — the backlog shows on `/admin` and drains later —
but pull `ollama` into its own compose file if you'd rather it be always-on.

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

**Postgres has no password** — `POSTGRES_HOST_AUTH_METHOD=trust`, port bound to
`127.0.0.1`. The tailnet is the boundary, and a password stored in `.env` on the
same box as the shell that could read it was guarding nothing. Note this is applied
at initdb time, so changing your mind later means editing `pg_hba.conf`, not the
compose file.

**Run one app instance at a time against a given database.** The embedding
provider's endpoint is stored in Postgres and rewritten from `EMBEDDINGS_ENDPOINT`
at startup, so two instances with different config will fight over that row.
