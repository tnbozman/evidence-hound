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

@test "the scene names each part of the private deployment" {
  run trial_scene
  [ "$status" -eq 0 ]
  for part in "Oracle Cloud" "private" "site-to-site VPN" "XGS 88W" "CISO Assistant" "Evidence Hound" "Microsoft 365" "Essential Eight" "GRC-PLATFORM" "OFFICE-FIREWALL"; do
    [[ "$output" == *"$part"* ]] || { echo "missing: $part"; return 1; }
  done
}

@test "the trial onboards the onboarding tutorial's two systems" {
  [ "$(trial_systems)" = "GRC-PLATFORM OFFICE-FIREWALL" ]
  for system in $(trial_systems); do
    [ -f "$BATS_TEST_DIRNAME/../../examples/onboarding-tutorial/answers/$(printf '%s' "$system" | tr '[:upper:]' '[:lower:]').decisions.txt" ]
    [ -n "$(trial_system_solution "$system")" ]
    [ -n "$(trial_system_product "$system")" ]
  done
  [[ "$(trial_system_product OFFICE-FIREWALL)" == *"XGS 88W"* ]]
  [[ "$(trial_system_product GRC-PLATFORM)" == *"OCI"* ]]
  run trial_system_solution SOPHOS-NETWORK
  [ "$status" -ne 0 ]
}

@test "each agent stage's request names what to draft, the skills and the person's part" {
  run trial_brief third-parties EX-E8
  for name in Oracle Sophos "CISO Assistant" "Evidence Hound" GRC-PLATFORM OFFICE-FIREWALL onboard-a-third-party "hound supplier draft" "hound approve"; do
    [[ "$output" == *"$name"* ]] || { echo "missing: $name"; return 1; }
  done
  run trial_brief system EX-E8 GRC-PLATFORM
  [[ "$output" == *"GRC-PLATFORM"* && "$output" == *"onboard-a-system-with-hound"* && "$output" == *"pending"* && "$output" == *"SOL-OCI-HOSTING"* ]]
  run trial_brief system EX-E8 OFFICE-FIREWALL
  [[ "$output" == *"XGS 88W"* && "$output" == *"../specs"* ]]
  run trial_brief audit EX-E8 OFFICE-FIREWALL
  [[ "$output" == *"decide"* && "$output" == *"review-before-apply"* && "$output" == *"EX-E8-OFFICE-FIREWALL"* ]]
  run trial_brief controls EX-E8 GRC-PLATFORM
  [[ "$output" == *"hound add control"* && "$output" == *"--audit EX-E8-GRC-PLATFORM"* ]]
  run trial_brief unknown EX-E8 GRC-PLATFORM
  [ "$status" -ne 0 ]
}

@test "a stage is ready once its draft is in the repo" {
  repo="$BATS_TEST_TMPDIR/repo"
  mkdir -p "$repo/scope/third-parties" "$repo/onboarding/GRC-PLATFORM" "$repo/scope/audits"
  for f in sup-oracle sup-sophos sup-ciso-assistant; do
    touch "$repo/scope/third-parties/$f.yaml"
    run trial_stage_ready third-parties "$repo"
    [ "$status" -ne 0 ]
  done
  touch "$repo/scope/third-parties/sup-evidence-hound.yaml"
  trial_stage_ready third-parties "$repo"
  run trial_stage_ready system "$repo" GRC-PLATFORM
  [ "$status" -ne 0 ]
  printf '{"stage": "deciding"}' >"$repo/onboarding/GRC-PLATFORM/session.json"
  trial_stage_ready system "$repo" GRC-PLATFORM
  run trial_stage_ready audit "$repo" GRC-PLATFORM
  [ "$status" -ne 0 ]
  touch "$repo/scope/audits/grc-platform-essential-eight-2023.yaml"
  trial_stage_ready audit "$repo" GRC-PLATFORM
  run trial_stage_ready audit "$repo" OFFICE-FIREWALL
  [ "$status" -ne 0 ]
  printf 'controls:\n  - ref_id: AC-AUTO-EVIDENCE\n' >"$repo/scope/controls.yaml"
  run trial_stage_ready controls "$repo" GRC-PLATFORM
  [ "$status" -ne 0 ]
  printf '  - ref_id: AC-RESTORE-TEST\n    systems: [GRC-PLATFORM]\n' >>"$repo/scope/controls.yaml"
  trial_stage_ready controls "$repo" GRC-PLATFORM
  run trial_stage_ready controls "$repo" OFFICE-FIREWALL
  [ "$status" -ne 0 ]
}

@test "a system's audit becomes a target: a single target turns into a list, and adding it twice adds it once" {
  config="$BATS_TEST_TMPDIR/hound.yaml"
  printf 'collectors: []\ntarget:\n  mapping: mappings/essential-eight-2023.yaml\n  compliance_assessment: EX-E8\n  health_control: AC-AUTO-EVIDENCE\n' >"$config"
  trial_add_target "$config" mappings/grc-platform-essential-eight-2023.yaml EX-E8-GRC-PLATFORM
  printf 'scheduler:\n  time_zone: UTC\n' >>"$config"
  trial_add_target "$config" mappings/grc-platform-essential-eight-2023.yaml EX-E8-GRC-PLATFORM
  trial_add_target "$config" mappings/grc-platform-essential-eight-2023.yaml EX-E8-GRC-PLATFORM
  trial_add_target "$config" mappings/office-firewall-essential-eight-2023.yaml EX-E8-OFFICE-FIREWALL
  run cat "$config"
  [[ "$output" == *"targets:"* && "$output" != *$'\ntarget:'* ]]
  [[ "$output" == *"  - mapping: mappings/essential-eight-2023.yaml"$'\n'"    compliance_assessment: EX-E8"$'\n'"    health_control: AC-AUTO-EVIDENCE"* ]]
  [ "$(grep -c 'compliance_assessment: EX-E8-GRC-PLATFORM$' "$config")" -eq 1 ]
  [ "$(grep -c 'compliance_assessment: EX-E8-OFFICE-FIREWALL$' "$config")" -eq 1 ]
  [[ "$output" == "collectors: []"* ]]
  [ "$(grep -n 'EX-E8-OFFICE-FIREWALL' "$config" | cut -d: -f1)" -lt "$(sed -n '/^scheduler:/=' "$config")" ]
  run grep -A2 '^scheduler:' "$config"
  [[ "$output" == *"time_zone: UTC"* ]]
}

@test "the IaC collectors read the pipeline dev server with their profiles and a placeholder token" {
  run trial_iac_dev_sets http://localhost:5380/ grc-infra-controls
  [[ "$(printf '%s ' $output)" == *"--set profile=grc-infra-controls --set profiles_path=/trial/profiles --set api_url=http://localhost:5380/ --set repository=example/grc-infra --set workflow=controls.yml"* ]]
  run trial_iac_dev_sets http://localhost:5380/ terraform-drift
  [[ "$output" == *"workflow=drift.yml"* && "$output" == *"profile=terraform-drift"* ]]
}

@test "a control-test report from terraform test goes to the dev server when there is one" {
  [ -z "$(trial_controls_report_args "$BATS_TEST_TMPDIR")" ]
  touch "$BATS_TEST_TMPDIR/controls.xml"
  [ "$(trial_controls_report_args "$BATS_TEST_TMPDIR")" = "--controls-report $BATS_TEST_TMPDIR/controls.xml" ]
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
  for f in third-parties/sup-oracle.yaml third-parties/sup-sophos.yaml third-parties/sup-ciso-assistant.yaml third-parties/sup-evidence-hound.yaml; do
    grep -q "status: active" "$fixtures/$f"
    grep -q "result: ok" "$fixtures/$f"
  done
  [ -f "$BATS_TEST_DIRNAME/../../examples/onboarding-tutorial/answers/office-firewall.decisions.txt" ]
  [ -f "$BATS_TEST_DIRNAME/../../examples/onboarding-tutorial/answers/grc-platform.decisions.txt" ]
  [ ! -e "$fixtures/third-parties/sup-example-msp.yaml" ]
  run grep -nE 'Example backup service|AC-BACKUP|example-backup-restore-test' "$BATS_TEST_DIRNAME/../trial.sh"
  [ "$status" -eq 1 ]
}

@test "the dev server collector is added with its settings and no sign-in" {
  # s09-53: the settings the dev server prints, as make m365-dev shows them.
  log="$BATS_TEST_TMPDIR/m365-dev.log"
  cat >"$log" <<'LOG'
Example Pty Ltd Microsoft 365 dev tenant (compliant; deviations: none)
Microsoft Graph answers at http://localhost:5365/ (try http://localhost:5365/v1.0/users)
Collector settings for this tenant:
  tenant_id: "00000000-0000-4000-8000-00000000e044"
  client_id: "00000000-0000-4000-8000-00000000c044"
  break_glass_accounts: "00000000-0000-4000-8001-000000000003, 00000000-0000-4000-8001-000000000004"
  supported_windows_builds: "10.0.22631, 10.0.26100"
  graph_url: http://localhost:5365/
Settings from the ASD Blueprint for Secure Cloud, © Commonwealth of Australia 2023, CC BY 4.0. Ctrl+C stops the server.
LOG
  run trial_m365_dev_args "$log"
  [ "$status" -eq 0 ]
  args="$(printf '%s|' "${lines[@]}")"
  [[ "$args" == *"--set|tenant_id=00000000-0000-4000-8000-00000000e044|"* ]]
  [[ "$args" == *"--set|break_glass_accounts=00000000-0000-4000-8001-000000000003, 00000000-0000-4000-8001-000000000004|"* ]]
  [[ "$args" == *"--set|supported_windows_builds=10.0.22631, 10.0.26100|"* ]]
  [[ "$args" == *"--set|graph_url=http://localhost:5365/|"* ]]
  [[ "$args" == *"--set|sign_in=none|" ]]
  run trial_m365_dev_args "$BATS_TEST_TMPDIR/missing.log"
  [ "$status" -ne 0 ]
}

@test "the dev server step runs hound on the host network, where localhost is the dev container" {
  [ "$(trial_host_mode bundled)" = "bundled" ]
  [ "$(trial_host_mode local)" = "local" ]
  [ "$(trial_host_mode "network hound-ciso-assistant_default")" = "local" ]
}

@test "the scan step reads the sources make scan-dev printed" {
  local log="$BATS_TEST_DIRNAME/fixtures/scan-dev.log"
  run trial_scan_source_names "$log"
  [ "$status" -eq 0 ]
  [ "${lines[*]}" = "internet-facing-scans laptops-scans" ]
  run trial_scan_source_args "$log" laptops-scans
  [ "$status" -eq 0 ]
  args="$(printf '%s|' "${lines[@]}")"
  [ "$args" = "--name|laptops-scans|--set|drop_folder=../scans/laptops|--set|format=csv|--set|scope=common_apps|--set|owner=it@example.com|--set|column_host=DeviceName|--set|column_finding_id=CveId|" ]
  run trial_scan_source_args "$log" nowhere-scans
  [ "$status" -ne 0 ]
  run trial_scan_source_names "$BATS_TEST_TMPDIR/missing.log"
  [ "$status" -ne 0 ]
}
