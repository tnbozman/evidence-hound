#!/usr/bin/env bash
# Runs on the host (devcontainer.json initializeCommand) before the containers start. Writes the
# local CISO Assistant's Django secret key to .devcontainer/.env (git-ignored) for compose.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
key="$(bash "$root/scripts/ca-secret-key.sh")"
umask 077
printf 'HOUND_CA_SECRET_KEY=%s\n' "$key" >"$root/.devcontainer/.env"
echo "initialize: wrote .devcontainer/.env"
