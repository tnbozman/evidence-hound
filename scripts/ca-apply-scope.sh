#!/usr/bin/env bash
# Write the example scope (examples/compliance-repo/scope) to the local CISO Assistant with
# `hound scope apply`, using the URL and token in .dev/ca.env. scripts/ca-seed.sh and the dev
# container call it after seeding. Safe to run again: a second apply writes nothing.
#
# Usage: scripts/ca-apply-scope.sh
# Environment: HOUND_DEV_DIR (default .dev), HOUND_CA_URL overrides the env file's URL.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${HOUND_DEV_DIR:-$root/.dev}/ca.env"
[[ -s "$env_file" ]] || { echo "ca-apply-scope: $env_file is missing; seed CISO Assistant first (make ca-up)" >&2; exit 1; }

url_override="${HOUND_CA_URL:-}"
set -a
# shellcheck source=/dev/null
. "$env_file"
set +a
[[ -z "$url_override" ]] || HOUND_CA_URL="$url_override"

# The repo's own config tells apply which rows its targets' mappings compute, so `unlisted` leaves them alone, and names
# the collectors its evidence plan uses.
# The report (JSON) goes next to the env file; it lists every change and what happened to it.
report="$(dirname "$env_file")/scope-apply.json"
if ! dotnet run --project "$root/src/EvidenceHound.Cli" -- scope apply "$root/examples/compliance-repo/scope" \
  --config "$root/examples/compliance-repo/hound.yaml" >"$report"; then
  echo "ca-apply-scope: hound scope apply failed; see $report" >&2
  # The changes that failed, so a CI log shows why without the report.
  command -v jq >/dev/null && jq -c '[.. | objects | select((.problems? // []) | length > 0)]' "$report" >&2 || true
  exit 1
fi
echo "ca-apply-scope: applied examples/compliance-repo/scope to $HOUND_CA_URL (report: $report)"
