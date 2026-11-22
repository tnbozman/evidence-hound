#!/usr/bin/env bats
# Tests for scripts/lib/trial.sh, the pure helpers of the interactive local trial (make trial). Run: bats scripts/tests

setup() {
  # shellcheck source=../lib/trial.sh
  source "$BATS_TEST_DIRNAME/../lib/trial.sh"
}

@test "a CISO Assistant that is already running is reused, never started again" {
  [ "$(trial_choose_ca yes "" no free free)" = "bundled" ]
  [ "$(trial_choose_ca no hound-ciso-assistant_default no busy busy)" = "network hound-ciso-assistant_default" ]
  [ "$(trial_choose_ca no "" yes busy free)" = "local" ]
}

@test "the dev container's own CISO Assistant wins over one on the visible daemon" {
  [ "$(trial_choose_ca yes hound-ciso-assistant_default yes busy busy)" = "bundled" ]
}

@test "with no CISO Assistant running, free ports offer to start one" {
  [ "$(trial_choose_ca no "" no free free)" = "start" ]
}

@test "with no CISO Assistant running, a bound port blocks the trial and names the port" {
  [ "$(trial_choose_ca no "" no busy free)" = "blocked 8000" ]
  [ "$(trial_choose_ca no "" no free busy)" = "blocked 8443" ]
  [ "$(trial_choose_ca no "" no busy busy)" = "blocked 8000 8443" ]
}

@test "port report names the container that holds the port, or says it is outside Docker" {
  [ "$(trial_port_holder 8000 "other-app-1")" = "Port 8000 is held by the container other-app-1." ]
  [ "$(trial_port_holder 8443 "")" = "Port 8443 is held by a process outside Docker (check with: ss -ltnp 'sport = :8443')." ]
}

@test "network choice: host namespace for the dev container's CISO Assistant, its network for a make ca-up stack" {
  [ "$(trial_network_args bundled)" = "--network host" ]
  [ "$(trial_network_args local)" = "--network host" ]
  [ "$(trial_network_args "network hound-ciso-assistant_default")" = "--network hound-ciso-assistant_default" ]
}

@test "API address for each case" {
  [ "$(trial_ca_url bundled)" = "http://backend:8000/api/" ]
  [ "$(trial_ca_url "network hound-ciso-assistant_default")" = "http://backend:8000/api/" ]
  [ "$(trial_ca_url local)" = "http://localhost:8000/api/" ]
}

@test "hound runs in a throwaway container with the token as a file, never on the command line" {
  run trial_hound_argv evidence-hound-runner:trial "--network host" /work/.dev/trial 1000:1000 http://backend:8000/api/ no scope plan scope
  [ "$status" -eq 0 ]
  cmd="$(printf '%s ' $output)"
  [[ "$cmd" == "docker run --rm -i --user 1000:1000 --network host -v /work/.dev/trial:/trial -w /trial/repo "* ]]
  [[ "$cmd" == *"-e HOUND_CA_URL=http://backend:8000/api/ "* ]]
  [[ "$cmd" == *"-e HOUND_CA_TOKEN_FILE=/trial/secrets/ca_token "* ]]
  [[ "$cmd" == *" evidence-hound-runner:trial scope plan scope " ]]
  [[ "$cmd" != *"HOUND_CA_TOKEN="* ]]
}

@test "an interactive step gets a terminal" {
  run trial_hound_argv img "--network host" /w 1:1 http://backend:8000/api/ yes manual upload
  [[ "$(printf '%s ' $output)" == "docker run --rm -i -t "* ]]
}

@test "trial config has the manual collectors and no Microsoft 365" {
  run trial_render_config /trial no
  [ "$status" -eq 0 ]
  [[ "$output" == *"type: manual"* ]]
  [[ "$output" == *"name: example-backup-restore-test"* ]]
  [[ "$output" == *"drop_folder: /trial/manual/example-backup-restore-test"* ]]
  [[ "$output" == *"root: /trial/evidence"* ]]
  [[ "$output" == *"compliance_assessment: EX-E8-2026"* ]]
  [[ "$output" != *"m365"* ]]
  [[ "$output" != *"sophos"* ]]
}

@test "the Sophos collector uses file references for its secret only" {
  run trial_render_config /trial yes 00000000-0000-4000-8000-0000000050d1 "21.5, 22.0" 14
  [ "$status" -eq 0 ]
  [[ "$output" == *"type: sophos"* ]]
  [[ "$output" == *"client_id: 00000000-0000-4000-8000-0000000050d1"* ]]
  [[ "$output" == *'supported_firewall_firmware: "21.5, 22.0"'* ]]
  [[ "$output" == *"stale_agent_days: \"14\""* ]]
  [[ "$output" == *"client_secret: file:/trial/secrets/sophos_client_secret"* ]]
  [[ "$output" != *"client_secret: "[!f]* ]]
}

@test "a client ID that is not a plain ID is refused before it reaches the config" {
  run trial_valid_client_id 'abc"; rm -rf /'
  [ "$status" -ne 0 ]
  trial_valid_client_id 00000000-0000-4000-8000-0000000050d1
}

@test "a firmware list must be versions" {
  trial_valid_firmware "21.5, 22.0"
  run trial_valid_firmware "latest"
  [ "$status" -ne 0 ]
}
