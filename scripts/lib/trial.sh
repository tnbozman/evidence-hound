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

# trial_collector_step UNATTENDED(1|0) CONFIG TYPE -> unattended | added already | add
# A collector step needs the person's credential, so an unattended run stops before it; one already in the config is kept.
trial_collector_step() {
  local unattended="$1" config="$2" type="$3"
  if [[ "$unattended" == 1 ]]; then
    echo "unattended"
  elif [[ -f "$config" ]] && grep -q "^    type: $type\$" "$config"; then
    echo "added already"
  else
    echo "add"
  fi
}
