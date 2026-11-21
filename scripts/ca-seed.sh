#!/usr/bin/env bash
# Seed the local CISO Assistant started by `make ca-up` and write its API URL and token to
# .dev/ca.env (git-ignored) for `hound publish`. Safe to run again; each run issues a new token.
#
# Usage: scripts/ca-seed.sh
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose=(docker compose -f "$root/dev/ciso-assistant/compose.yml")
env_file="$root/.dev/ca.env"

output="$("${compose[@]}" exec -T backend bash /seed/seed.sh)"
grep -v '^HOUND_CA_TOKEN=' <<<"$output" || true
token_line="$(grep '^HOUND_CA_TOKEN=' <<<"$output" | tail -n 1)"
[[ -n "$token_line" ]] || { echo "ca-seed: the seed script printed no token" >&2; exit 1; }

mkdir -p "$(dirname "$env_file")"
umask 077
{
  echo "HOUND_CA_URL=http://localhost:8000/api/"
  echo "$token_line"
} >"$env_file"
echo "ca-seed: wrote $env_file. Open https://localhost:8443 and sign in as admin@example.com."
