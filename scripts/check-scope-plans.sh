#!/usr/bin/env bash
# s08-06 and s08-07 functional checks against the local CISO Assistant (make ca-up), on a copy of the example
# compliance repo, with the URL and token in .dev/ca.env:
#   s08-06  one hand-written result gained and one regressed: the plan's "Changes to compliance" section.
#   s08-07  --detailed-exitcode exits 2; a saved plan is refused once its object changes in CISO Assistant
#           (scope.plan_stale); a fresh saved plan applies.
# It changes the example objects in that CISO Assistant, then puts them back with scripts/ca-apply-scope.sh.
# For CI and throwaway instances. Usage: scripts/check-scope-plans.sh
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$root/.dev/ca.env"
[[ -s "$env_file" ]] || { echo "check-scope-plans: $env_file is missing; run make ca-up first" >&2; exit 1; }
set -a
# shellcheck source=/dev/null
. "$env_file"
set +a

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cp -R "$root/examples/compliance-repo" "$work/repo"
scope="$work/repo/scope"
audit="$scope/audits/ex-e8-2026.yaml"
config="$root/examples/example-essential-eight.yaml"
plan="$work/plan.json"
dotnet build -c Release --nologo -v q "$root/src/EvidenceHound.Cli" >/dev/null

# hound ARGS... prints the command, keeps stdout and stderr in $work, and leaves the exit code in $code
hound() {
  printf '$ hound %s\n' "${*//$work/\$WORK}"
  code=0
  dotnet run --no-build -c Release --project "$root/src/EvidenceHound.Cli" -- "$@" >"$work/out" 2>"$work/err" || code=$?
  printf 'exit %s\n' "$code"
}

expect() {
  [[ "$code" == "$1" ]] || { echo "check-scope-plans: expected exit $1, got $code" >&2; grep -v '^info:' "$work/err" >&2; exit 1; }
}

# ca METHOD PATH [JSON] calls the CISO Assistant API with the token from a file, never on the command line
printf 'Authorization: Token %s\n' "$HOUND_CA_TOKEN" >"$work/auth"
chmod 600 "$work/auth"
ca() {
  curl -fsS -X "$1" -H @"$work/auth" -H 'Content-Type: application/json' ${3:+--data "$3"} "${HOUND_CA_URL%/}/${2#/}"
}

echo "== s08-06: a gain and a regression =="
python3 - "$audit" <<'PY'
import sys
path = sys.argv[1]
text = open(path).read()
text = text.replace("    result: compliant\n    observation: Java", "    result: non_compliant\n    observation: Java", 1)
text = text.replace("unlisted:", "  - ref: ISM-1485\n    status: done\n    result: compliant\n    observation: Checked at the trial.\nunlisted:", 1)
open(path, "w").write(text)
PY
hound scope plan "$scope" --config "$config"
expect 0
sed -n '/^### Changes to compliance/,/^### /p' "$work/out" | sed '$d'

echo "== s08-07: detailed exit code, a stale saved plan, a fresh one =="
hound scope plan "$scope" --config "$config" --detailed-exitcode --out "$plan"
expect 2
grep -m1 'to create' "$work/out"

# Someone edits a row the plan writes, in CISO Assistant, between the plan and the apply.
row="$(python3 -c '
import json, sys
plan = json.load(open(sys.argv[1]))
print(next(k for k, v in plan["read"].items() if k.startswith("assessment/EX-E8-2026/ISM-1486") and v))' "$plan")"
ra_id="$(ca GET "requirement-assessments/?compliance_assessment__ref_id=EX-E8-2026&limit=500" | python3 -c '
import json, sys
rows = json.load(sys.stdin)["results"]
print(next(r["id"] for r in rows if (r.get("requirement") or {}).get("ref_id") == "ISM-1486"))')"
ca PATCH "requirement-assessments/$ra_id/" '{"observation": "Edited in the UI after the plan."}' >/dev/null
echo "edited $row in CISO Assistant"
hound scope apply "$plan"
expect 3
grep -o 'is stale: [^[]*\[scope.plan_stale\]' "$work/err" | sed "s|$work|\$WORK|g"

hound scope plan "$scope" --config "$config" --out "$plan"
expect 0
hound scope apply "$plan"
expect 0
python3 -c 'import json,sys; r=json.load(open(sys.argv[1])); print({k: r[k] for k in ("written", "failed", "skipped")})' "$work/out"

echo "== Put the example scope back =="
"$root/scripts/ca-apply-scope.sh"
echo "check-scope-plans: ok"
