#!/usr/bin/env bash
# Seed the local CISO Assistant started by `make ca-up` and write its API URL and token to
# .dev/ca.env (git-ignored) for `hound publish`. Safe to run again; each run issues a new token.
#
# Usage: scripts/ca-seed.sh
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose=(docker compose -f "$root/dev/ciso-assistant/compose.yml")
env_file="$root/.dev/ca.env"

"${compose[@]}" exec -T backend bash /seed/seed.sh \
  | bash "$root/dev/ciso-assistant/seed/write-env.sh" "http://localhost:8000/api/" "$env_file"
echo "ca-seed: wrote $env_file. Open https://localhost:8443 and sign in as admin@example.com."
