#!/usr/bin/env bash
# CHANGELOG.md and release notes from Conventional Commits, with git-cliff and cliff.toml.
#
# Usage: scripts/changelog.sh <version> [repo]          rewrite CHANGELOG.md, naming unreleased commits <version> (1.0.0)
#        scripts/changelog.sh --notes <tag> [repo]      print the release notes of <tag> (v1.0.0) only
# Needs git-cliff: pip install --require-hashes -r tools/changelog/requirements.txt
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
die() { echo "changelog: $*" >&2; exit 2; }
command -v git-cliff >/dev/null || die "git-cliff is not installed (pip install --require-hashes -r tools/changelog/requirements.txt)"

if [[ "${1:-}" == "--notes" ]]; then
  tag="${2:?usage: changelog.sh --notes <tag> [repo]}"
  repo="${3:-$root}"
  git -C "$repo" rev-parse -q --verify "refs/tags/$tag" >/dev/null || die "no tag $tag"
  # The whole changelog up to the tag, cut down to the tag's own section.
  git-cliff --config "$root/cliff.toml" --repository "$repo" --strip all |
    awk -v heading="## ${tag#v} " 'index($0, heading) == 1 { keep = 1; next } /^## / { keep = 0 } keep'
  exit 0
fi

version="${1:?usage: changelog.sh <version> [repo]}"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || die "the version is semantic, without a v: 1.0.0"
repo="${2:-$root}"
git-cliff --config "$root/cliff.toml" --repository "$repo" --tag "v$version" --output "$repo/CHANGELOG.md"
# git-cliff ends the file with blank lines; Markdown lint wants one final newline.
printf '%s\n' "$(cat "$repo/CHANGELOG.md")" >"$repo/CHANGELOG.md.tmp" && mv "$repo/CHANGELOG.md.tmp" "$repo/CHANGELOG.md"
echo "changelog: wrote $repo/CHANGELOG.md for $version"
