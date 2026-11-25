#!/usr/bin/env bash
# Pure helpers for the interactive local trial (scripts/trial.sh). Source this file; every function
# reads its arguments and prints to stdout, with no Docker, network or file side effects.

# trial_choose_ca BUNDLED NETWORK LOCAL PORT8000 PORT8443 -> how to reach CISO Assistant
#   BUNDLED  yes when the dev container's own CISO Assistant answers at http://backend:8000
#   NETWORK  the Docker network of a running CISO Assistant backend on the visible daemon, or empty
#   LOCAL    yes when a CISO Assistant answers at http://localhost:8000
#   PORTnnnn free or busy
# Prints: bundled | network <name> | local | start | blocked <ports...>
# A running CISO Assistant is always reused; one is only offered to be started when both ports are free.
trial_choose_ca() {
  local bundled="$1" network="$2" local_api="$3" api_port="$4" ui_port="$5" busy=()
  if [[ "$bundled" == yes ]]; then echo bundled; return; fi
  if [[ -n "$network" ]]; then echo "network $network"; return; fi
  if [[ "$local_api" == yes ]]; then echo local; return; fi
  [[ "$api_port" == busy ]] && busy+=(8000)
  [[ "$ui_port" == busy ]] && busy+=(8443)
  if ((${#busy[@]} > 0)); then echo "blocked ${busy[*]}"; else echo start; fi
}

# trial_port_holder PORT CONTAINERS -> one sentence naming what holds a bound port
trial_port_holder() {
  local port="$1" containers="$2"
  if [[ -n "$containers" ]]; then
    echo "Port $port is held by the container $containers."
  else
    echo "Port $port is held by a process outside Docker (check with: ss -ltnp 'sport = :$port')."
  fi
}

# trial_network_args MODE -> the docker run network option for hound's container
# The dev container's CISO Assistant runs on the outer Docker daemon, which a docker-in-docker dev container cannot
# attach to, so hound shares the dev container's network namespace instead, where `backend` resolves.
trial_network_args() {
  case "$1" in
    "network "*) echo "--network ${1#network } --add-host host.docker.internal:host-gateway" ;;
    *) echo "--network host" ;;
  esac
}

# trial_dev_url PORT -> a dev server's address, listening in the dev container on PORT, as hound's container sees it
# Always localhost: hound sends credentials over plain http only to this machine, so hound reading a dev server shares the
# host network (trial_run_mode).
trial_dev_url() {
  echo "http://localhost:$1/"
}

# trial_run_mode MODE READS_DEV(yes|no) -> the CISO Assistant mode for one hound container: the host network's when the config
# reads a dev server, as only there is the dev server on localhost
trial_run_mode() {
  if [[ "$2" == yes ]]; then trial_host_mode "$1"; else echo "$1"; fi
}

# trial_sophos_dev_sets URL -> the hound add collector rest settings, one word per line, for the Sophos Central dev server at URL
# The client ID is the dev tenant's (SophosDevTenant.ClientId); the server checks no credential, so nothing asks for one.
trial_sophos_dev_sets() {
  printf '%s\n' --set profile=sophos-central --set profiles_path=/trial/profiles \
    --set client_id=00000000-0000-4000-8000-0000050d0001 --set "id_url=$1" --set "api_url=$1" \
    --set "supported_firewall_firmware=21.0, 21.5"
}

# trial_uses_dev_server CONFIG PORT -> success when the config's collectors read a dev server on PORT
trial_uses_dev_server() {
  grep -qE "(localhost|host\.docker\.internal):$2/" "$1"
}

# trial_ca_url MODE -> the CISO Assistant API address as hound's container sees it
trial_ca_url() {
  case "$1" in
    local) echo "http://localhost:8000/api/" ;;
    *) echo "http://backend:8000/api/" ;;
  esac
}

# trial_hound_argv IMAGE NETWORK_ARGS WORKSPACE UID:GID CA_URL TTY(yes|no) HOUND_ARGS... -> one word per line
# A throwaway container with the trial workspace at /trial. The token is read from a file inside the workspace,
# so no secret is ever on a command line.
trial_hound_argv() {
  trial_hound_argv_in /trial/repo "$@"
}

# trial_hound_argv_in WORKDIR IMAGE NETWORK_ARGS WORKSPACE UID:GID CA_URL TTY(yes|no) HOUND_ARGS... -> one word per line
# The same container, working in WORKDIR (a path under /trial) instead of the repo's root.
trial_hound_argv_in() {
  local workdir="$1" image="$2" network="$3" workspace="$4" user="$5" url="$6" tty="$7"
  shift 7
  local words=(docker run --rm -i)
  [[ "$tty" == yes ]] && words+=(-t)
  # shellcheck disable=SC2206  # network is "--network <name>", two words on purpose
  words+=(--user "$user" $network -v "$workspace:/trial" -w "$workdir"
    -e "HOUND_CA_URL=$url" -e HOUND_CA_TOKEN_FILE=/trial/secrets/ca_token "$image" "$@")
  printf '%s\n' "${words[@]}"
}

# trial_workdir WORKSPACE DIR -> DIR as hound's container sees it (/trial/...); fails when DIR is outside WORKSPACE
trial_workdir() {
  local workspace="${1%/}" dir="${2%/}"
  if [[ "$dir" == "$workspace" ]]; then
    echo /trial
  elif [[ "$dir" == "$workspace"/* ]]; then
    echo "/trial/${dir#"$workspace"/}"
  else
    return 1
  fi
}

# trial_scene -> the deployment the trial onboards: the onboarding tutorial's (docs/examples/onboarding-tutorial.md)
trial_scene() {
  cat <<'TEXT'
Example Pty Ltd, a small defence supplier, runs its cyber GRC on CISO Assistant and Evidence Hound, self-hosted and private:
  - GRC-PLATFORM: CISO Assistant and Evidence Hound in Docker Compose on one private Oracle Cloud (OCI) VM, with no public IP
    address. Its network, backups and hosting controls are Terraform, tested on every change and checked nightly for drift.
  - OFFICE-FIREWALL: the office's Sophos XGS 88W firewall, managed in Sophos Central. Staff reach the GRC platform only over
    its site-to-site VPN to OCI.
  - Microsoft 365: staff identities, sign-in with MFA, and the laptops in Intune.
The GRC team audits each system to the Essential Eight, maturity level 2, in CISO Assistant: third parties first, then each
system and its audit, the controls it relies on, and evidence that collectors gather on a schedule. Nothing here is real, and
every collector reads a fictional tenant on this machine: no account, API key or cloud is needed.
Diagram: docs/examples/onboarding-tutorial.md, The deployment.
TEXT
}

# trial_systems -> the systems the trial onboards, in order
trial_systems() { echo "GRC-PLATFORM OFFICE-FIREWALL"; }

# trial_system_product SYSTEM -> what the system is, as hound onboard records it
trial_system_product() {
  case "$1" in
    GRC-PLATFORM) echo "CISO Assistant and Evidence Hound on an OCI Compute VM" ;;
    OFFICE-FIREWALL) echo "Sophos Firewall on an XGS 88W appliance, managed in Sophos Central" ;;
    *) return 1 ;;
  esac
}

# trial_system_solution SYSTEM -> the supplier's solution the system is
trial_system_solution() {
  case "$1" in
    GRC-PLATFORM) echo SOL-OCI-HOSTING ;;
    OFFICE-FIREWALL) echo SOL-SOPHOS-FIREWALL ;;
    *) return 1 ;;
  esac
}

# trial_brief STAGE SERIES [SYSTEM] -> what to ask the coding agent for at one stage, for a person to paste
trial_brief() {
  local series="$2" system="${3:-}"
  case "$1" in
    third-parties) cat <<TEXT
Draft the third parties our two systems depend on, with the skills onboard-a-third-party and assess-a-supplier and
hound supplier draft: Oracle (Oracle Cloud Infrastructure, solution SOL-OCI-HOSTING) for GRC-PLATFORM; Sophos (the XGS 88W and
Sophos Central, solution SOL-SOPHOS-FIREWALL) for OFFICE-FIREWALL; and the open-source CISO Assistant and Evidence Hound projects
for GRC-PLATFORM. Read the vendor documents in ../inbox. Give each level its reason.
I approve them and conclude their reviews myself, with hound approve.
TEXT
      ;;
    system) cat <<TEXT
Start onboarding $system ($(trial_system_product "$system")) with the skill onboard-a-system-with-hound:
hound onboard $system pending --json --repo . with its owner, product, solution $(trial_system_solution "$system") and the
maturity level groups, and any API specification in ../specs. Ask me for what you cannot read from the documents.
TEXT
      ;;
    audit) cat <<TEXT
Decide every requirement for $system with hound onboard $system decide, using read-an-api-specification for the
API checks, until pending lists nothing. Then run the review (review-before-apply) and tell me what it flagged.
Its audit will be $series-$system. I approve the decisions myself with hound approve.
TEXT
      ;;
    controls) cat <<TEXT
Declare the applied controls the $system audit relies on for its manual requirements, each with
hound add control --scope scope --yes --ref-id AC-... --name ... --owner ... --systems $system --audit $series-$system --requirements ...
Then run hound scope validate scope --config hound.yaml --json and fix what it reports.
TEXT
      ;;
    *) return 1 ;;
  esac
}

# trial_stage_ready STAGE REPO [SYSTEM] -> 0 once the agent's draft for the stage is in the repo
trial_stage_ready() {
  local repo="$2" system="${3:-}" slug
  slug="$(printf '%s' "$system" | tr '[:upper:]' '[:lower:]')"
  case "$1" in
    third-parties) (($(find "$repo/scope/third-parties" -maxdepth 1 -name '*.yaml' 2>/dev/null | wc -l) >= 4)) ;;
    system) [[ -s "$repo/onboarding/$system/session.json" ]] ;;
    audit) compgen -G "$repo/scope/audits/$slug-*.yaml" >/dev/null ;;
    controls) grep -Eq "^    systems: \[(.*, )?$system(, .*)?\]" "$repo/scope/controls.yaml" 2>/dev/null ;;
    *) return 1 ;;
  esac
}

# trial_add_target CONFIG MAPPING AUDIT adds a target to the deployment config, so collectors publish to a system's audit too:
# a single `target:` becomes a `targets:` list first; a target already there for AUDIT is left as it is
trial_add_target() {
  local config="$1" mapping="$2" audit="$3"
  grep -q "^    compliance_assessment: $audit\$" "$config" && return 0
  awk -v mapping="$mapping" -v audit="$audit" '
    function add() { print "  - mapping: " mapping; print "    compliance_assessment: " audit; added = 1 }
    /^target:$/ { print "targets:"; inside = 1; first = 1; next }
    /^targets:$/ { print; inside = 1; next }
    inside && /^  / { if (first) { sub(/^  /, "  - "); first = 0 } else if ($0 !~ /^  - / && $0 !~ /^    /) { sub(/^  /, "    ") } print; next }
    inside { add(); inside = 0 }
    { print }
    END { if (inside) add() }
  ' "$config" >"$config.new" && mv "$config.new" "$config"
}

# trial_iac_dev_sets URL PROFILE -> hound add collector options for a rest collector reading the IaC pipeline dev server, one word per line
trial_iac_dev_sets() {
  local workflow=controls.yml
  [[ "$2" == terraform-drift ]] && workflow=drift.yml
  printf '%s\n' --set "profile=$2" --set profiles_path=/trial/profiles --set "api_url=$1" --set repository=example/grc-infra --set "workflow=$workflow"
}

# trial_controls_report_args DIR -> the IaC dev server's option for the report terraform test wrote to DIR/controls.xml, if it did
trial_controls_report_args() {
  [[ -s "$1/controls.xml" || -f "$1/controls.xml" ]] && echo "--controls-report $1/controls.xml"
  return 0
}

# trial_collector_step UNATTENDED(1|0) CONFIG NAME -> unattended | added already | add
# A collector step needs the person's credential, so an unattended run stops before it; one already in the config, by name, is kept.
trial_collector_step() {
  local unattended="$1" config="$2" name="$3"
  if [[ "$unattended" == 1 ]]; then
    echo "unattended"
  elif [[ -f "$config" ]] && grep -q "^  - name: $name\$" "$config"; then
    echo "added already"
  else
    echo "add"
  fi
}

# trial_m365_dev_args LOG -> hound add collector options, one word per line, from the settings the Microsoft 365 dev server printed
# to LOG (make m365-dev), with sign_in=none: the dev server answers Graph but not sign-in (docs/tutorials/m365-dev-server.md).
trial_m365_dev_args() {
  [[ -s "$1" ]] || return 1
  local key value found=0
  while IFS= read -r line; do
    [[ "$line" =~ ^\ \ ([a-z_]+):\ (.*)$ ]] || continue
    key="${BASH_REMATCH[1]}"
    value="${BASH_REMATCH[2]%\"}"
    value="${value#\"}"
    printf -- '--set\n%s=%s\n' "$key" "$value"
    found=1
  done <"$1"
  ((found)) || return 1
  printf -- '--set\nsign_in=none\n'
}

# trial_host_mode MODE -> the CISO Assistant mode for a hound container on the host network, as the Microsoft 365 step needs it to
# reach the dev server on localhost: a make ca-up stack is then reached on its published port, the others as they are
trial_host_mode() {
  case "$1" in
    "network "*) echo local ;;
    *) echo "$1" ;;
  esac
}

# trial_scan_source_names LOG -> the vuln-scan source names make scan-dev printed to LOG, one per line
trial_scan_source_names() {
  [[ -s "$1" ]] || return 1
  local found=0 line
  while IFS= read -r line; do
    [[ "$line" =~ ^\ \ ([a-z0-9-]+-scans)\ .*drop_folder= ]] || continue
    echo "${BASH_REMATCH[1]}"
    found=1
  done <"$1"
  ((found))
}

# trial_scan_source_args LOG NAME -> hound add collector options for the vuln-scan source NAME that make scan-dev printed to LOG,
# one word per line: --name NAME, then --set key=value for each setting
trial_scan_source_args() {
  [[ -s "$1" ]] || return 1
  local line setting
  while IFS= read -r line; do
    [[ "$line" == "  $2 "* ]] || continue
    printf -- '--name\n%s\n' "$2"
    for setting in ${line#"  $2 "}; do
      printf -- '--set\n%s\n' "$setting"
    done
    return 0
  done <"$1"
  return 1
}

# trial_seed_inputs ROOT WS copies the committed fictional vendor notes into WS/inbox and the API specifications into WS/specs, which
# the agent's requests name; a file already there (the person's or the agent's) is kept
trial_seed_inputs() {
  local root="$1" ws="$2" kind file
  for kind in inbox specs; do
    mkdir -p "$ws/$kind"
    for file in "$root/examples/trial/$kind/"*; do
      [[ -e "$ws/$kind/${file##*/}" ]] || cp "$file" "$ws/$kind/"
    done
  done
}
