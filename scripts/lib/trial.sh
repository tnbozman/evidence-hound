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
  local image="$1" network="$2" workspace="$3" user="$4" url="$5" tty="$6"
  shift 6
  local words=(docker run --rm -i)
  [[ "$tty" == yes ]] && words+=(-t)
  # shellcheck disable=SC2206  # network is "--network <name>", two words on purpose
  words+=(--user "$user" $network -v "$workspace:/trial" -w /trial/repo
    -e "HOUND_CA_URL=$url" -e HOUND_CA_TOKEN_FILE=/trial/secrets/ca_token "$image" "$@")
  printf '%s\n' "${words[@]}"
}

# trial_valid_client_id ID -> exit 0 if the ID is safe to write into YAML (letters, digits, dashes)
trial_valid_client_id() {
  [[ "${1:-}" =~ ^[A-Za-z0-9-]{8,64}$ ]]
}

# trial_valid_firmware LIST -> exit 0 if LIST is comma-separated versions such as "21.5, 22.0"
trial_valid_firmware() {
  [[ "${1:-}" =~ ^[0-9]+(\.[0-9]+)*([[:space:]]*,[[:space:]]*[0-9]+(\.[0-9]+)*)*$ ]]
}

# trial_render_config ROOT WITH_SOPHOS [CLIENT_ID FIRMWARE STALE_DAYS] -> the trial's hound.yaml
# ROOT is the workspace as hound sees it (/trial in the container). Manual collectors only, no Microsoft 365.
trial_render_config() {
  local root="$1" sophos="$2" client_id="${3:-}" firmware="${4:-}" stale="${5:-14}"
  cat <<YAML
# Written by scripts/trial.sh for the interactive local trial. Fictional organisation; lives in .dev/trial (git-ignored).
# Paths are as hound's container sees them. Guide: docs/tutorials/local-trial.md

ciso_assistant:
  url: http://backend:8000/api/          # HOUND_CA_URL overrides this
  token: env:HOUND_CA_TOKEN              # read from HOUND_CA_TOKEN_FILE, a file in $root/secrets

evidence:
  root: $root/evidence
  retention_days: 90

scheduler:
  time_zone: Australia/Sydney

scope: scope

collectors:
  - name: example-incident-register
    type: manual
    schedule: "0 6 * * *"
    settings:
      drop_folder: $root/manual/example-incident-register
      checks: incident.reported_to_ciso, incident.reported_to_asd, incident.response_plan_followed
      due_days: 31
      owner: Alex Example

  - name: example-backup-restore-test
    type: manual
    schedule: "0 6 * * *"
    settings:
      drop_folder: $root/manual/example-backup-restore-test
      checks: backup.restore_tested
      due_days: 92
      owner: Sam Example
YAML
  if [[ "$sophos" == yes ]]; then
    cat <<YAML

  # Your own Sophos Central tenant, read with a Service Principal Read-Only credential. Guide: docs/collectors/sophos.md
  - name: sophos
    type: sophos
    schedule: "0 4 * * *"
    settings:
      client_id: $client_id
      supported_firewall_firmware: "$firmware"
      stale_agent_days: "$stale"
    secrets:
      client_secret: file:$root/secrets/sophos_client_secret
YAML
  fi
  cat <<YAML

target:
  mapping: mappings/essential-eight-2023.yaml
  compliance_assessment: EX-E8-2026
  health_control: AC-AUTO-EVIDENCE
YAML
}
