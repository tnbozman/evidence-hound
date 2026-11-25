#!/usr/bin/env bash
# s13-03: packs every NuGet package hound publishes and fails when one is missing or lacks its metadata: licence
# expression, repository URL and commit, project URL, README, description, and a symbols package (.snupkg) for
# each library. CI runs it on every pull request; the release workflow publishes what it packs.
#
# Usage: scripts/check-packages.sh [output-folder]   (make test-packages; default: a temporary folder)
# Environment: MinVerVersionOverride to pack a given version, such as 1.0.0-dry-run.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="${1:-$(mktemp -d)}"
mkdir -p "$out"

libraries=(
  EvidenceHound.Core
  EvidenceHound.Collectors.Abstractions
  EvidenceHound.Collectors.Testing
  EvidenceHound.Collectors.M365
  EvidenceHound.Collectors.Manual
  EvidenceHound.Collectors.Rest
  EvidenceHound.Collectors.VulnScan
)
others=(EvidenceHound.Templates EvidenceHound.Cli)

echo "check-packages: packing into $out"
# Restore once in locked mode, then pack without restoring, so packing never rewrites a lock file.
dotnet restore "$root/EvidenceHound.slnx" --locked-mode --nologo -v quiet
# MinVerVersionOverride (an MSBuild property, read from the environment) sets the version instead of the git tags, as for a dry run.
dotnet pack "$root/EvidenceHound.slnx" --no-restore -c "${CONFIGURATION:-Release}" -o "$out" --nologo -v quiet

failures=0
fail() { echo "check-packages: $*" >&2; failures=$((failures + 1)); }

nuspec_of() { unzip -p "$1" '*.nuspec'; }

for id in "${libraries[@]}" "${others[@]}"; do
  shopt -s nullglob
  found=("$out/$id".[0-9]*.nupkg)
  shopt -u nullglob
  [[ ${#found[@]} -eq 1 ]] || { fail "$id: expected one package, found ${#found[@]}"; continue; }
  package="${found[0]}"
  nuspec="$(nuspec_of "$package")"
  grep -q '<license type="expression">Apache-2.0</license>' <<<"$nuspec" || fail "$id: no Apache-2.0 licence expression"
  grep -Eq '<repository type="git" url="https://github.com/tnbozman/evidence-hound(\.git)?"( branch="[^"]*")? commit="[0-9a-f]{40}"' <<<"$nuspec" \
    || fail "$id: no repository URL and commit (SourceLink)"
  grep -q '<projectUrl>https://' <<<"$nuspec" || fail "$id: no project URL"
  grep -q '<description>[^<]' <<<"$nuspec" || fail "$id: no description"
  readme="$(sed -n 's|.*<readme>\(.*\)</readme>.*|\1|p' <<<"$nuspec")"
  if [[ -z "$readme" ]]; then
    fail "$id: no README"
  elif ! unzip -l "$package" "$readme" >/dev/null 2>&1; then
    fail "$id: README $readme is not in the package"
  fi
done

for id in "${libraries[@]}"; do
  compgen -G "$out/$id.[0-9]*.snupkg" >/dev/null || fail "$id: no symbols package (.snupkg)"
done

grep -q '<packageType name="DotnetTool"' <<<"$(nuspec_of "$(compgen -G "$out/EvidenceHound.Cli.[0-9]*.nupkg" | head -n 1)" 2>/dev/null || true)" \
  || fail "EvidenceHound.Cli: not a .NET tool package"
grep -q '<packageType name="Template"' <<<"$(nuspec_of "$(compgen -G "$out/EvidenceHound.Templates.[0-9]*.nupkg" | head -n 1)" 2>/dev/null || true)" \
  || fail "EvidenceHound.Templates: not a template package"

# Nothing else: internal libraries (the runtime, the CISO Assistant client, onboarding) ship inside the CLI tool only.
expected=" ${libraries[*]} ${others[*]} "
for package in "$out"/*.nupkg; do
  id="$(basename "$package" | sed -E 's/\.[0-9]+\.[0-9]+\.[0-9]+.*\.nupkg$//')"
  [[ "$expected" == *" $id "* ]] || fail "$id: packed, but it is not a published package (set IsPackable to false)"
done

[[ $failures -eq 0 ]] || { echo "check-packages: $failures problems" >&2; exit 1; }
echo "check-packages: ok ($(find "$out" -name '*.nupkg' | wc -l) packages)"
