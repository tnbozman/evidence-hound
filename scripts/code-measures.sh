#!/usr/bin/env bash
# Measures of the code for the engineering review (s19-03): tracked C# lines per project, production against tests, and the
# largest files. Reads only what git tracks, so build output never counts.
#
# Usage: scripts/code-measures.sh [repo] (default: this repository)
set -euo pipefail

repo="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
files="$(git -C "$repo" ls-files -- '*.cs')"
[[ -n "$files" ]] || { echo "code-measures: no tracked C# files in $repo" >&2; exit 1; }

lines() { (cd "$repo" && xargs -d '\n' cat) | wc -l; }

# A project is src/<name>, src/Collectors/<name> or tests/<name>.
project() { awk -F/ '{ if ($2 == "Collectors") print $1"/"$2"/"$3; else print $1"/"$2 }'; }

echo "Lines per project:"
while read -r p; do
  in_project="$(grep "^$p/" <<<"$files")"
  echo "  $p $(lines <<<"$in_project") lines in $(wc -l <<<"$in_project") files"
done < <(project <<<"$files" | sort -u)

echo "Production $(grep '^src/' <<<"$files" | lines), tests $(grep '^tests/' <<<"$files" | lines)"

echo "Largest files:"
(cd "$repo" && xargs -d '\n' wc -l <<<"$files") | grep -v ' total$' | sort -rn | head -15 | awk '{ print "  "$1" "$2 }'
