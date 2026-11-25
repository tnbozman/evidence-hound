#!/usr/bin/env bats
# s14-04 (from s13's discovered work): one command moves every deployment pin to a release, instead of editing four files by hand.

setup() {
  root="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
  copy="$(mktemp -d)"
  for file in deploy/compose/.env.example examples/compliance-repo/deploy/deploy.env \
    examples/compliance-repo/.github/workflows/compliance.yml docs/tutorials/first-deployment.md; do
    mkdir -p "$copy/$(dirname "$file")"
    cp "$root/$file" "$copy/$file"
  done
}

teardown() {
  rm -rf "$copy"
}

@test "moves the runner image, the workflow ref and the tutorial's image to the version" {
  run "$root/scripts/stamp-release.sh" 1.2.3 "$copy"
  [ "$status" -eq 0 ]
  grep -qx 'HOUND_RUNNER_IMAGE=ghcr.io/tnbozman/evidence-hound:1.2.3' "$copy/deploy/compose/.env.example"
  grep -qx 'HOUND_RUNNER_IMAGE=ghcr.io/tnbozman/evidence-hound:1.2.3' "$copy/examples/compliance-repo/deploy/deploy.env"
  grep -q '^  EVIDENCE_HOUND_REF: v1.2.3$' "$copy/examples/compliance-repo/.github/workflows/compliance.yml"
  grep -q 'ghcr.io/tnbozman/evidence-hound:1.2.3' "$copy/docs/tutorials/first-deployment.md"
  ! grep -q 'evidence-hound:1\.0\.0' "$copy/docs/tutorials/first-deployment.md"
  [[ "$output" == *"4 files"* ]]
}

@test "refuses a version that is not x.y.z" {
  run "$root/scripts/stamp-release.sh" v1.2 "$copy"
  [ "$status" -eq 2 ]
  [[ "$output" == *"x.y.z"* ]]
}
