#!/usr/bin/env bash
# s13-04: builds what a release publishes, into one folder, and checks it: every NuGet package with its metadata
# (check-packages.sh), the hound tool installed from those packages as a local test feed, a CycloneDX SBOM of the
# CLI, and the release notes from CHANGELOG-style Conventional Commits. The release workflow runs it before approval
# (and, on pull requests, as a dry run on a local v1.0.0-dry-run tag) and again to publish.
#
# Usage: scripts/release-build.sh <version> <out>     for example: scripts/release-build.sh 1.0.0 artifacts
# The tag v<version> must exist in this checkout (the workflow's dry run makes a local one). Needs the .NET SDK,
# git-cliff (tools/changelog/requirements.txt) and unzip.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="${1:?usage: release-build.sh <version> <out>}"
out="${2:?usage: release-build.sh <version> <out>}"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || { echo "release-build: '$version' is not a semantic version" >&2; exit 2; }
mkdir -p "$out/packages" "$out/sbom"

echo "release-build: packages $version"
MinVerVersionOverride="$version" "$root/scripts/check-packages.sh" "$out/packages"
compgen -G "$out/packages/EvidenceHound.Cli.$version.nupkg" >/dev/null \
  || { echo "release-build: the packages are not version $version" >&2; exit 1; }

echo "release-build: installing hound $version from the packages as a test feed"
tools="$(mktemp -d)"
trap 'rm -rf "$tools"' EXIT
dotnet tool install EvidenceHound.Cli --version "$version" --tool-path "$tools" --add-source "$out/packages" \
  --ignore-failed-sources >/dev/null
installed="$("$tools/hound" --version)"
[[ "$installed" == "$version"* ]] || { echo "release-build: installed hound reports '$installed', not $version" >&2; exit 1; }
echo "release-build: hound --version: $installed"

echo "release-build: SBOM (CycloneDX)"
(cd "$root" && dotnet tool restore >/dev/null)
(cd "$root" && dotnet tool run dotnet-CycloneDX src/EvidenceHound.Cli/EvidenceHound.Cli.csproj \
  --output "$out/sbom" --filename "evidence-hound-$version.cdx.json" --output-format Json --set-version "$version" >/dev/null)

echo "release-build: release notes"
"$root/scripts/changelog.sh" --notes "v$version" "$root" >"$out/notes.md"
[[ -s "$out/notes.md" ]] || { echo "release-build: no release notes for v$version" >&2; exit 1; }
echo "release-build: ok"
