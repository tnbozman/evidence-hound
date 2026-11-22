# Deployment settings

Example Pty Ltd's settings for Evidence Hound's [reference Compose stack](../../../deploy/compose/README.md). The [first deployment tutorial](../../../docs/tutorials/first-deployment.md) walks through using them.

| File | What it holds |
| --- | --- |
| [`deploy.env`](deploy.env) | The stack's settings: CISO Assistant release and host name, time zone, runner image tag, and where this repo and the secrets files are on the host. Linked as the stack's `.env`. |
| [`compose.override.yml`](compose.override.yml) | Organisation-specific additions to the stack, merged over it (`COMPOSE_FILE`). |

Secrets never go here. They are files in `HOUND_SECRETS_DIR` on the host (`postgres_password`, `django_secret_key`, `hound_ca_token`, `m365_client_certificate`), referenced from `hound.yaml` as `file:/run/secrets/<name>`.

In your own repo, point these links at the Evidence Hound version you run.
