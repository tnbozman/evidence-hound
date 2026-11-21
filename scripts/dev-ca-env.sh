#!/usr/bin/env bash
# Runs when the dev container starts. Waits for the ca-seed service to write the local CISO
# Assistant's URL and token to the shared volume, then copies them to .dev/ca.env (git-ignored).
#
# Usage: scripts/dev-ca-env.sh
# Environment: HOUND_SHARED_CA_ENV (default /run/hound/ca.env), HOUND_DEV_DIR (default .dev),
#              HOUND_CA_WAIT_SECONDS (default 600).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
shared="${HOUND_SHARED_CA_ENV:-/run/hound/ca.env}"
dev_dir="${HOUND_DEV_DIR:-$root/.dev}"
wait_seconds="${HOUND_CA_WAIT_SECONDS:-600}"

deadline=$((SECONDS + wait_seconds))
until [[ -s "$shared" ]]; do
  if ((SECONDS >= deadline)); then
    echo "dev-ca-env: CISO Assistant was not seeded after ${wait_seconds}s; check 'docker compose logs ca-seed' and run scripts/dev-ca-env.sh again" >&2
    exit 1
  fi
  sleep 1
done

mkdir -p "$dev_dir"
(umask 077 && cp "$shared" "$dev_dir/ca.env")
chmod 600 "$dev_dir/ca.env"
echo "dev-ca-env: wrote $dev_dir/ca.env. Load it with: set -a && . .dev/ca.env && set +a"
