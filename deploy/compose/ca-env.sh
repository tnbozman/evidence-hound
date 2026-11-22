#!/bin/sh
# Runs a command in a CISO Assistant container with its database password and Django secret key exported from the
# Docker secrets files. CISO Assistant reads both only from variables, so they are exported here, inside the container,
# and never set in compose.yml or .env. Mounted read-only at /hound/ca-env.sh; the backend and worker start through it,
# and manage.sh runs management commands through it.
set -eu
secrets="${HOUND_SECRETS_PATH:-/run/secrets}"
POSTGRES_PASSWORD="$(cat "$secrets/postgres_password")"
DJANGO_SECRET_KEY="$(cat "$secrets/django_secret_key")"
export POSTGRES_PASSWORD DJANGO_SECRET_KEY
exec "$@"
