# noto on a Linode, behind WireGuard

Everything that was done to the server by hand, made repeatable.

    ssh into nothing — the tunnel must be up first
    cp secrets.example.yml secrets.yml && $EDITOR secrets.yml
    ansible-playbook site.yml

Second run should report `changed=0`.

## The shape

    iPhone ─┐                            ┌─ traefik :443  (Let's Encrypt, DNS-01)
            ├─ wireguard ─→ 10.77.0.1 ───┼─ app     :5100
    Mac  ───┘                            └─ postgres:5432 (loopback only)
             │
             └─ 10.77.0.2:11434  ollama on the Mac, dialled *from* the server

The Linode's public interface accepts **UDP 51820 and nothing else**. There is no
authentication in noto itself — the tunnel is the boundary, which is why the
firewall matters more than usual.

`noto.zozo-pepe.cc` is a public A record pointing at `10.77.0.1`. Anyone can
resolve it; only tunnel peers can route to it. DNS and reachability are separate
concerns, which is the trick that makes this work with no open ports.

## What Ansible does not cover

The instance and its firewall live in the Linode API, not on the host. They were
created with `linode-cli` and are reproduced here for the record:

```bash
# 2GB instance
linode-cli linodes create --label noto-server --region us-east \
  --type g6-standard-1 --image linode/ubuntu24.04 \
  --root_pass "$(openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | head -c 32)" \
  --authorized_keys "$(cat ~/.ssh/id_ed25519.pub)"

# Inbound DROP except WireGuard; outbound ACCEPT so docker/apt/ACME work.
linode-cli firewalls create --label noto-fw \
  --rules.inbound_policy DROP --rules.outbound_policy ACCEPT \
  --rules.inbound '[{"label":"wireguard","action":"ACCEPT","protocol":"UDP",
                     "ports":"51820",
                     "addresses":{"ipv4":["0.0.0.0/0"],"ipv6":["::/0"]}}]' \
  --devices.linodes <linode-id>
```

A **Cloud** firewall rather than host `iptables`, deliberately. Docker publishes
ports via `PREROUTING` DNAT and `FORWARD` — they never traverse `INPUT` — so
host firewall rules do not protect published container ports. Filtering upstream
of the instance sidesteps that entirely, and a bad rule cannot lock you out of
Lish. Compose additionally binds ports to `10.77.0.1` rather than `0.0.0.0`.

## Getting back in when the tunnel is down

Public SSH is closed, so the tunnel is the only route. The fallback is **Lish**:
Cloud Manager → the instance → *Launch LISH Console*, then log in as `root` with
the root password.

Lish-over-SSH (`ssh user-label@lish-us-east.linode.com`) needs a key on your
**Linode profile**, which is a different list from the instance's
`authorized_keys`. `lish_auth_method` accepts only `keys_only` or `disabled` —
the password option no longer exists.

## DNS gotchas, both real, both hit during setup

**On the Mac**, MagicDNS forwarded to a company resolver that strips RFC1918
answers — DNS rebinding protection doing its job against a pattern that is,
technically, exactly what we are doing. Fixed with a Tailscale **split DNS**
entry sending `zozo-pepe.cc` to `1.1.1.1`. An `/etc/hosts` line works too and
keeps the domain out of the company console.

**On the phone**, do *not* set `DNS =` in the WireGuard config unless something
is actually serving port 53 on `10.77.0.1`. iOS applies a tunnel's DNS
system-wide, so pointing it at a dead address breaks all name resolution on the
device, not just noto. Carrier DNS resolves the record fine on its own.

## Where local files live

| path | what | tracked |
|---|---|---|
| `deploy/.local/` | peer configs (private keys), phone QR, server root password, Cloudflare token | no |
| `deploy/ansible/secrets.yml` | tokens the playbook templates into `.env` | no |
| `src/Noto.Server/appsettings.Local.json` | OpenRouter key for the dev stack | no |
| `~/noto-data/` | dev stack state: postgres, media, ollama models | n/a |
| `~/.config/linode-cli` | Linode API token | n/a |

`deploy/.local/` is populated by `ansible-playbook fetch-peers.yml`, or by hand.
Everything in it is a credential; the directory is gitignored as a whole rather
than file by file, so a new file added there is ignored by default.

## Backups — still missing

`{{ noto_data }}` is the only copy of anything captured since the migration, and
it is **two** sources of truth that must be captured together: the postgres
database and the media tree. `attachments.storage_path` stores a path, not the
bytes, so a database dump alone restores notes with every photo and recording
missing, and nothing errors loudly when that happens.
