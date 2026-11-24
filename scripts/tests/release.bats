#!/usr/bin/env bats
# s13-04: the release workflow. Static checks of .github/workflows/release.yml; its dry run runs in CI on pull requests
# that change it, packing version 0.0.0-test into a local test feed and installing hound from there.

setup() {
  root="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
  wf="$root/.github/workflows/release.yml"
}

@test "releases run from v* tags, and as a dry run on pull requests that change the release" {
  grep -Eq '^  push:' "$wf"
  grep -Eq '^    tags: \["v\*"\]' "$wf"
  grep -Eq '^  pull_request:' "$wf"
}

@test "the workflow reads by default; only the publish job may write, and only after approval in the release environment" {
  grep -Eq '^permissions:\s*$' "$wf"
  grep -A1 -E '^permissions:\s*$' "$wf" | grep -q 'contents: read'
  publish="$(sed -n '/^  publish:/,/^  [a-z-]*:$/p' "$wf")"
  grep -q 'environment: release' <<<"$publish"
  grep -q 'contents: write' <<<"$publish"
  grep -q 'packages: write' <<<"$publish"
  grep -q 'id-token: write' <<<"$publish"
  grep -q 'attestations: write' <<<"$publish"
  [ "$(grep -c 'contents: write' "$wf")" -eq 1 ]
}

@test "publishing never runs for a dry run" {
  sed -n '/^  publish:/,/^  [a-z-]*:$/p' "$wf" | grep -q "if: startsWith(github.ref, 'refs/tags/v')"
}

@test "every action is pinned to a full commit SHA" {
  run grep -En 'uses: [^ ]+@' "$wf"
  [ "$status" -eq 0 ]
  ! grep -En 'uses: [^ ]+@' "$wf" | grep -Ev 'uses: [^ ]+@[0-9a-f]{40}( |$)'
}

@test "the release has an SBOM, provenance for packages and the image, and notes from the changelog" {
  grep -q 'CycloneDX' "$wf"
  [ "$(grep -c 'actions/attest-build-provenance@' "$wf")" -ge 2 ]
  grep -q 'scripts/changelog.sh --notes' "$wf"
  grep -q 'gh release create' "$wf"
}

@test "the dry run packs 0.0.0-test into a test feed and installs hound from it" {
  grep -q '0.0.0-test' "$wf"
  grep -q 'dotnet tool install' "$wf"
  grep -q -- '--add-source' "$wf"
}

@test "the only secret is the optional NuGet key; no long-lived token is needed otherwise" {
  secrets="$(grep -o 'secrets\.[A-Z_]*' "$wf" | sort -u | tr '\n' ' ')"
  [ "$secrets" = "secrets.GITHUB_TOKEN secrets.NUGET_API_KEY " ]
}
