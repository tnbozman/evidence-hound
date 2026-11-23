#!/usr/bin/env bash
# hound for a coding agent in the local trial: runs the trial's runner image against the trial's CISO Assistant, in the
# folder it is called from. make trial links it as .dev/trial/bin/hound; put that folder on PATH in the trial repo.
# Guide: docs/tutorials/local-trial.md, section 5.
#
# Usage: hound ARGS... (from inside the trial workspace, usually .dev/trial/repo)
# Environment: HOUND_TRIAL_DIR (default .dev/trial), HOUND_TRIAL_IMAGE (default evidence-hound-runner:trial)
set -euo pipefail

root="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")/.." && pwd)"
# shellcheck source=scripts/lib/trial.sh
source "$root/scripts/lib/trial.sh"

ws="$(cd "${HOUND_TRIAL_DIR:-$root/.dev/trial}" && pwd -P)"
image="${HOUND_TRIAL_IMAGE:-evidence-hound-runner:trial}"
[[ -s "$ws/state/ca-mode" ]] || { echo "hound (trial): run make trial first; it finds CISO Assistant and writes $ws/state/ca-mode." >&2; exit 2; }
mode="$(cat "$ws/state/ca-mode")"
workdir="$(trial_workdir "$ws" "$(pwd -P)")" || { echo "hound (trial): run it from inside $ws (the trial repo is $ws/repo); hound's container sees only that folder." >&2; exit 2; }
tty=no
[[ -t 0 && -t 1 ]] && tty=yes

mapfile -t argv < <(trial_hound_argv_in "$workdir" "$image" "$(trial_network_args "$mode")" "$ws" "$(id -u):$(id -g)" "$(trial_ca_url "$mode")" "$tty" "$@")
exec "${argv[@]}"
