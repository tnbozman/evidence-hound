#!/usr/bin/env bash
# Smoke test for the reference Compose stack: bring it up from .env.example with throwaway secrets, wait for
# health, seed a service account and the example scope, run the runner once with the fake collector, check
# `hound status` and HTTPS through Caddy, then tear everything down (volumes too).
#
# Usage: deploy/compose/smoke-test.sh        (make compose-smoke)
# Environment: HOUND_RUNNER_IMAGE to test a locally built runner image (default: build it from this checkout).
# Needs Docker with Compose, curl and openssl. Everything it creates is fictional and deleted on exit.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
dir="$root/deploy/compose"
work="$(mktemp -d)"
project="hound-smoke-$$"

compose() { docker compose --project-name "$project" --project-directory "$dir" -f "$dir/compose.yml" -f "$work/smoke.override.yml" --env-file "$work/.env" "$@"; }

cleanup() {
  status=$?
  if [[ $status -ne 0 ]]; then
    compose ps -a || true
    compose logs --no-color --tail 80 || true
  fi
  compose down --volumes --remove-orphans >/dev/null 2>&1 || true
  rm -rf "$work"
  exit "$status"
}
trap cleanup EXIT

image="${HOUND_RUNNER_IMAGE:-}"
if [[ -z "$image" ]]; then
  image="evidence-hound-runner:smoke"
  docker build -q -f "$root/src/EvidenceHound.Cli/Dockerfile" -t "$image" "$root" >/dev/null
fi

# Settings: the documented example, with a port that is free on CI runners and the runner image under test.
sed -e "s|^HOUND_RUNNER_IMAGE=.*|HOUND_RUNNER_IMAGE=$image|" \
    -e "s|^CA_HTTPS_PORT=.*|CA_HTTPS_PORT=18443|" \
    -e "s|^HOUND_SECRETS_DIR=.*|HOUND_SECRETS_DIR=$work/secrets|" \
    -e "s|^HOUND_CONFIG_DIR=.*|HOUND_CONFIG_DIR=$root/examples/compliance-repo|" \
    "$dir/.env.example" >"$work/.env"

# Throwaway secrets, readable by the containers' non-root users (only on this disposable runner).
mkdir -p "$work/secrets"
openssl rand -hex 24 >"$work/secrets/postgres_password"
openssl rand -hex 32 >"$work/secrets/django_secret_key"
: >"$work/secrets/hound_ca_token"
: >"$work/secrets/m365_client_certificate"
chmod 0444 "$work/secrets/"*

# The smoke config sits next to the example compliance repo, which the stack mounts at /config.
cat >"$work/smoke.override.yml" <<YAML
services:
  runner:
    volumes:
      - $dir/smoke:/config-smoke:ro
YAML

echo "smoke: starting the stack"
compose up -d --wait --wait-timeout 600 postgres backend worker frontend caddy

echo "smoke: seeding a service account, its token and the example staff users"
token="$(compose exec -T backend python manage.py shell <"$root/dev/ciso-assistant/seed/seed.py" | grep '^HOUND_CA_TOKEN=' | tail -n 1 | cut -d= -f2-)"
[[ -n "$token" ]] || { echo "smoke: the seed printed no token" >&2; exit 1; }
printf '%s\n' "$token" >"$work/secrets/hound_ca_token"

echo "smoke: applying the example scope through the runner image"
compose run --rm --no-deps runner scope apply /config/scope --config /config/hound.yaml >"$work/scope-apply.json"

echo "smoke: running the runner once with the fake collector"
compose run --rm --no-deps runner run --config /config-smoke/hound.yaml
compose run --rm --no-deps runner status --config /config-smoke/hound.yaml | tee "$work/status.txt"
grep -q 'smoke-fake' "$work/status.txt"

echo "smoke: checking HTTPS through Caddy"
curl -fsS --cacert <(compose exec -T caddy cat /data/caddy/pki/authorities/local/root.crt) \
  --resolve "localhost:18443:127.0.0.1" -o /dev/null "https://localhost:18443/api/health/"
curl -fsS --cacert <(compose exec -T caddy cat /data/caddy/pki/authorities/local/root.crt) \
  --resolve "localhost:18443:127.0.0.1" -o /dev/null "https://localhost:18443/login"

echo "smoke: passed"
