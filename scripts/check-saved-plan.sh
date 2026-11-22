#!/usr/bin/env bash
# s08-07 functional check, after the trial (make trial): saved plans against the trial's real CISO Assistant.
# Plans with --detailed-exitcode and --out, shows that apply refuses the saved plan once a scope file changes,
# then plans again and applies the new file. Prints each command and its exit code; fails on any surprise.
#
# Usage: scripts/check-saved-plan.sh (same environment as scripts/trial.sh)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=scripts/lib/trial.sh
source "$root/scripts/lib/trial.sh"

ws="${HOUND_TRIAL_DIR:-$root/.dev/trial}"
image="${HOUND_TRIAL_IMAGE:-evidence-hound-runner:trial}"
[[ -f "$ws/state/ca-mode" ]] || { echo "check-saved-plan: run make trial first" >&2; exit 1; }
mode="$(cat "$ws/state/ca-mode")"
plan=/trial/check-plan.json
controls="$ws/repo/scope/controls.yaml"

# hound ARGS... runs hound as the trial does, prints the command, and leaves its exit code in $code
hound() {
  local argv
  mapfile -t argv < <(trial_hound_argv "$image" "$(trial_network_args "$mode")" "$ws" "$(id -u):$(id -g)" "$(trial_ca_url "$mode")" no "$@")
  printf '$ hound %s\n' "$*"
  code=0
  "${argv[@]}" >"$ws/check-out.txt" 2>"$ws/check-err.txt" || code=$?
  printf 'exit %s\n' "$code"
}

expect() {
  [[ "$code" == "$1" ]] || { echo "check-saved-plan: expected exit $1, got $code" >&2; cat "$ws/check-err.txt" >&2; exit 1; }
}

hound scope plan scope --config hound.yaml --detailed-exitcode --out "$plan"
[[ "$code" == 0 || "$code" == 2 ]] || expect 2
grep -m1 "to create" "$ws/check-out.txt" || true

cp "$controls" "$ws/controls.yaml.bak"
printf '# changed after the plan\n' >>"$controls"
hound scope apply "$plan"
mv "$ws/controls.yaml.bak" "$controls"
expect 3
grep -o 'is stale: [^.]*' "$ws/check-err.txt"

hound scope plan scope --config hound.yaml --out "$plan"
expect 0
hound scope apply "$plan"
expect 0
grep -E '"(written|failed|skipped)"' "$ws/check-out.txt"
rm -f "$ws/check-plan.json" "$ws/check-out.txt" "$ws/check-err.txt"
echo "saved plans: ok"
