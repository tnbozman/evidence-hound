#!/usr/bin/env bats
# s13-04: the release workflow. Static checks of .github/workflows/release.yml; its dry run runs in CI on pull requests
# that change it, packing version 1.0.0-dry-run into a local test feed and installing hound from there.

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
  grep -q 'scripts/changelog.sh" --notes' "$root/scripts/release-build.sh"
  grep -q 'artifacts/notes.md' "$wf"
  grep -q 'gh release create' "$wf"
}

@test "the dry run packs 1.0.0-dry-run into a test feed and installs hound from it" {
  # A 0.x version would fail the profiles' "requires hound >=0.1" check in the unit tests and the image.
  grep -q 'version="1.0.0-dry-run"' "$wf"
  ! grep -q '0.0.0-test' "$wf"
  grep -q 'scripts/release-build.sh' "$wf"
  grep -q 'dotnet tool install' "$root/scripts/release-build.sh"
  grep -q -- '--add-source "$out/packages"' "$root/scripts/release-build.sh"
}

@test "the only publishing secret is the optional NuGet key; the deny-list is the only other secret" {
  secrets="$(grep -o 'secrets\.[A-Z_]*' "$wf" | sort -u | tr '\n' ' ')"
  [ "$secrets" = "secrets.GITHUB_TOKEN secrets.NUGET_API_KEY secrets.ORG_DENYLIST " ]
}

@test "the documentation site is built strictly and published to GitHub Pages with the release" {
  publish="$(sed -n '/^  publish:/,/^  [a-z-]*:$/p' "$wf")"
  grep -q 'make docs' <<<"$publish"
  grep -q 'mkdocs gh-deploy' <<<"$publish"
}

@test "a release is checked for organisation data across the full history before anything is built" {
  verify="$(sed -n '/^  verify:/,/^  publish:$/p' "$wf")"
  grep -q 'scripts/check-org-data.sh --history' <<<"$verify"
  grep -q 'ORG_DENYLIST: ${{ secrets.ORG_DENYLIST }}' <<<"$verify"
}
