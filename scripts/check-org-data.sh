#!/usr/bin/env bash
# Fail if any organisation-specific term appears in tracked files or in the
# commit messages being pushed. Terms come from ORG_DENYLIST (newline or comma
# separated), stored as an encrypted Actions secret and never committed.
# Matching is case-insensitive. Matched terms are never printed, only where.
#
# Usage: ORG_DENYLIST='...' scripts/check-org-data.sh [git-range-for-messages]
#        ORG_DENYLIST='...' scripts/check-org-data.sh --history
#   --history scans every commit on every branch and tag: each commit's changes (so a term that was added and later
#   removed is still found) and its message. It names the commits, never the terms.
set -euo pipefail

if [[ -z "${ORG_DENYLIST:-}" ]]; then
  echo "check-org-data: ORG_DENYLIST is empty; refusing to pass without a deny-list." >&2
  exit 2
fi

patterns="$(mktemp)"
trap 'rm -f "$patterns"' EXIT
printf '%s\n' "$ORG_DENYLIST" | tr ',' '\n' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//' | grep -v '^$' > "$patterns"

found=0

if [[ "${1:-}" == "--history" ]]; then
  while IFS= read -r sha; do
    if git log -1 --format=%B "$sha" | grep -qiF -f "$patterns"; then
      echo "::error::organisation-specific term in commit message ${sha:0:12}"
      found=1
    fi
    if git show --format= --no-color --no-ext-diff --text "$sha" | grep -qiF -f "$patterns"; then
      echo "::error::organisation-specific term in the changes of commit ${sha:0:12}"
      found=1
    fi
  done < <(git rev-list --all)
  if [[ $found -ne 0 ]]; then
    echo "check-org-data: the history holds organisation-specific content; rewrite it before the repository is published further." >&2
    exit 1
  fi
  echo "check-org-data: history clean ($(git rev-list --all | wc -l | tr -d ' ') commits)"
  exit 0
fi

# Tracked files (binary files skipped).
while IFS= read -r hit; do
  echo "::error file=${hit%%:*},line=${hit#*:}::organisation-specific term found"
  found=1
done < <(git ls-files -z | xargs -0 grep -nIiF -f "$patterns" -- 2>/dev/null | cut -d: -f1,2 || true)

# Commit messages in the given range (default: commits not on origin/main).
range="${1:-origin/main..HEAD}"
if git rev-parse --verify --quiet "${range%%..*}" >/dev/null 2>&1; then
  while IFS= read -r sha; do
    if git log -1 --format=%B "$sha" | grep -qiF -f "$patterns"; then
      echo "::error::organisation-specific term in commit message ${sha:0:12}"
      found=1
    fi
  done < <(git rev-list "$range")
fi

if [[ $found -ne 0 ]]; then
  echo "check-org-data: remove the flagged content (and rewrite the branch if it was committed)." >&2
  exit 1
fi
echo "check-org-data: clean"
