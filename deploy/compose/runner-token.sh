#!/usr/bin/env bash
# Issue the runner's CISO Assistant token: creates the service account if needed, replaces its previous token,
# and writes the new one to <secrets-dir>/hound_ca_token without printing it. The file is mode 0444 so the
# runner's non-root user can read the bind-mounted secret; keep <secrets-dir> itself root-only (0700). Run from deploy/compose
# with the stack up; docker compose's own settings (.env, COMPOSE_FILE, COMPOSE_PROJECT_NAME) apply.
#
# Usage: deploy/compose/runner-token.sh <service-account-email> <secrets-dir> [days, default 365]
# Then restart nothing: the runner reads the secrets file on its next run.
set -euo pipefail

email="${1:?usage: runner-token.sh <service-account-email> <secrets-dir> [days]}"
secrets_dir="${2:?usage: runner-token.sh <service-account-email> <secrets-dir> [days]}"
days="${3:-365}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

output="$("$here/manage.sh" -e HOUND_SERVICE_EMAIL="$email" -e HOUND_TOKEN_DAYS="$days" shell <"$here/runner-token.py")"
grep -v '^HOUND_CA_TOKEN=' <<<"$output" || true
token="$(grep '^HOUND_CA_TOKEN=' <<<"$output" | tail -n 1 | cut -d= -f2-)"
[[ -n "$token" ]] || { echo "runner-token: CISO Assistant issued no token" >&2; exit 1; }

umask 077
printf '%s\n' "$token" >"$secrets_dir/hound_ca_token.new"
chmod 0444 "$secrets_dir/hound_ca_token.new"
mv "$secrets_dir/hound_ca_token.new" "$secrets_dir/hound_ca_token"
echo "runner-token: wrote $secrets_dir/hound_ca_token"
