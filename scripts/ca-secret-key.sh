#!/usr/bin/env bash
# Prints the Django secret key for the local CISO Assistant, creating it on first use.
# The key lives in .dev/ (git-ignored) so sessions survive restarts without a committed secret.
set -euo pipefail
key_file="$(dirname "$0")/../.dev/ca-secret-key"
if [[ ! -s "$key_file" ]]; then
  mkdir -p "$(dirname "$key_file")"
  (umask 077 && od -An -N32 -tx1 /dev/urandom | tr -d ' \n' >"$key_file")
fi
cat "$key_file"
