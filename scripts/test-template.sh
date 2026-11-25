#!/usr/bin/env bash
# Checks the collector template: packs the collector SDK and the template pack (EvidenceHound.Templates) into a local
# feed, installs the template pack from it into a throwaway template store, generates a collector with the template's
# defaults into a temp folder, then builds and tests it unmodified.
#
# Usage: scripts/test-template.sh
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# A unique version, so packages restored by an earlier run are never reused from the NuGet cache.
version="0.1.0-template.$(date +%s)"
feed="$work/feed"
hive="$work/hive"

echo "test-template: packing the collector SDK $version"
# The version is set with MinVerVersionOverride: MinVer otherwise sets it from git tags.
for project in src/EvidenceHound.Core src/EvidenceHound.Collectors.Abstractions src/EvidenceHound.Collectors.Testing templates; do
  dotnet pack "$root/$project" -c Release -o "$feed" -p:MinVerVersionOverride="$version" --nologo -v quiet
done

echo "test-template: installing the template pack from the local feed, and generating Example.Widget"
dotnet new install "$feed/EvidenceHound.Templates.$version.nupkg" --debug:custom-hive "$hive" >/dev/null
# No --HoundVersion: the template pack defaults to the SDK packages of its own version.
(cd "$work" && dotnet new hound-collector -n Example.Widget --debug:custom-hive "$hive")

echo "test-template: building and testing the generated collector"
cd "$work/Example.Widget"
# One explicit configuration for build and test: MSBuild reads a CONFIGURATION environment variable (CI sets it), but
# dotnet test --no-build does not, so without -c the test step looks for binaries the build did not produce.
dotnet restore --source "$feed" --source https://api.nuget.org/v3/index.json
dotnet build --no-restore --nologo -c Release
dotnet test --no-build -c Release
echo "test-template: ok"
