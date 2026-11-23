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
  [ "$(trial_network_args "network hound-ciso-assistant_default")" = "--network hound-ciso-assistant_default --add-host host.docker.internal:host-gateway" ]
}

@test "the Sophos Central dev server's address as hound's container sees it, for each case" {
  [ "$(trial_dev_url bundled 5370)" = "http://localhost:5370/" ]
  [ "$(trial_dev_url local 5370)" = "http://localhost:5370/" ]
  [ "$(trial_dev_url "network hound-ciso-assistant_default" 5370)" = "http://host.docker.internal:5370/" ]
}

@test "the sophos collector for the dev server reads it with the sophos-central profile and asks for no credential" {
  run trial_sophos_dev_sets http://localhost:5370/
  [ "$status" -eq 0 ]
  sets="$(printf '%s|' "${lines[@]}")"
  [ "$sets" = "--set|profile=sophos-central|--set|profiles_path=/trial/profiles|--set|client_id=00000000-0000-4000-8000-0000050d0001|--set|id_url=http://localhost:5370/|--set|api_url=http://localhost:5370/|--set|supported_firewall_firmware=21.0, 21.5|" ]
}

@test "a sophos collector already in the config uses the dev server when its address is the dev server's" {
  config="$BATS_TEST_TMPDIR/hound.yaml"
  printf 'collectors:\n  - name: sophos\n    settings:\n      id_url: http://localhost:5370/\n' >"$config"
  trial_uses_dev_server "$config" 5370
  printf 'collectors:\n  - name: sophos\n    settings:\n      id_url: https://id.sophos.com/\n' >"$config"
  ! trial_uses_dev_server "$config" 5370
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

@test "a collector step is skipped when unattended, kept when the collector is already added by name, otherwise added" {
  config="$BATS_TEST_TMPDIR/hound.yaml"
  printf 'collectors:\n  - name: sophos\n    type: rest\n' >"$config"
  [ "$(trial_collector_step 1 "$config" m365)" = "unattended" ]
  [ "$(trial_collector_step 0 "$config" sophos)" = "added already" ]
  [ "$(trial_collector_step 0 "$config" rest)" = "add" ]
  [ "$(trial_collector_step 0 "$config" m365)" = "add" ]
  [ "$(trial_collector_step 0 "$BATS_TEST_TMPDIR/missing.yaml" m365)" = "add" ]
}

@test "each agent stage's request names what to draft, the skills and the person's part" {
  run trial_brief third-parties EX-E8 SOPHOS-NETWORK
  [[ "$output" == *"Sophos"* && "$output" == *"managed service provider"* && "$output" == *"onboard-a-third-party"* && "$output" == *"hound supplier draft"* ]]
  run trial_brief system EX-E8 SOPHOS-NETWORK
  [[ "$output" == *"SOPHOS-NETWORK"* && "$output" == *"onboard-a-system-with-hound"* && "$output" == *"pending"* && "$output" == *"../specs"* ]]
  run trial_brief audit EX-E8 SOPHOS-NETWORK
  [[ "$output" == *"decide"* && "$output" == *"review-before-apply"* && "$output" == *"EX-E8-SOPHOS-NETWORK"* ]]
  run trial_brief controls EX-E8 SOPHOS-NETWORK
  [[ "$output" == *"hound add control"* && "$output" == *"--audit EX-E8-SOPHOS-NETWORK"* ]]
  run trial_brief unknown EX-E8 SOPHOS-NETWORK
  [ "$status" -ne 0 ]
}

@test "a stage is ready once its draft is in the repo" {
  repo="$BATS_TEST_TMPDIR/repo"
  mkdir -p "$repo/scope/third-parties" "$repo/onboarding/SOPHOS-NETWORK" "$repo/scope/audits"
  run trial_stage_ready third-parties "$repo" SOPHOS-NETWORK
  [ "$status" -ne 0 ]
  touch "$repo/scope/third-parties/sup-sophos.yaml"
  run trial_stage_ready third-parties "$repo" SOPHOS-NETWORK
  [ "$status" -ne 0 ]
  touch "$repo/scope/third-parties/sup-example-msp.yaml"
  trial_stage_ready third-parties "$repo" SOPHOS-NETWORK
  run trial_stage_ready system "$repo" SOPHOS-NETWORK
  [ "$status" -ne 0 ]
  printf '{"stage": "deciding"}' >"$repo/onboarding/SOPHOS-NETWORK/session.json"
  trial_stage_ready system "$repo" SOPHOS-NETWORK
  run trial_stage_ready audit "$repo" SOPHOS-NETWORK
  [ "$status" -ne 0 ]
  touch "$repo/scope/audits/sophos-network-essential-eight-2023.yaml"
  trial_stage_ready audit "$repo" SOPHOS-NETWORK
  printf 'controls:\n  - ref_id: AC-AUTO-EVIDENCE\n' >"$repo/scope/controls.yaml"
  run trial_stage_ready controls "$repo" SOPHOS-NETWORK
  [ "$status" -ne 0 ]
  printf '  - ref_id: AC-EXTERNAL-SCAN\n' >>"$repo/scope/controls.yaml"
  trial_stage_ready controls "$repo" SOPHOS-NETWORK
}

@test "hound for the agent runs in the folder it is called from, inside the trial workspace only" {
  [ "$(trial_workdir /work/.dev/trial /work/.dev/trial/repo)" = "/trial/repo" ]
  [ "$(trial_workdir /work/.dev/trial /work/.dev/trial/repo/scope/audits)" = "/trial/repo/scope/audits" ]
  [ "$(trial_workdir /work/.dev/trial /work/.dev/trial)" = "/trial" ]
  run trial_workdir /work/.dev/trial /work/.dev/trial-other
  [ "$status" -ne 0 ]
  run trial_workdir /work/.dev/trial /work
  [ "$status" -ne 0 ]
  run trial_hound_argv_in /trial/repo/scope img "--network host" /w 1:1 http://backend:8000/api/ no scope validate .
  [[ "$(printf '%s ' $output)" == *" -v /w:/trial -w /trial/repo/scope "* ]]
}

@test "the unattended replay uses committed fictional files" {
  fixtures="$BATS_TEST_DIRNAME/../../examples/trial"
  for f in third-parties/sup-sophos.yaml third-parties/sup-example-msp.yaml; do
    grep -q "status: active" "$fixtures/$f"
    grep -q "result: ok" "$fixtures/$f"
  done
  [ -f "$BATS_TEST_DIRNAME/../../examples/onboarding-tutorial/answers/office-firewall.decisions.txt" ]
  run grep -nE 'Example backup service|AC-BACKUP|example-backup-restore-test' "$BATS_TEST_DIRNAME/../trial.sh"
  [ "$status" -eq 1 ]
}
