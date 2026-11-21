#!/usr/bin/env bash
# Start a sprint: create sprint/<sNN-slug> from main, mark the sprint file
# "In progress", push, and open a draft PR whose body is the task checklist.
#
# Usage: scripts/start-sprint.sh sNN
# Needs: git, gh (authenticated). Used locally and by .github/workflows/start-sprint.yml.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/lib/sprint.sh
source "$here/lib/sprint.sh"

die() { echo "start-sprint: $*" >&2; exit 1; }

id="${1:-}"
sprint_valid_id "$id" || die "usage: start-sprint.sh sNN (for example s01)"

git fetch --quiet origin main
git switch --quiet --detach origin/main

file="$(sprint_file "$id")" || die "no single docs/sprints/${id}-*.md file on main"
slug="$(sprint_slug "$file")"
title="$(sprint_title "$file")"
branch="sprint/$slug"

[[ "$(sprint_status "$file")" == "Planned" ]] \
  || die "$file is not 'Status: Planned' (it is '$(sprint_status "$file")')"

in_progress="$(sprint_others_in_progress "$id")"
[[ -z "$in_progress" ]] || die "another sprint is already in progress on main: $in_progress"

open_sprints="$(gh pr list --state open --json headRefName --jq '.[].headRefName' | grep '^sprint/' || true)"
[[ -z "$open_sprints" ]] || die "another sprint is open: $open_sprints. Finish or close it first."

if git ls-remote --exit-code --heads origin "$branch" >/dev/null 2>&1; then
  die "branch $branch already exists"
fi

git switch --quiet -c "$branch"
sprint_set_status "$file" "In progress"
git add "$file"
git commit --quiet -m "chore($id): start sprint"
git push --quiet -u origin "$branch"

body="$(mktemp)"
trap 'rm -f "$body"' EXIT
sprint_pr_body "$file" > "$body"

gh pr create --draft --base main --head "$branch" \
  --title "Sprint $id: $title" --body-file "$body"

echo "Started $branch"
