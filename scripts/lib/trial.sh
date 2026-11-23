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
    "network "*) echo "--network ${1#network }" ;;
    *) echo "--network host" ;;
  esac
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

# trial_brief STAGE SERIES SYSTEM -> what to ask the coding agent for one stage, with the skills it uses; fails for an unknown stage
trial_brief() {
  local series="$2" system="$3"
  case "$1" in
    third-parties) cat <<TEXT
Draft the third parties for the Sophos perimeter firewall ($system): Sophos, who makes it, and the
managed service provider that runs it for us (Example Managed Services Pty Ltd, an msp for Sophos). Use the skills onboard-a-third-party
and assess-a-supplier, with hound supplier draft. Read the vendor documents in ../inbox. Give each level its reason.
I approve them and conclude their reviews myself, with hound approve.
TEXT
      ;;
    system) cat <<TEXT
Start onboarding the Sophos perimeter firewall as system $system with the skill onboard-a-system-with-hound:
hound onboard $system pending --json --repo . with its owner, product, solution and maturity level groups,
and the API specification in ../specs. Ask me for what you cannot read from the documents.
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

# trial_stage_ready STAGE REPO SYSTEM -> 0 once the stage's draft is in the repo: two third parties, the system's session, its
# audit (written when the person approves), a control besides hound init's health control
trial_stage_ready() {
  local repo="$2" slug
  slug="$(printf '%s' "$3" | tr '[:upper:]' '[:lower:]')"
  case "$1" in
    third-parties) (($(find "$repo/scope/third-parties" -maxdepth 1 -name '*.yaml' 2>/dev/null | wc -l) >= 2)) ;;
    system) [[ -s "$repo/onboarding/$3/session.json" ]] ;;
    audit) compgen -G "$repo/scope/audits/$slug-*.yaml" >/dev/null ;;
    controls) (($(grep -c '^  - ref_id: AC-' "$repo/scope/controls.yaml" 2>/dev/null || true) >= 2)) ;;
    *) return 1 ;;
  esac
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
