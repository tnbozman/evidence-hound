#!/usr/bin/env bash
# Interactive local trial: from the dev container, with CISO Assistant already running, run Evidence Hound in a
# container next to it and walk through the example organisation, manual evidence and controls, then Sophos.
# Guide: docs/tutorials/local-trial.md
#
# Usage: make trial (or scripts/trial.sh). Safe to stop and run again: finished steps are skipped.
# Environment:
#   HOUND_TRIAL_DIR        the trial workspace (default .dev/trial, git-ignored)
#   HOUND_TRIAL_IMAGE      the runner image to build and use (default evidence-hound-runner:trial)
#   HOUND_TRIAL_YES=1      answer yes to every question (for unattended checks); stops before Sophos
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=scripts/lib/trial.sh
source "$root/scripts/lib/trial.sh"

ws="${HOUND_TRIAL_DIR:-$root/.dev/trial}"
image="${HOUND_TRIAL_IMAGE:-evidence-hound-runner:trial}"
yes="${HOUND_TRIAL_YES:-0}"
state="$ws/state"

if [[ -t 1 ]]; then bold=$'\033[1m'; dim=$'\033[2m'; plain=$'\033[0m'; else bold=""; dim=""; plain=""; fi

step() { printf '\n%s== %s ==%s\n' "$bold" "$*" "$plain"; }
say() { printf '%s\n' "$*"; }
show() { printf '%s$ %s%s\n' "$dim" "$*" "$plain"; }
die() { printf 'trial: %s\n' "$*" >&2; exit 1; }

# ask QUESTION -> 0 for yes; q stops the trial
ask() {
  [[ "$yes" == 1 ]] && { say "$1 [Y/n/q] y"; return 0; }
  local answer
  read -r -p "$1 [Y/n/q] " answer || answer=q
  case "${answer,,}" in
    "" | y | yes) return 0 ;;
    q | quit) say "Stopped. Run make trial again to carry on from here."; exit 0 ;;
    *) return 1 ;;
  esac
}

done_already() { [[ -f "$state/$1.done" ]]; }
mark_done() { mkdir -p "$state"; touch "$state/$1.done"; }

# hound TTY ARGS... runs hound in a throwaway container on CISO Assistant's network, showing the command first
hound() {
  local tty="$1"
  shift
  local mode argv
  mode="$(cat "$state/ca-mode")"
  mapfile -t argv < <(trial_hound_argv "$image" "$(trial_network_args "$mode")" "$ws" "$(id -u):$(id -g)" "$(trial_ca_url "$mode")" "$tty" "$@")
  show "hound $*"
  "${argv[@]}"
}

answers_ok() { curl -fsS -m 3 -o /dev/null "$1" 2>/dev/null; }
port_busy() { (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null; }

backend_network() {
  local id
  id="$(docker ps --format '{{.ID}} {{.Image}}' | awk '$2 ~ /ciso-assistant-community\/backend/ {print $1; exit}')"
  [[ -n "$id" ]] || return 0
  docker inspect -f '{{range $name, $_ := .NetworkSettings.Networks}}{{$name}}{{"\n"}}{{end}}' "$id" | head -n 1
}

preflight() {
  step "1. Preflight: Docker, CISO Assistant and ports"
  docker info >/dev/null 2>&1 || die "Docker does not answer. Start Docker (or rebuild the dev container) and run make trial again."
  say "Docker answers."

  local bundled=no local_api=no api=free ui=free network mode
  answers_ok http://backend:8000/api/health/ && bundled=yes
  network="$(backend_network)"
  answers_ok http://localhost:8000/api/health/ && local_api=yes
  port_busy 8000 && api=busy
  port_busy 8443 && ui=busy
  mode="$(trial_choose_ca "$bundled" "$network" "$local_api" "$api" "$ui")"

  case "$mode" in
    bundled) say "Found the dev container's CISO Assistant at http://backend:8000. Reusing it; nothing new is started." ;;
    "network "*) say "Found a running CISO Assistant on the Docker network ${mode#network }. Reusing it; nothing new is started." ;;
    local) say "Found a CISO Assistant answering at http://localhost:8000. Reusing it; nothing new is started." ;;
    "blocked "*)
      say "No CISO Assistant is running, and a port it needs is taken:"
      local port
      for port in ${mode#blocked }; do
        trial_port_holder "$port" "$(docker ps --filter "publish=$port" --format '{{.Names}}' | paste -sd, -)"
      done
      die "Free the port (or start your CISO Assistant) and run make trial again."
      ;;
    start)
      say "No CISO Assistant is running, and ports 8000 and 8443 are free."
      show "make ca-up"
      ask "Start a local CISO Assistant with make ca-up (a few minutes the first time)?" || die "The trial needs a CISO Assistant."
      make -C "$root" ca-up
      mode="network hound-ciso-assistant_default"
      ;;
  esac
  mkdir -p "$state"
  printf '%s\n' "$mode" >"$state/ca-mode"
}

token() {
  local env_file="$root/.dev/ca.env"
  if [[ ! -s "$env_file" && -s "${HOUND_SHARED_CA_ENV:-/run/hound/ca.env}" ]]; then
    bash "$root/scripts/dev-ca-env.sh"
  fi
  [[ -s "$env_file" ]] || die "$env_file is missing, so there is no API token. Run make ca-up (or scripts/dev-ca-env.sh in the dev container)."
  mkdir -p "$ws/secrets"
  chmod 700 "$ws/secrets"
  (umask 077 && sed -n 's/^HOUND_CA_TOKEN=//p' "$env_file" | tail -n 1 >"$ws/secrets/ca_token")
  [[ -s "$ws/secrets/ca_token" ]] || die "$env_file has no HOUND_CA_TOKEN line."
  say "The API token is in $ws/secrets/ca_token (mode 600). It is never printed or put on a command line."
}

build_image() {
  step "2. Build the Evidence Hound runner image from this checkout"
  if done_already image && docker image inspect "$image" >/dev/null 2>&1; then say "Already built: $image."; return; fi
  show "make image HOUND_IMAGE=$image"
  ask "Build it now (a few minutes the first time)?" || die "The trial runs hound from this image."
  make -C "$root" image HOUND_IMAGE="$image"
  mark_done image
}

workspace() {
  step "3. Trial workspace in ${ws#"$root"/}"
  if ! done_already workspace; then
    mkdir -p "$ws/repo" "$ws/evidence" "$ws/manual/example-incident-register" "$ws/manual/example-backup-restore-test" "$ws/inbox"
    cp -R "$root/examples/compliance-repo/." "$ws/repo/"
    trial_render_config /trial no >"$ws/repo/hound.yaml"
    mark_done workspace
  fi
  token
  say "Example Pty Ltd's compliance repo is copied to repo/, with a trial hound.yaml (manual collectors, no Microsoft 365)."
  hound no config validate hound.yaml
}

example_organisation() {
  step "4. Apply the example organisation (scope as configuration)"
  say "The scope folder declares Example Pty Ltd's domain, controls, documents and audits. Plan first, then apply."
  hound no scope validate scope --config hound.yaml
  hound no scope plan scope --config hound.yaml
  if done_already scope; then say "Applied before; the plan above shows anything that changed since."; fi
  if ask "Apply this plan to CISO Assistant?"; then
    hound no scope apply scope --config hound.yaml >"$ws/scope-apply.json"
    say "Applied. The report is in ${ws#"$root"/}/scope-apply.json."
    mark_done scope
  fi
}

manual_evidence() {
  step "5. Manual evidence and controls"
  local today file
  today="$(date +%F)"
  file="restore-test-$today.txt"
  printf 'Example Pty Ltd restore test, %s\nRestored the example file share to a scratch folder and compared checksums: all files matched.\n' "$today" >"$ws/inbox/$file"
  say "A system owner has sent the GRC team an example restore test record: inbox/$file."
  say "Upload it into the example-backup-restore-test source and attest it (answer the prompts: y, your name, pass, a note)."
  if [[ "$yes" == 1 ]]; then
    printf 'y\nSam Example\npass\nTrial restore test.\n' | hound no manual upload --config hound.yaml --source example-backup-restore-test "/trial/inbox/$file"
  else
    hound yes manual upload --config hound.yaml --source example-backup-restore-test "/trial/inbox/$file"
  fi
  say "Now collect it and publish the result to the EX-E8-2026 audit."
  hound no run --config hound.yaml --only example-backup-restore-test
  hound no status --config hound.yaml
  mark_done manual
  cat <<TEXT

Look in CISO Assistant (https://localhost:8443, sign in as admin@example.com):
  - Compliance > Audits > Essential Eight ML2 2026 > ISM-1515: the result, the observation naming who attested,
    and the evidence example-backup-restore-test/$file.
  - Operations > Applied controls: AC-BACKUP (declared in scope/controls.yaml) and AC-AUTO-EVIDENCE, the
    health control, which now has this run's health evidence.
  - Evidence: the run's evidence, with SHA-256 hashes in its description.
TEXT
  ask "Seen it? Carry on to Sophos?" || { say "Stopped before Sophos. Run make trial again to add it."; exit 0; }
}

sophos() {
  step "6. Add your Sophos Central tenant"
  if [[ "$yes" == 1 ]]; then say "Unattended run: stopping before Sophos, which needs your credential."; return; fi
  say "You need a Sophos Central API credential with the Service Principal Read-Only role (docs/collectors/sophos.md)."
  say "The client ID and secret are saved under ${ws#"$root"/}/secrets (git-ignored, mode 600) and never printed."
  local client_id secret firmware stale
  if [[ ! -s "$ws/secrets/sophos_client_secret" ]]; then
    read -r -p "Client ID: " client_id
    trial_valid_client_id "$client_id" || die "That does not look like a client ID (letters, digits and dashes)."
    read -r -s -p "Client secret (not shown): " secret
    echo
    [[ -n "$secret" ]] || die "The client secret is empty."
    (umask 077 && printf '%s' "$secret" >"$ws/secrets/sophos_client_secret")
    unset secret
    printf '%s\n' "$client_id" >"$state/sophos-client-id"
  fi
  client_id="$(cat "$state/sophos-client-id")"
  read -r -p "Firewall firmware versions still in support [21.5, 22.0]: " firmware
  firmware="${firmware:-21.5, 22.0}"
  trial_valid_firmware "$firmware" || die "Firmware versions look like 21.5, 22.0."
  read -r -p "Days an endpoint may go unseen before it is stale [14]: " stale
  stale="${stale:-14}"
  [[ "$stale" =~ ^[0-9]+$ ]] || die "Days must be a whole number."
  trial_render_config /trial yes "$client_id" "$firmware" "$stale" >"$ws/repo/hound.yaml"
  say "Added a sophos collector to repo/hound.yaml."

  hound no config validate hound.yaml --check-secrets
  say "A dry run signs in, reads endpoints and firewalls, stores the evidence locally and plans the updates, sending nothing."
  hound no run --config hound.yaml --only sophos --dry-run
  if ask "Publish the Sophos results to CISO Assistant?"; then
    hound no run --config hound.yaml --only sophos
    hound no status --config hound.yaml
    mark_done sophos
    say "Look at the EX-E8-2026 audit in CISO Assistant for the requirements the Sophos checks map to."
  fi
  say "Paste any output you want reviewed with the identifiers removed: no tenant IDs, host names, IP addresses or serial numbers."
}

preflight
build_image
workspace
example_organisation
manual_evidence
sophos
step "Done"
say "Run make trial again to repeat any step, or make trial-clean to remove the workspace (CISO Assistant keeps running)."
