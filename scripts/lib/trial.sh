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

# trial_dev_url MODE PORT -> a dev server's address, listening in the dev container on PORT, as hound's container sees it
# With the host network hound shares the dev container's namespace; on a make ca-up network it reaches the dev container,
# which is its Docker host, as host.docker.internal.
trial_dev_url() {
  case "$1" in
    "network "*) echo "http://host.docker.internal:$2/" ;;
    *) echo "http://localhost:$2/" ;;
  esac
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

# trial_agent_brief AUDIT -> what to ask the coding agent in the trial repo
trial_agent_brief() {
  cat <<TEXT
Onboard the Sophos perimeter firewall with hound for the $1 audit: draft Sophos and the managed service provider
as third parties, then the firewall as a system, its audit decisions and its manual evidence. Use the API
specification and vendor documents in ../specs and ../inbox. Run the review before proposing apply.
I approve in my own terminal with hound approve and hound manual approve.
TEXT
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
