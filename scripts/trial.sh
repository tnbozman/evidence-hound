#!/usr/bin/env bash
# Interactive local trial: from the dev container, with CISO Assistant already running, run Evidence Hound in a
# container next to it. The guided steps: a new organisation (hound init), a hand-over to your coding agent, which
# onboards the Sophos perimeter firewall (hound agent setup), then the Sophos Central collector and Microsoft 365.
# Guide: docs/tutorials/local-trial.md
#
# Usage: make trial (or scripts/trial.sh). Safe to stop and run again: finished steps are skipped.
# Environment:
#   HOUND_TRIAL_DIR        the trial workspace (default .dev/trial, git-ignored)
#   HOUND_TRIAL_IMAGE      the runner image to build and use (default evidence-hound-runner:trial)
#   HOUND_TRIAL_YES=1      answer yes to every question (for unattended checks); stops before Sophos and M365
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
  show "hound$(printf ' %s' "${@@Q}" | sed "s/ '\([^' ]*\)'/ \1/g")"
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
  mkdir -p "$ws/repo" "$ws/evidence" "$ws/manual" "$ws/inbox" "$ws/specs"
  token
  say "hound writes the organisation's compliance repo into repo/, evidence into evidence/, and secrets into secrets/."
}

# audit_ref -> the audit hound init wrote into the trial config
audit_ref() { sed -n 's/^  compliance_assessment: *//p' "$ws/repo/hound.yaml" | tr -d '"'; }

organisation() {
  step "4. A new organisation: hound init, then apply its scope"
  if [[ -s "$ws/repo/hound.yaml" ]]; then
    say "Already written: repo/hound.yaml and repo/scope. Remove ${ws#"$root"/} (make trial-clean) to start again."
  else
    say "hound init asks about the organisation. Press Enter to take each default: the fictional Example Pty Ltd at maturity"
    say "level 2, audited by quarter. The plan below creates its domain, perimeter, health control and this quarter's audit."
    if [[ "$yes" == 1 ]]; then
      hound no init /trial/repo --evidence-root /trial/evidence --yes
    else
      hound yes init /trial/repo --evidence-root /trial/evidence
    fi
  fi
  hound no config validate hound.yaml
  apply_scope scope
  agent_setup
}

# agent_setup writes the coding agent's files in the trial repo and links bin/hound, which the agent and you run hound with
agent_setup() {
  done_already agent && return 0
  say "hound agent setup writes the skills and agent files Claude Code and GitHub Copilot read in the trial repo."
  hound no agent setup --assistant both
  mkdir -p "$ws/bin"
  ln -sfn "$root/scripts/trial-hound.sh" "$ws/bin/hound"
  mark_done agent
  cat <<TEXT

From here your coding agent drafts each stage and you approve it. Start it in a second terminal and keep it open:
  cd ${ws#"$root"/}/repo && export PATH="\$PWD/../bin:\$PATH"
  claude        (or open the folder in VS Code and pick the hound-onboarding agent in Copilot)
${ws#"$root"/}/bin/hound runs this image against this CISO Assistant. hound's container sees only ${ws#"$root"/}: put the
firewall's API specification in ${ws#"$root"/}/specs and the vendor documents in ${ws#"$root"/}/inbox.
TEXT
}

# apply_scope STEP validates and plans the scope, then asks before applying it; STEP marks it done
apply_scope() {
  say "The scope folder declares the domain, systems, controls and the audit. Plan first, then apply."
  hound no scope validate scope --config hound.yaml
  hound no scope plan scope --config hound.yaml --out /trial/scope-plan.json
  if ask "Apply this plan to CISO Assistant?"; then
    say "Apply writes exactly the plan you saw, and refuses it if the scope or CISO Assistant changed since."
    hound no scope apply /trial/scope-plan.json >"$ws/scope-apply.json"
    rm -f "$ws/scope-plan.json"
    say "Applied. The report is in ${ws#"$root"/}/scope-apply.json."
    mark_done "$1"
  fi
}

# add WIZARD ARGS... runs one hound add wizard: with every answer given when unattended, asking otherwise
add() {
  if [[ "$yes" == 1 ]]; then hound no add "$@" --yes; else hound yes add "$@"; fi
}

# The system the trial onboards, and the stages an agent drafts. Each stage: what it is, the request for the agent, a wait until its
# draft is in the repo, the person's approval where one is needed, then validate, plan and apply.
system=SOPHOS-NETWORK

# stage NAME NUMBER TITLE APPROVE(yes|no) REPLAY LOOK
stage() {
  local name="$1" number="$2" title="$3" approve="$4" replay="$5" look="$6" series
  step "$number. $title"
  series="$(audit_ref)"
  if done_already "$name"; then
    say "Done already. Remove ${ws#"$root"/}/state/$name.done to do it again."
    return 0
  fi
  if [[ "$yes" == 1 ]]; then
    say "Unattended: replaying the fictional answers an agent and a person would give (examples/trial/README.md)."
    "$replay"
  else
    say "Ask your agent, in its terminal:"
    say ""
    trial_brief "$name" "$series" "$system" | sed 's/^/  /'
    say ""
    until trial_stage_ready "$name" "$ws/repo" "$system"; do
      ask "Has the agent finished this stage?" || say "Waiting for the agent; answer q to stop and run make trial again later."
      trial_stage_ready "$name" "$ws/repo" "$system" || say "Not yet: hound does not see the draft in ${ws#"$root"/}/repo."
    done
    if [[ "$approve" == yes ]]; then
      say "Now you: hound approve shows each draft that needs a person, one at a time. Answer y, n or s."
      hound yes approve --repo .
    fi
  fi
  if [[ "$name" == system ]]; then
    say "The session is in repo/onboarding/$system/session.json; nothing reaches CISO Assistant until its audit is approved."
    mark_done "$name"
    return 0
  fi
  apply_scope "$name"
  done_already "$name" && say "Look in CISO Assistant (https://localhost:8443): $look"
}

replay_third_parties() {
  hound no supplier draft --system "$system" --ref SUP-SOPHOS --name Sophos --solution-ref SOL-SOPHOS-FIREWALL \
    --solution-name "Sophos XGS firewall and Sophos Central" --contract-ref CTR-SOPHOS-2026 --contract-name "Sophos XGS subscription 2026" \
    --level "dependency=3:The perimeter firewall carries all office internet traffic." --level "penetration=2:Sophos Central manages the firewall remotely." \
    --level "maturity=3:Publishes a security advisory programme." --level "trust=3:Long-standing vendor." --kind vendor
  hound no supplier draft --system "$system" --ref SUP-EXAMPLE-MSP --name "Example Managed Services Pty Ltd" --solution-ref SOL-EXAMPLE-MSP-FIREWALL \
    --solution-name "Managed firewall service" --contract-ref CTR-EXAMPLE-MSP-2026 --contract-name "Managed firewall service 2026" \
    --level "dependency=3:Runs the firewall's changes and monitoring." --level "penetration=3:Holds admin access in Sophos Central." --kind msp --for SUP-SOPHOS
  say "Standing in for hound approve: the approved copies in examples/trial/third-parties."
  cp "$root/examples/trial/third-parties/"*.yaml "$ws/repo/scope/third-parties/"
}

replay_system() {
  # The maturity level's groups, as hound init wrote them on the organisation's audit: groups: ["1", "2"]
  local groups=() group
  for group in $(sed -n 's/^groups: \[\(.*\)\]/\1/p' "$ws/repo/scope/audits/"*-e8.yaml | tr -d '",'); do groups+=(--group "$group"); done
  hound no onboard "$system" pending --json --repo . "${groups[@]}" --owner it@example.com \
    --product "Sophos Firewall on an XGS appliance, managed in Sophos Central" --solution SOL-SOPHOS-FIREWALL >/dev/null
}

replay_audit() {
  # Typed into hound onboard as a person would: the answers reach the container on its standard input.
  hound no onboard "$system" --repo . <"$root/examples/onboarding-tutorial/answers/office-firewall.decisions.txt" >/dev/null
}

replay_controls() {
  hound no add control --scope scope --yes --ref-id AC-EXTERNAL-SCAN --name "Daily external vulnerability scan of the firewall" \
    --owner it@example.com --systems "$system" --audit "$(audit_ref)-$system" --requirements ISM-1701
}

third_parties() { stage third-parties 5 "Third parties: Sophos and the managed service provider" yes replay_third_parties "Third parties: SUP-SOPHOS and SUP-EXAMPLE-MSP, their solutions, contracts and reviews."; }
system_stage() { stage system 6 "The system: the Sophos perimeter firewall ($system)" no replay_system ""; }
audit_stage() { stage audit 7 "The system's audit: one decision per requirement" yes replay_audit "Assets: $system. Compliance > Audits: $(audit_ref)-<period>-$system."; }
controls_stage() { stage controls 8 "Controls the audit relies on" no replay_controls "Operations > Applied controls, linked to $(audit_ref)-<period>-$system."; }

sophos() {
  step "9. Evidence collectors: your Sophos Central tenant"
  mkdir -p "$ws/profiles"
  cp -R "$root/profiles/sophos-central.yaml" "$root/profiles/sophos-central.tests" "$ws/profiles/"
  case "$(trial_collector_step "$yes" "$ws/repo/hound.yaml" sophos)" in
    unattended) say "Unattended run: stopping before Sophos, which needs your credential."; return ;;
    "added already") say "A collector named sophos is already in repo/hound.yaml." ;;
    add)
      say "You need a Sophos Central API credential with the Service Principal Read-Only role (docs/collectors/sophos.md)."
      say "Sophos Central is read by the rest collector with the sophos-central profile, copied into ${ws#"$root"/}/profiles with its tests."
      say "hound asks for the profile's client ID and secret, and for the firewall firmware versions still in support (such as 21.5, 22.0)."
      say "The secret is not shown as you type, and is saved under ${ws#"$root"/}/secrets (git-ignored, mode 600), never printed."
      hound yes add collector rest --name sophos --config hound.yaml --schedule "0 4 * * *" --secrets-dir /trial/secrets \
        --set profile=sophos-central --set profiles_path=/trial/profiles
      ;;
  esac
  run_collector sophos "ISM-1501, which the firewall firmware check feeds" "reads the firewalls"
  say "ISM-1501 also needs the Microsoft 365 collector's operating system check, so it stays not assessed until step 10 runs too."
  say "Sophos Central is read for network devices only: endpoint protection evidence comes from the endpoints' own antivirus."
}

# run_collector NAME WHAT READS checks the secrets, dry-runs the collector, then publishes it after asking
run_collector() {
  hound no config validate hound.yaml --check-secrets
  say "A dry run signs in, $3, stores the evidence locally and plans the updates, sending nothing."
  hound no run --config hound.yaml --only "$1" --dry-run
  if ask "Publish the $1 results to CISO Assistant?"; then
    hound no run --config hound.yaml --only "$1"
    hound no status --config hound.yaml
    mark_done "$1"
    say "Look at the $(audit_ref) audit in CISO Assistant for $2."
  fi
  say "Paste any output you want reviewed with the identifiers removed: no tenant IDs, host names, IP addresses or serial numbers."
}

m365() {
  step "10. Evidence collectors: a Microsoft 365 test tenant (optional)"
  local pem="$ws/inbox/hound-m365.pem" crt="$ws/inbox/hound-m365.crt"
  case "$(trial_collector_step "$yes" "$ws/repo/hound.yaml" m365)" in
    unattended) say "Unattended run: stopping before Microsoft 365, which needs your tenant."; return ;;
    "added already") say "An m365 collector is already in repo/hound.yaml." ;;
    add)
      if ! ask "Try Microsoft 365 now? Use a test tenant, never a work one (docs/tutorials/local-trial.md, section 10)."; then return 0; fi
      say "In the tenant's Entra admin center, register an app with the six read-only Graph application permissions in"
      say "docs/collectors/m365.md and grant admin consent. The collector signs in with a certificate."
      if [[ ! -f "$pem" ]] && command -v openssl >/dev/null && ask "Make a test certificate in ${ws#"$root"/}/inbox now?"; then
        mkdir -p "$ws/inbox"
        (umask 077 && openssl req -x509 -newkey rsa:3072 -sha256 -days 90 -nodes -keyout "$ws/inbox/hound-m365-private.pem" \
          -out "$crt" -subj "/CN=Evidence Hound trial" 2>/dev/null && cat "$crt" "$ws/inbox/hound-m365-private.pem" >"$pem")
        rm -f "$ws/inbox/hound-m365-private.pem"
      fi
      [[ -f "$pem" ]] || die "Put the certificate and its private key, as one PEM file, at ${pem#"$root"/}, then run make trial again."
      [[ -f "$crt" ]] && say "Upload ${crt#"$root"/} (the public certificate only) under the app's Certificates & secrets."
      ask "Uploaded the certificate and granted admin consent?" || return 0
      say "hound asks for the tenant ID, the app's client ID and the Windows builds still in support (such as 10.0.22631, 10.0.26100)."
      hound yes add collector m365 --config hound.yaml --schedule "30 4 * * *" --secrets-dir /trial/secrets \
        --secret-file client_certificate=/trial/inbox/hound-m365.pem
      rm -f "$pem"
      say "The certificate is now in ${ws#"$root"/}/secrets/m365_client_certificate (mode 600); the copy in inbox is removed."
      ;;
  esac
  run_collector m365 "the identity, device and Office requirements the Microsoft 365 checks map to" "reads users, Conditional Access, roles and Intune"
}

preflight
build_image
workspace
organisation
third_parties
system_stage
audit_stage
controls_stage
sophos
m365
step "Done"
say "Run make trial again to repeat any step, or make trial-clean to remove the workspace (CISO Assistant keeps running)."
