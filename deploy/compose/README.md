# Reference deployment (Docker Compose)

One Compose project for the whole stack on one VM: CISO Assistant (backend, worker, frontend) pinned to a release, PostgreSQL, Caddy serving HTTPS with its internal certificate authority, and the Evidence Hound collector runner. Everything specific to your organisation lives in your private compliance repo; the [first deployment tutorial](../../docs/tutorials/first-deployment.md) walks through both from an empty VM.

Until backups, restore testing and the hardening guide arrive (sprint s12), **snapshot the VM before every upgrade**.

| File | What it is |
| --- | --- |
| [`compose.yml`](compose.yml) | The stack. Settings come from `.env`, secrets from files. |
| [`.env.example`](.env.example) | Every setting, documented. Copy to `.env` (git-ignored) or keep it in your private repo as `deploy/deploy.env`. |
| [`Caddyfile`](Caddyfile) | HTTPS on `CA_HOSTNAME`, `/api/*` to the backend, everything else to the frontend. |
| [`runner-token.sh`](runner-token.sh) | Creates the runner's service account and writes its token to the `hound_ca_token` secrets file. |
| [`smoke-test.sh`](smoke-test.sh) | CI's smoke test: brings the stack up, runs the runner once with the fake collector, tears down. |

## What talks to what

| Network | Members | Why |
| --- | --- | --- |
| `web` | Caddy, frontend, backend, worker | Caddy is the only service with a published port (`CA_HTTPS_PORT`). |
| `data` (internal) | PostgreSQL, backend, worker | The database is unreachable from anything else, and has no internet. |
| `collect` | runner, backend | The runner reaches only the CISO Assistant API (`http://backend:8000/api/`) and the internet, for the collectors' cloud APIs. It publishes no ports. |

The runner runs as a non-root user on a read-only root filesystem with no capabilities. Its evidence, run records and manual drop folders live in the `runner-data` volume at `/var/lib/evidence-hound`; your compliance repo is mounted read-only at `/config`.

## Secrets

Secrets are Docker secrets files in `HOUND_SECRETS_DIR` (default `/etc/evidence-hound/secrets`), never variables and never in git:

| File | Used by | How to make it |
| --- | --- | --- |
| `postgres_password` | PostgreSQL (`POSTGRES_PASSWORD_FILE`), backend, worker | `openssl rand -hex 24` |
| `django_secret_key` | backend, worker | `openssl rand -hex 32` |
| `hound_ca_token` | runner (`HOUND_CA_TOKEN_FILE`), dispatcher | `runner-token.sh`, after the stack is up |
| `m365_client_certificate` | runner (the Microsoft 365 collector's `file:` secret) | [Microsoft 365 collector guide](../../docs/collectors/m365.md); an empty file until then |

CISO Assistant reads its database password and secret key from variables, so its containers read the files at start and export them only inside the container. The runner understands Docker's `_FILE` convention for any variable: `HOUND_CA_TOKEN_FILE=/run/secrets/hound_ca_token` sets `HOUND_CA_TOKEN` from the file.

The secrets folder should be owned by root with mode `0700`; the files inside are `0444` so the containers' non-root users can read their bind-mounted copies.

## Install

On a VM with Docker Engine and the Compose plugin, a DNS name for it, and your private compliance repo cloned to `/opt/compliance` (the tutorial covers creating it):

```bash
sudo install -d -m 0700 /etc/evidence-hound/secrets
cd /etc/evidence-hound/secrets
sudo sh -c 'umask 022; openssl rand -hex 24 > postgres_password; openssl rand -hex 32 > django_secret_key; : > hound_ca_token; : > m365_client_certificate'

git clone https://github.com/tnbozman/evidence-hound.git /opt/evidence-hound
cd /opt/evidence-hound/deploy/compose
cp .env.example .env        # then edit CA_HOSTNAME, TZ, HOUND_RUNNER_IMAGE and the paths
docker compose up -d --wait postgres backend worker frontend caddy
```

The first start migrates the database and loads CISO Assistant's libraries, which takes a few minutes. Then:

1. Create the first administrator: `docker compose exec backend python manage.py createsuperuser`.
2. Sign in at `https://<CA_HOSTNAME>`. Caddy's internal certificate is not trusted by browsers until you install its root: `docker compose exec caddy cat /data/caddy/pki/authorities/local/root.crt > caddy-root.crt` and add it to your devices' trust stores, or use your own certificate (see the comments in the [`Caddyfile`](Caddyfile)).
3. Issue the runner's token: `sudo ./runner-token.sh hound-runner@example.com /etc/evidence-hound/secrets`.
4. Apply your scope and run the collectors once:

```bash
docker compose run --rm runner scope apply /config/scope --config /config/hound.yaml
docker compose run --rm runner run --config /config/hound.yaml
docker compose run --rm runner status --config /config/hound.yaml
```

5. Start the schedule: `docker compose up -d runner`. The runner runs `hound serve`, which runs each collector on its `schedule` in `scheduler.time_zone` ([running collectors](../../docs/running.md#on-a-schedule)); `docker compose logs runner` shows each collector's next run. To use the host's cron or a systemd timer instead, see the same page.

Paths in `hound.yaml` are as the runner sees them: `/config/...` for your repo, `/var/lib/evidence-hound/...` for evidence and manual drop folders, and `/run/secrets/<name>` for secrets.

## Optional Kafka transport

`docker compose --profile kafka up -d` adds Redpanda and CISO Assistant's official dispatcher, built from the same release (`CA_VERSION`). To publish through it, set `HOUND_TRANSPORT=kafka` and `HOUND_KAFKA_BOOTSTRAP_SERVERS=redpanda:9092` for the runner in a `compose.override.yml` (see [publishing](../../docs/publishing.md)).

## Upgrade

1. Snapshot the VM.
2. Change `CA_VERSION` or `HOUND_RUNNER_IMAGE` in `.env` (in your private repo, through a pull request).
3. `docker compose pull && docker compose up -d --wait`.
