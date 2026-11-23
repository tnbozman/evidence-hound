#!/usr/bin/env bash
# Interactive local trial: from the dev container, with CISO Assistant already running, run Evidence Hound in a
# container next to it. It walks the onboarding tutorial's deployment: Example Pty Ltd self-hosts CISO Assistant and Evidence Hound
# on a private Oracle Cloud VM (GRC-PLATFORM), and the office reaches it over a site-to-site VPN through a Sophos XGS 88W
# (OFFICE-FIREWALL). Steps: a new organisation (hound init), a hand-over to your coding agent, which drafts the third parties, each
# system, its Essential Eight ML2 audit and controls for you to approve, then the collectors, each against a fictional tenant on
# this machine: the IaC pipeline (control tests and drift), Sophos Central and Microsoft 365. No account, key or cloud is needed.
# Guide: docs/tutorials/local-trial.md
#
# Usage: make trial (or scripts/trial.sh). Safe to stop and run again: finished steps are skipped.
# Environment:
#   HOUND_TRIAL_DIR        the trial workspace (default .dev/trial, git-ignored)
#   HOUND_TRIAL_IMAGE      the runner image to build and use (default evidence-hound-runner:trial)
#   HOUND_TRIAL_YES=1      answer yes to every question (for unattended checks): every collector reads its dev server
#   HOUND_TRIAL_SOPHOS_PORT  the Sophos Central dev server's port (default 5370)
#   HOUND_TRIAL_IAC_PORT     the IaC pipeline dev server's port (default 5380)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=scripts/lib/trial.sh
source "$root/scripts/lib/trial.sh"

ws="${HOUND_TRIAL_DIR:-$root/.dev/trial}"
image="${HOUND_TRIAL_IMAGE:-evidence-hound-runner:trial}"
yes="${HOUND_TRIAL_YES:-0}"
state="$ws/state"
sophos_port="${HOUND_TRIAL_SOPHOS_PORT:-5370}"
sophos_dev_pid=""
iac_port="${HOUND_TRIAL_IAC_PORT:-5380}"
iac_dev_pid=""

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
  # host_mode, when set, overrides how hound's container reaches CISO Assistant (the Microsoft 365 dev server step).
  mode="${host_mode:-$(cat "$state/ca-mode")}"
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
  trial_seed_inputs "$root" "$ws"
  token
  say "hound writes the organisation's compliance repo into repo/, evidence into evidence/, and secrets into secrets/."
  say "inbox/ holds fictional notes on each supplier and specs/ a description of the Sophos Central API, for your agent to read."
  step "The scene"
  trial_scene
}

# audit_ref -> the audit hound init wrote into the trial config
audit_ref() { sed -n 's/^ *compliance_assessment: *//p' "$ws/repo/hound.yaml" | head -n 1 | tr -d '"'; }

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
${ws#"$root"/}/bin/hound runs this image against this CISO Assistant. hound's container sees only ${ws#"$root"/}: put any
API specification in ${ws#"$root"/}/specs and the vendor documents in ${ws#"$root"/}/inbox.
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

# The stages an agent drafts. Each stage: what it is, the request for the agent, a wait until its draft is in the repo, the
# person's approval where one is needed, then validate, plan and apply. A system's stages run once for each of trial_systems.

# stage NAME NUMBER TITLE APPROVE(yes|no) REPLAY LOOK [SYSTEM]
stage() {
  local name="$1" number="$2" title="$3" approve="$4" replay="$5" look="$6" system="${7:-}" series key
  key="$name${system:+-$system}"
  step "$number. $title"
  series="$(audit_ref)"
  if done_already "$key"; then
    say "Done already. Remove ${ws#"$root"/}/state/$key.done to do it again."
    return 0
  fi
  if [[ "$yes" == 1 ]]; then
    say "Unattended: replaying the fictional answers an agent and a person would give (examples/trial/README.md)."
    "$replay" "$system"
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
    mark_done "$key"
    return 0
  fi
  apply_scope "$key"
  done_already "$key" && say "Look in CISO Assistant (https://localhost:8443): $look"
}

# draft_supplier SYSTEM REF NAME SOLUTION-REF SOLUTION-NAME CONTRACT-REF CONTRACT-NAME LEVEL... drafts one third party as an agent would
draft_supplier() {
  local system="$1" ref="$2" name="$3" sol="$4" sol_name="$5" ctr="$6" ctr_name="$7" levels=() level
  shift 7
  for level in "$@"; do levels+=(--level "$level"); done
  hound no supplier draft --system "$system" --ref "$ref" --name "$name" --solution-ref "$sol" --solution-name "$sol_name" \
    --contract-ref "$ctr" --contract-name "$ctr_name" "${levels[@]}" --kind vendor
}

replay_third_parties() {
  draft_supplier GRC-PLATFORM SUP-ORACLE "Oracle Cloud Infrastructure" SOL-OCI-HOSTING "OCI compute, networking and storage" \
    CTR-OCI-2026 "OCI pay-as-you-go 2026" "dependency=3:The GRC platform runs only on it; moving it is a planned project." \
    "penetration=2:It hosts the VM and its disks, but holds no account in our identity provider."
  draft_supplier OFFICE-FIREWALL SUP-SOPHOS Sophos SOL-SOPHOS-FIREWALL "Sophos XGS 88W firewall and Sophos Central" \
    CTR-SOPHOS-2026 "Sophos XGS subscription 2026" \
    "dependency=3:The office firewall carries all office traffic and the site-to-site VPN to the GRC platform." \
    "penetration=2:Sophos Central manages the firewall remotely."
  draft_supplier GRC-PLATFORM SUP-CISO-ASSISTANT "CISO Assistant (open-source project)" SOL-CISO-ASSISTANT "CISO Assistant community edition" \
    CTR-CISO-ASSISTANT-LICENCE "AGPL-3.0 licence" "dependency=3:It is our system of record for compliance." \
    "penetration=1:It runs in our VM; the project has no access to it."
  draft_supplier GRC-PLATFORM SUP-EVIDENCE-HOUND "Evidence Hound (open-source project)" SOL-EVIDENCE-HOUND "Evidence Hound runner" \
    CTR-EVIDENCE-HOUND-LICENCE "Apache-2.0 licence" "dependency=2:It collects our evidence; CISO Assistant still works without it." \
    "penetration=1:It runs in our VM with read-only credentials; the project has no access to it."
  say "Standing in for hound approve: the approved copies in examples/trial/third-parties."
  cp "$root/examples/trial/third-parties/"*.yaml "$ws/repo/scope/third-parties/"
}

replay_system() {
  # The maturity level's groups, as hound init wrote them on the organisation's audit: groups: ["1", "2"]
  local system="$1" groups=() group
  for group in $(sed -n 's/^groups: \[\(.*\)\]/\1/p' "$ws/repo/scope/audits/"*-e8.yaml | tr -d '",'); do groups+=(--group "$group"); done
  hound no onboard "$system" pending --json --repo . "${groups[@]}" --owner it@example.com \
    --product "$(trial_system_product "$system")" --solution "$(trial_system_solution "$system")" >/dev/null
}

replay_audit() {
  # Typed into hound onboard as a person would: the answers reach the container on its standard input.
  local slug
  slug="$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')"
  hound no onboard "$1" --repo . <"$root/examples/onboarding-tutorial/answers/$slug.decisions.txt" >/dev/null
}

replay_controls() {
  local audit
  audit="$(audit_ref)-$1"
  case "$1" in
    GRC-PLATFORM)
      hound no add control --scope scope --yes --ref-id AC-GRC-PATCHING --name "Unattended security updates on the GRC platform's VM" \
        --owner it@example.com --systems GRC-PLATFORM --audit "$audit" --requirements ISM-1695
      hound no add control --scope scope --yes --ref-id AC-GRC-RESTORE-TEST --name "Quarterly restore test of the GRC platform to a scratch VM" \
        --owner it@example.com --systems GRC-PLATFORM --audit "$audit" --requirements ISM-1515
      ;;
    OFFICE-FIREWALL)
      hound no add control --scope scope --yes --ref-id AC-EXTERNAL-SCAN --name "Daily external vulnerability scan of the office firewall" \
        --owner it@example.com --systems OFFICE-FIREWALL --audit "$audit" --requirements ISM-1701
      ;;
  esac
}

third_parties() {
  stage third-parties 5 "Third parties: Oracle, Sophos, and the CISO Assistant and Evidence Hound projects" yes replay_third_parties \
    "Third parties: SUP-ORACLE, SUP-SOPHOS, SUP-CISO-ASSISTANT and SUP-EVIDENCE-HOUND, their solutions, contracts and reviews."
}

# onboard_system SYSTEM FIRST-NUMBER runs the system's three stages, numbered from FIRST-NUMBER
onboard_system() {
  local system="$1" n="$2"
  stage system "$n" "$system: start onboarding it ($(trial_system_product "$system"))" no replay_system "" "$system"
  stage audit "$((n + 1))" "$system: its Essential Eight ML2 audit, one decision per requirement" yes replay_audit \
    "Assets: $system. Compliance > Audits: $(audit_ref)-<period>-$system." "$system"
  stage controls "$((n + 2))" "$system: the controls its audit relies on" no replay_controls \
    "Operations > Applied controls, linked to $(audit_ref)-<period>-$system." "$system"
}

# wire_audit SYSTEM adds the system's mapping (its API checks, as the onboarding tutorial has it) and a target for its audit series,
# so collectors publish to the system's audit as well as the organisation's. No hound command writes these yet (s09, Discovered work).
wire_audit() {
  local slug mapping
  slug="$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')"
  mapping="mappings/$slug-essential-eight-2023.yaml"
  if [[ ! -f "$ws/repo/$mapping" ]]; then
    cp "$root/examples/onboarding-tutorial/$mapping" "$ws/repo/$mapping"
    say "Copied the onboarding tutorial's $mapping: which of $1's requirements each of its checks answers."
  fi
  trial_add_target "$ws/repo/hound.yaml" "$mapping" "$(audit_ref)-$1"
  say "repo/hound.yaml targets $(audit_ref)-$1 too, so the collector publishes to $1's own audit."
}

# sophos_dev [DEVIATION] (re)starts the Sophos Central dev server in the background, compliant or with one deviation
sophos_dev() {
  local url args=()
  url="$(trial_dev_url "$(cat "$state/ca-mode")" "$sophos_port")"
  stop_sophos_dev
  args=(--port "$sophos_port" --public-url "$url")
  [[ -n "${1:-}" ]] && args+=(--deviation "$1")
  show "make sophos-dev ARGS=\"${args[*]}\""
  dotnet run --project "$root/src/EvidenceHound.SophosDevServer" -- "${args[@]}" >"$ws/sophos-dev.log" 2>&1 &
  sophos_dev_pid=$!
  local tries=0
  until answers_ok "http://localhost:$sophos_port/whoami/v1"; do
    kill -0 "$sophos_dev_pid" 2>/dev/null || die "The Sophos Central dev server stopped; see ${ws#"$root"/}/sophos-dev.log."
    ((tries++ < 180)) || die "The Sophos Central dev server did not answer within three minutes; see ${ws#"$root"/}/sophos-dev.log."
    sleep 1
  done
  say "The Sophos Central dev server answers at $url (${1:-every control in place})."
}

stop_sophos_dev() {
  [[ -n "$sophos_dev_pid" ]] || return 0
  kill "$sophos_dev_pid" 2>/dev/null || true
  wait "$sophos_dev_pid" 2>/dev/null || true
  sophos_dev_pid=""
}
trap 'stop_sophos_dev; stop_iac_dev' EXIT

# iac_dev [DEVIATION] (re)starts the IaC pipeline dev server in the background: the GRC platform's control tests (the report
# terraform test wrote, when there is one) and its drift plan, as the IaC repository's GitHub Actions would serve them
iac_dev() {
  local url args=() report
  url="$(trial_dev_url "$(cat "$state/ca-mode")" "$iac_port")"
  stop_iac_dev
  args=(--port "$iac_port" --public-url "$url")
  report="$(trial_controls_report_args "$ws/iac")"
  [[ -n "$report" ]] && args+=(--controls-report "${report#--controls-report }")
  [[ -n "${1:-}" ]] && args+=(--deviation "$1")
  show "make iac-dev ARGS=\"${args[*]}\""
  dotnet run --project "$root/src/EvidenceHound.IacDevServer" -- "${args[@]}" >"$ws/iac-dev.log" 2>&1 &
  iac_dev_pid=$!
  local tries=0
  until answers_ok "http://localhost:$iac_port/repos/example/grc-infra/actions/workflows/drift.yml/runs"; do
    kill -0 "$iac_dev_pid" 2>/dev/null || die "The IaC pipeline dev server stopped; see ${ws#"$root"/}/iac-dev.log."
    ((tries++ < 180)) || die "The IaC pipeline dev server did not answer within three minutes; see ${ws#"$root"/}/iac-dev.log."
    sleep 1
  done
  say "The IaC pipeline dev server answers at $url (${1:-every control in place, no drift})."
}

stop_iac_dev() {
  [[ -n "$iac_dev_pid" ]] || return 0
  kill "$iac_dev_pid" 2>/dev/null || true
  wait "$iac_dev_pid" 2>/dev/null || true
  iac_dev_pid=""
}

# control_tests runs the onboarding tutorial's Terraform control tests (mock providers: no Oracle account, nothing created) into
# iac/controls.xml, when Terraform is installed; the dev server then serves that real report
control_tests() {
  command -v terraform >/dev/null || { say "Terraform is not installed, so the dev server serves its own report of the same tests."; return 0; }
  [[ -s "$ws/iac/controls.xml" ]] && { say "Using the control-test report in ${ws#"$root"/}/iac/controls.xml (remove it to test again)."; return 0; }
  mkdir -p "$ws/iac"
  rm -rf "$ws/iac/oci"
  cp -R "$root/examples/onboarding-tutorial/infra/oci" "$ws/iac/oci"
  say "The IaC pipeline runs the hosting controls as Terraform tests on every change. Running them here, as it would:"
  show "terraform init -backend=false && terraform test -junit-xml=controls.xml   (in ${ws#"$root"/}/iac/oci)"
  if (cd "$ws/iac/oci" && terraform init -backend=false -input=false >"$ws/iac/terraform.log" 2>&1 &&
    terraform test -junit-xml="$ws/iac/controls.xml" >>"$ws/iac/terraform.log" 2>&1); then
    say "terraform test passed; the dev server serves its report as the pipeline's control-tests artefact."
  else
    say "terraform test did not finish cleanly (see ${ws#"$root"/}/iac/terraform.log)."
    [[ -s "$ws/iac/controls.xml" ]] || say "No report was written, so the dev server serves its own report of the same tests."
  fi
}

grc_platform_evidence() {
  step "12. GRC-PLATFORM's evidence: its hosting controls and drift, from the IaC pipeline"
  mkdir -p "$ws/profiles"
  cp -R "$root/examples/onboarding-tutorial/profiles/grc-infra-controls.yaml" "$root/examples/onboarding-tutorial/profiles/grc-infra-controls.tests" \
    "$root/profiles/terraform-drift.yaml" "$root/profiles/terraform-drift.tests" "$ws/profiles/"
  say "GRC-PLATFORM's network, backups and hosting controls are Terraform in the IaC repository (examples/onboarding-tutorial/infra/oci)."
  say "Its pipeline proves each control with terraform test and checks nightly that the cloud still matches the code. hound only"
  say "reads the pipeline's reports; it never runs Terraform against the cloud and holds no cloud credential."
  control_tests
  iac_dev
  wire_audit GRC-PLATFORM
  local name profile schedule sets
  for name in grc-infra-controls grc-infra-drift; do
    profile=grc-infra-controls schedule="30 23 * * *"
    [[ "$name" == grc-infra-drift ]] && profile=terraform-drift schedule="0 23 * * *"
    if ! grep -q "^  - name: $name\$" "$ws/repo/hound.yaml"; then
      mkdir -p "$ws/inbox"
      printf '%s\n' iac-dev-server-checks-no-token >"$ws/inbox/iac-dev-token"
      mapfile -t sets < <(trial_iac_dev_sets "$(trial_dev_url "$(cat "$state/ca-mode")" "$iac_port")" "$profile")
      hound no add collector rest --name "$name" --config hound.yaml --schedule "$schedule" --secrets-dir /trial/secrets \
        --secret-file token=/trial/inbox/iac-dev-token "${sets[@]}" --yes
      rm -f "$ws/inbox/iac-dev-token"
    fi
  done
  run_collector grc-infra-controls "GRC-PLATFORM's backup rows (ISM-1811, ISM-1705, ISM-1812, ISM-1707, ISM-1814), with the drift check" \
    "reads the newest control-tests report"
  run_collector grc-infra-drift "the same rows: each needs its control test and no drift" "reads the newest drift plan"
  say "Evidence is collected on a schedule, so a change shows up at the next run. To see one:"
  if ask "Simulate someone changing the backup bucket in the OCI console (drift) and collect again?"; then
    iac_dev drift
    hound no run --config hound.yaml --only grc-infra-drift
    hound no status --config hound.yaml
    say "Look at the $(audit_ref)-GRC-PLATFORM audit again: the backup rows now fail, naming the drifted bucket."
    say "Run make trial again to collect from the clean pipeline once more."
  fi
  stop_iac_dev
  say "GRC-PLATFORM's other rows are manual: patching (AC-GRC-PATCHING) and restore tests (AC-GRC-RESTORE-TEST), submitted by it@example.com."
}

sophos() {
  step "13. OFFICE-FIREWALL's evidence: Sophos Central, a fictional tenant on this machine or your own"
  mkdir -p "$ws/profiles"
  cp -R "$root/profiles/sophos-central.yaml" "$root/profiles/sophos-central.tests" "$ws/profiles/"
  local dev=yes
  case "$(trial_collector_step 0 "$ws/repo/hound.yaml" sophos)" in
    "added already")
      say "A collector named sophos is already in repo/hound.yaml."
      trial_uses_dev_server "$ws/repo/hound.yaml" "$sophos_port" || dev=no
      ;;
    add)
      say "The Sophos Central dev server is a fictional Example Pty Ltd tenant (docs/tutorials/sophos-dev-server.md): two firewalls"
      say "and five admins, served on this machine. It needs no account and no credential, and nothing leaves the machine."
      if [[ "$yes" != 1 ]] && ! ask "Use the dev server? Answer n to use your own Sophos Central tenant instead."; then dev=no; fi
      if [[ "$dev" == yes ]]; then
        sophos_dev
        say "Sophos Central is read by the rest collector with the sophos-central profile, copied into ${ws#"$root"/}/profiles with its tests."
        say "The dev server checks no secret, so the collector gets a placeholder one; no question asks for a credential."
        mkdir -p "$ws/inbox"
        printf '%s\n' sophos-dev-server-checks-no-secret >"$ws/inbox/sophos-dev-secret"
        local sets
        mapfile -t sets < <(trial_sophos_dev_sets "$(trial_dev_url "$(cat "$state/ca-mode")" "$sophos_port")")
        hound no add collector rest --name sophos --config hound.yaml --schedule "0 4 * * *" --secrets-dir /trial/secrets \
          --secret-file client_secret=/trial/inbox/sophos-dev-secret "${sets[@]}" --yes
        rm -f "$ws/inbox/sophos-dev-secret"
      else
        say "You need a Sophos Central API credential with the Service Principal Read-Only role (docs/collectors/sophos.md)."
        say "hound asks for the profile's client ID and secret, and for the firewall firmware versions still in support (such as 21.5, 22.0)."
        say "The secret is not shown as you type, and is saved under ${ws#"$root"/}/secrets (git-ignored, mode 600), never printed."
        hound yes add collector rest --name sophos --config hound.yaml --schedule "0 4 * * *" --secrets-dir /trial/secrets \
          --set profile=sophos-central --set profiles_path=/trial/profiles
      fi
      ;;
  esac
  [[ "$dev" == yes && -z "$sophos_dev_pid" ]] && sophos_dev
  wire_audit OFFICE-FIREWALL
  run_collector sophos "ISM-1501, which the firewall firmware check feeds" "reads the firewalls"
  if [[ "$dev" == yes ]]; then
    say "Evidence is collected on a schedule, so a change in Sophos Central shows up at the next run. To see one:"
    if ask "Take the branch firewall out of firmware support in the dev server and collect again?"; then
      sophos_dev unsupported-firmware
      hound no run --config hound.yaml --only sophos
      hound no status --config hound.yaml
      say "Look at the $(audit_ref) audit in CISO Assistant again: the firmware check now fails, naming the branch firewall."
      say "Run make trial again to collect from the compliant tenant once more."
    fi
    stop_sophos_dev
  fi
  say "In the organisation's audit, ISM-1501 also needs the Microsoft 365 collector's operating system check: step 14."
  say "Sophos Central is read for network devices only: endpoint protection evidence comes from the endpoints' own antivirus."
}

# run_collector NAME WHAT READS checks the secrets, dry-runs the collector, then publishes it after asking
run_collector() {
  hound no config validate hound.yaml --check-secrets
  say "A dry run $3, stores the evidence locally and plans the updates, sending nothing."
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
  step "14. Identity and laptops: Microsoft 365"
  local pem="$ws/inbox/hound-m365.pem" crt="$ws/inbox/hound-m365.crt"
  if grep -q 'sign_in: none' "$ws/repo/hound.yaml" 2>/dev/null; then
    say "The m365 collector in repo/hound.yaml reads the Microsoft 365 dev server."
    m365_dev
    return
  fi
  case "$(trial_collector_step 0 "$ws/repo/hound.yaml" m365)" in
    "added already") say "An m365 collector is already in repo/hound.yaml." ;;
    add)
      say "The Microsoft 365 dev server is a fictional Example Pty Ltd tenant on this machine: no tenant, no sign-in, nothing leaves it."
      if [[ "$yes" == 1 ]] || ask "Use the dev server? Answer n to use your own test tenant instead."; then
        m365_dev
        return
      fi
      if ! ask "Try your own test tenant now? Never a work one (docs/tutorials/local-trial.md, section 14)."; then return 0; fi
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
  run_collector m365 "the identity, device and Office requirements the Microsoft 365 checks map to" "signs in and reads users, Conditional Access, roles and Intune"
}

# m365_dev starts the Microsoft 365 dev server, adds the m365 collector for its tenant if it is not there yet, runs it, and stops the server
m365_dev() {
  local variant=compliant log="$ws/m365-dev.log" server="" tries
  if [[ "$yes" != 1 ]] && ! grep -q 'sign_in: none' "$ws/repo/hound.yaml"; then
    ask "The compliant tenant, which passes every check? Answer n for the gaps tenant, where each named check fails." || variant=gaps
  fi
  if answers_ok http://localhost:5365/v1.0/users; then
    [[ -s "$log" ]] || die "Something already answers on port 5365. Stop it (or your make m365-dev) and run make trial again."
    say "The dev server already answers on http://localhost:5365/."
  else
    show "make m365-dev ARGS=\"--variant $variant\""
    setsid dotnet run --project "$root/src/EvidenceHound.M365DevServer" -- --variant "$variant" >"$log" 2>&1 &
    server=$!
    # Stopped on any exit, so a failed step does not leave it running. Expanded now: server is local to this function.
    # shellcheck disable=SC2064
    trap "kill -- -$server 2>/dev/null || true" EXIT
    for ((tries = 0; tries < 90; tries++)); do answers_ok http://localhost:5365/v1.0/users && break; sleep 2; done
    answers_ok http://localhost:5365/v1.0/users || { kill -- "-$server" 2>/dev/null; die "The dev server did not start; see ${log#"$root"/}."; }
    say "The dev server answers on http://localhost:5365/ ($variant tenant)."
  fi
  if ! grep -q '^  - name: m365$' "$ws/repo/hound.yaml"; then
    local args=()
    mapfile -t args < <(trial_m365_dev_args "$log")
    ((${#args[@]} > 0)) || die "The dev server printed no collector settings; see ${log#"$root"/}."
    say "Adding the m365 collector with the tenant's settings, graph_url on localhost and sign_in: none (no certificate or secret)."
    hound no add collector m365 --config hound.yaml --schedule "30 4 * * *" --yes "${args[@]}"
  fi
  host_mode="$(trial_host_mode "$(cat "$state/ca-mode")")" \
    run_collector m365 "the identity, device and Office requirements the Microsoft 365 checks map to" "reads the fictional tenant's users, Conditional Access, roles and Intune"
  if [[ -n "$server" ]]; then
    kill -- "-$server" 2>/dev/null || true
    trap - EXIT
    say "Stopped the dev server. make m365-dev starts it again (ARGS=\"--variant gaps\" for the gaps tenant)."
  fi
}

# scans writes the fictional scan exports, adds a vuln-scan source per scope if it is not there yet, runs and publishes them, then
# offers to write the gaps estate and collect again
scans() {
  step "15. Patching evidence: vulnerability scans, fictional exports on this machine"
  local log="$ws/scan-dev.log" name args=()
  say "Scanners export files rather than answer an API, so the vuln-scan collector reads exports dropped in a folder."
  say "make scan-dev writes a fictional Example Pty Ltd estate's exports, dated from today, into ${ws#"$root"/}/scans:"
  say "Nessus for internet-facing services, other applications and operating systems, and a Defender CSV for the laptops."
  scan_dev compliant
  for name in $(trial_scan_source_names "$log"); do
    if ! grep -q "^  - name: $name\$" "$ws/repo/hound.yaml"; then
      mapfile -t args < <(trial_scan_source_args "$log" "$name")
      hound no add collector vuln-scan --config hound.yaml --schedule "0 7 * * *" --yes "${args[@]}"
    fi
  done
  for name in $(trial_scan_source_names "$log"); do
    run_collector "$name" "the patching requirements its scope maps to (docs/collectors/vuln-scan.md)" "reads the newest export in its folder"
  done
  say "A real deployment drops each new export into the same folder, by hand or with hound manual upload --source <name>."
  if ask "Write the gaps estate (a stale scan, overdue patches, unsupported software) and collect again?"; then
    scan_dev gaps
    for name in $(trial_scan_source_names "$log"); do
      hound no run --config hound.yaml --only "$name"
    done
    hound no status --config hound.yaml
    say "Look at the $(audit_ref) audit in CISO Assistant again: the patching rows now fail, naming the hosts and findings."
    say "Run make trial again to write the compliant estate once more."
  fi
}

# scan_dev VARIANT writes that estate's exports into the workspace's scans folder, as make scan-dev does
scan_dev() {
  show "make scan-dev ARGS=\"--variant $1 --out ../scans\""
  (cd "$ws/repo" && dotnet run --project "$root/src/EvidenceHound.ScanDev" -- --variant "$1" --out ../scans) >"$ws/scan-dev.log" 2>&1 ||
    die "make scan-dev failed; see ${ws#"$root"/}/scan-dev.log."
  trial_scan_source_names "$ws/scan-dev.log" >/dev/null || die "make scan-dev printed no scan sources; see ${ws#"$root"/}/scan-dev.log."
}

preflight
build_image
workspace
organisation
third_parties
n=6
for system in $(trial_systems); do
  onboard_system "$system" "$n"
  n=$((n + 3))
done
grc_platform_evidence
sophos
m365
scans
step "Done"
say "Run make trial again to repeat any step, or make trial-clean to remove the workspace (CISO Assistant keeps running)."
