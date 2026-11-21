#!/usr/bin/env bash
# Reads the seed script's output on stdin, prints every line except the token, and writes the
# API URL and the last token to an env file, readable by its owner only unless HOUND_CA_ENV_MODE
# says otherwise (the dev container's shared volume uses 644 so another user can copy it).
#
# Usage: seed.sh | write-env.sh <api-url> <env-file>
set -euo pipefail

url="$1"
env_file="$2"

output="$(cat)"
grep -v '^HOUND_CA_TOKEN=' <<<"$output" || true
token_line="$(grep '^HOUND_CA_TOKEN=' <<<"$output" | tail -n 1 || true)"
[[ -n "$token_line" ]] || { echo "ca-seed: the seed script printed no token" >&2; exit 1; }

mkdir -p "$(dirname "$env_file")"
umask 077
printf 'HOUND_CA_URL=%s\n%s\n' "$url" "$token_line" >"$env_file"
chmod "${HOUND_CA_ENV_MODE:-600}" "$env_file"
