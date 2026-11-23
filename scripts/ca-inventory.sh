#!/usr/bin/env bash
# Probe a CISO Assistant instance for the facts docs/ciso-assistant-configuration.md relies on, and save its
# OpenAPI schema. Creates a few objects named "hound-probe-*" or ref_id "EX-PROBE-*" in the seeded Example
# Pty Ltd domain, and deletes them again. Never point it at a production instance.
#
# Usage: HOUND_CA_TOKEN=... scripts/ca-inventory.sh <api url ending in /api/> <output folder>
# Writes <output>/openapi.json, <output>/openapi-paths.json and <output>/answers.json, and prints the answers.
set -uo pipefail

api="${1:?usage: ca-inventory.sh <api url> <output folder>}"
out="${2:?usage: ca-inventory.sh <api url> <output folder>}"
: "${HOUND_CA_TOKEN:?set HOUND_CA_TOKEN to a personal access token}"
api="${api%/}/"
mkdir -p "$out"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# call METHOD PATH [JSON BODY]: prints "<status> <body>" on one line.
call() {
  local method="$1" path="$2" body="${3:-}" status
  local args=(-s -o "$work/body" -w '%{http_code}' -X "$method" -H "Authorization: Token $HOUND_CA_TOKEN" -H 'Accept: application/json')
  if [[ -n "$body" ]]; then
    args+=(-H 'Content-Type: application/json' --data "$body")
  fi
  status="$(curl "${args[@]}" "$api$path")"
  printf '%s %s\n' "$status" "$(tr -d '\n' < "$work/body")"
}

status_of() { cut -d' ' -f1 <<< "$1"; }
body_of() { cut -d' ' -f2- <<< "$1"; }
first_id() { body_of "$1" | jq -r '(.results // .)[0].id // empty'; }

answers='{}'
answer() { answers="$(jq --arg k "$1" --argjson v "$2" '.[$k] = $v' <<< "$answers")"; }

# --- OpenAPI schema -----------------------------------------------------------------------------
curl -s -H "Authorization: Token $HOUND_CA_TOKEN" "${api}schema/?format=json" > "$out/openapi.json"
schema="$out/openapi.json"
# The snapshot hound's endpoints are checked against: the operations on the resources hound uses or s06 configures,
# small enough to commit. The full schema stays in openapi.json.
snapshot_resources='^/api/(health|schema|requirement-assessments|compliance-assessments|evidences|evidence-revisions|applied-controls|folders|perimeters|assets|stored-libraries|loaded-libraries|frameworks|requirement-nodes|reference-controls|risk-matrices|entities|settings|users|actors|user-groups|role-assignments)/'
jq --arg re "$snapshot_resources" '{openapi, info: {title: .info.title, version: .info.version}, all_paths: (.paths | length),
  paths: (.paths | with_entries(select(.key | test($re))) | map_values(with_entries(select(.key | test("^(get|post|put|patch|delete)$")) | .value = {operationId: .value.operationId})))}' \
  "$schema" > "$out/openapi-paths.json"
answer schema "$(jq '{openapi, version: .info.version, paths: (.paths | length), components: (.components.schemas | length)}' "$schema")"

# Properties of the write and read components whose name starts with $1.
props() { jq --arg p "$1" '[.components.schemas | to_entries[] | select(.key | startswith($p)) | {(.key): (.value.properties // {} | keys)}] | add // {}' "$schema"; }
paths_like() { jq --arg re "$1" '[.paths | keys[] | select(test($re))]' "$schema"; }

# --- Setup: the seeded perimeter and the Essential Eight 2023 framework ---------------------------
perimeter="$(call GET 'perimeters/?name=Corporate%20IT')"
perimeter_id="$(first_id "$perimeter")"
folder_id="$(body_of "$perimeter" | jq -r '(.results // .)[0].folder.id // empty')"
framework_id="$(first_id "$(call GET 'frameworks/?urn=urn:intuitem:risk:framework:essential-eight-2023')")"
answer setup "$(jq -n --arg p "$perimeter_id" --arg f "$folder_id" --arg fw "$framework_id" '{perimeter: ($p != ""), folder: ($f != ""), framework: ($fw != "")}')"

new_audit() { # ref_id [extra json fields]
  local extra="${2:-}"
  [[ -n "$extra" ]] || extra='{}'
  call POST compliance-assessments/ "$(jq -n --arg r "$1" --arg p "$perimeter_id" --arg fo "$folder_id" --arg f "$framework_id" --argjson x "$extra" \
    '{name: ("hound-probe " + $r), ref_id: $r, perimeter: $p, folder: $fo, framework: $f} + $x')"
}
first_row() { first_id "$(call GET "requirement-assessments/?compliance_assessment=$1&limit=1")"; }

# --- Q1: does locking an audit freeze its linked applied controls? -------------------------------
audit="$(new_audit EX-PROBE-LOCK)"
audit_id="$(body_of "$audit" | jq -r '.id // empty')"
control_id="$(body_of "$(call POST applied-controls/ "$(jq -n --arg f "$folder_id" '{name: "hound-probe lock control", ref_id: "AC-PROBE-LOCK", folder: $f, status: "to_do"}')")" | jq -r '.id // empty')"
row_id="$(first_row "$audit_id")"
call PATCH "requirement-assessments/$row_id/" "$(jq -n --arg c "$control_id" '{applied_controls: [$c], result: "compliant"}')" > /dev/null
lock="$(call PATCH "compliance-assessments/$audit_id/" '{"is_locked": true}')"
control_write="$(call PATCH "applied-controls/$control_id/" '{"status": "active"}')"
row_write="$(call PATCH "requirement-assessments/$row_id/" '{"observation": "hound-probe"}')"
audit_write="$(call PATCH "compliance-assessments/$audit_id/" '{"description": "hound-probe"}')"
answer lock_freezes_controls "$(jq -n \
  --arg audit "$(status_of "$audit")" --arg lock "$(status_of "$lock")" \
  --arg c "$(status_of "$control_write")" --arg cs "$(body_of "$control_write" | jq -r '.status // empty')" \
  --arg r "$(status_of "$row_write")" --arg rb "$(body_of "$row_write" | head -c 200)" \
  --arg a "$(status_of "$audit_write")" --arg ab "$(body_of "$audit_write" | head -c 200)" \
  '{create_audit: $audit, lock: $lock, control_patch: $c, control_status_after: $cs, row_patch: $r, row_patch_body: $rb, audit_patch: $a, audit_patch_body: $ab}')"

# --- Q3: audit status and date fields, and creating an audit from a baseline ----------------------
call PATCH "compliance-assessments/$audit_id/" '{"is_locked": false}' > /dev/null
baseline="$(new_audit EX-PROBE-BASELINE "$(jq -n --arg b "$audit_id" '{baseline: $b}')")"
baseline_id="$(body_of "$baseline" | jq -r '.id // empty')"
copied="$(call GET "requirement-assessments/?compliance_assessment=$baseline_id&result=compliant")"
answer audit_fields "$(jq -n --argjson props "$(props ComplianceAssessmentWrite)" \
  --argjson status "$(jq '[.components.schemas | to_entries[] | select(.key | test("^Status[0-9]*Enum$|ComplianceAssessment.*Status")) | {(.key): .value.enum}] | add // {}' "$schema")" \
  --arg b "$(status_of "$baseline")" --arg n "$(body_of "$copied" | jq -r '.count // (.results | length) // empty')" \
  '{write_fields: ($props | to_entries | map(.value) | add // [] | map(select(test("status|eta|due|date|start|end|baseline|lock|author|reviewer|visibility|ref_id")))), status_enums: $status, baseline_create: $b, compliant_rows_copied: $n}')"

# --- Q4: is a write to a field the role can only read ignored silently? ----------------------------
fv="$(new_audit EX-PROBE-FV '{"field_visibility": {"observation": {"auditor": "read", "respondent": "hidden"}}}')"
fv_id="$(body_of "$fv" | jq -r '.id // empty')"
fv_row="$(first_row "$fv_id")"
fv_write="$(call PATCH "requirement-assessments/$fv_row/" '{"observation": "hound-probe", "result": "compliant"}')"
fv_after="$(call GET "requirement-assessments/$fv_row/")"
answer read_only_write "$(jq -n --arg c "$(status_of "$fv")" --arg w "$(status_of "$fv_write")" \
  --arg o "$(body_of "$fv_after" | jq -r '.observation // ""')" --arg r "$(body_of "$fv_after" | jq -r '.result // ""')" \
  '{create_with_visibility: $c, patch: $w, observation_after: $o, result_after: $r}')"

# --- Q2: evidence revisions and their SHA-256 -----------------------------------------------------
evidence_id="$(body_of "$(call POST evidences/ "$(jq -n --arg f "$folder_id" '{name: "hound-probe evidence", ref_id: "EX-PROBE-EVIDENCE", folder: $f}')")" | jq -r '.id // empty')"
printf 'hound probe\n' > "$work/probe.txt"
upload() {
  curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Token $HOUND_CA_TOKEN" \
    -F "evidence=$evidence_id" -F "attachment=@$work/probe.txt;filename=probe.txt" "${api}evidence-revisions/"
}
first_upload="$(upload)"
second_upload="$(upload)"
revisions="$(call GET "evidence-revisions/?evidence=$evidence_id&ordering=-version")"
answer evidence_revision_hash "$(jq -n --arg u1 "$first_upload" --arg u2 "$second_upload" \
  --arg expected "$(sha256sum "$work/probe.txt" | cut -d' ' -f1)" \
  --argjson revs "$(body_of "$revisions" | jq '[(.results // .)[] | {version, attachment_hash}]')" \
  '{upload_status: [$u1, $u2], expected_sha256: $expected, revisions: $revs, hash_matches: ($revs | length > 0 and .[0].attachment_hash == $expected)}')"

# --- Q5: custom library upload, main entity, feature flags -----------------------------------------
cat > "$work/hound-probe-library.yaml" <<'YAML'
urn: urn:example:risk:library:hound-probe
locale: en
ref_id: hound-probe
name: Hound probe library
description: A fictional one-requirement framework used by scripts/ca-inventory.sh.
copyright: Example Pty Ltd
version: 1
publication_date: 2026-01-01
provider: Example Pty Ltd
packager: example
objects:
  framework:
    urn: urn:example:risk:framework:hound-probe
    ref_id: hound-probe
    name: Hound probe framework
    requirement_nodes:
      - urn: urn:example:risk:req_node:hound-probe:p-1
        assessable: true
        depth: 1
        ref_id: P-1
        name: Probe requirement
YAML
library_upload="$(curl -s -o "$work/upload" -w '%{http_code}' -H "Authorization: Token $HOUND_CA_TOKEN" \
  -H 'Content-Disposition: attachment; filename=hound-probe-library.yaml' -H 'Content-Type: application/octet-stream' \
  --data-binary "@$work/hound-probe-library.yaml" "${api}stored-libraries/upload/")"
main="$(call GET 'entities/?builtin=true')"
main_id="$(first_id "$main")"
main_description="$(body_of "$(call GET "entities/$main_id/")" | jq -r '.description // ""')"
main_write="$(call PATCH "entities/$main_id/" "$(jq -n --arg d "$main_description" '{description: $d}')")"
flags="$(call GET 'settings/feature-flags/')"
flags_write="$(call PATCH 'settings/feature-flags/' "$(body_of "$flags" | jq -c '.')")"
answer api_configuration "$(jq -n --arg lib "$library_upload" --arg libbody "$(head -c 200 "$work/upload")" \
  --arg main "$(status_of "$main")" --arg mainid "$main_id" --arg mw "$(status_of "$main_write")" \
  --arg fg "$(status_of "$flags")" --arg fw "$(status_of "$flags_write")" \
  --argjson flagkeys "$(body_of "$flags" | jq '(.value // .) | if type == "object" then keys else [] end' 2>/dev/null)" \
  --argjson upload_paths "$(paths_like 'librar.*upload|upload.*librar')" \
  '{library_upload: $lib, library_upload_body: $libbody, upload_paths: $upload_paths, main_entity_list: $main, main_entity_found: ($mainid != ""), main_entity_patch: $mw, feature_flags_get: $fg, feature_flags_patch: $fw, feature_flag_keys: $flagkeys}')"

# --- Q6: ref_id on BIAs, entity assessments and metric instances; entity assessment revisions -------
answer ref_ids "$(jq -n \
  --argjson bia "$(props BusinessImpactAnalysis)" --argjson ea "$(props EntityAssessment)" --argjson mi "$(props MetricInstance)" \
  --argjson revision_paths "$(paths_like 'entity-assessments/.*revis|entity-assessment-revisions|entity_assessment')" \
  '{business_impact_analysis: ($bia | map_values(index("ref_id") != null)), entity_assessment: ($ea | map_values(index("ref_id") != null)), metric_instance: ($mi | map_values(index("ref_id") != null)), entity_assessment_revision_paths: $revision_paths}')"

# --- Q7 (s10-04): recurring task templates, team emails and evidence labels, for the evidence plan --------------------------
task_probe="$(call POST "task-templates/" "$(jq -n --arg f "$folder_id" '{name: "hound probe task", ref_id: "TT-HOUND-PROBE", folder: $f,
  description: "probe", is_recurrent: true, task_date: "2027-01-31", enabled: true,
  schedule: {interval: 3, frequency: "MONTHLY", days_of_week: [], weeks_of_month: [], months_of_year: []}}')")"
task_id="$(body_of "$task_probe" | jq -r '.id // empty')"
task_read="$( [[ -n "$task_id" ]] && call GET "task-templates/$task_id/" || echo "000 {}")"
answer task_templates "$(jq -n \
  --argjson paths "$(paths_like '^/api/task-(templates|nodes)/')" \
  --argjson tt "$(props TaskTemplate)" --argjson groups "$(props UserGroup)" --argjson ev "$(props Evidence)" \
  --argjson enums "$(jq '[.components.schemas | to_entries[] | select(.key | test("Frequency|Schedule|Recurr|TaskNode.*Status|Status.*Enum")) | {(.key): (.value.enum // .value.properties // .value)}] | add // {}' "$schema")" \
  --arg cs "$(status_of "$task_probe")" --arg cb "$(body_of "$task_probe" | head -c 1500)" --arg rb "$(body_of "$task_read" | head -c 2500)" \
  '{paths: $paths, task_template_fields: $tt, user_group_fields: $groups, evidence_fields: $ev, enums: $enums, create_status: $cs, create_body: $cb, read_body: $rb}')"
[[ -n "$task_id" ]] && call DELETE "task-templates/$task_id/" > /dev/null

# --- Clean up ------------------------------------------------------------------------------------
for id in "$baseline_id" "$fv_id" "$audit_id"; do
  [[ -n "$id" ]] && call DELETE "compliance-assessments/$id/" > /dev/null
done
[[ -n "$control_id" ]] && call DELETE "applied-controls/$control_id/" > /dev/null
[[ -n "$evidence_id" ]] && call DELETE "evidences/$evidence_id/" > /dev/null

printf '%s\n' "$answers" | tee "$out/answers.json"
