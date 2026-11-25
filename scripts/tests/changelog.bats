#!/usr/bin/env bats
# s13-03: CHANGELOG.md from Conventional Commits. Features and fixes are listed by sprint task; tests, docs, chores and
# refactors are not, nor are TDD markers such as [red] and [green]. Needs git-cliff (tools/changelog/requirements.txt).

setup() {
  root="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
  repo="$(mktemp -d)"
  git -C "$repo" init -q -b main
  git -C "$repo" config user.email dev@example.com
  git -C "$repo" config user.name "Example Dev"
  commit() { git -C "$repo" commit -q --allow-empty -m "$1"; }
  commit "chore(s01): start sprint"
  commit "test(s01-02): mapping engine [red]"
  commit "feat(s01-02): map check results to requirements [green]"
  commit "fix(s01-03): keep the evidence hash stable"
  commit "docs(s01-04): explain mapping files"
  commit "refactor(s01-02): smaller functions"
}

teardown() {
  rm -rf "$repo"
}

@test "unreleased commits become the named version, with features and fixes only" {
  run "$root/scripts/changelog.sh" 1.0.0 "$repo"
  [ "$status" -eq 0 ]
  changelog="$(cat "$repo/CHANGELOG.md")"
  [[ "$changelog" == *"## 1.0.0"* ]]
  [[ "$changelog" == *"### Features"* ]]
  [[ "$changelog" == *"- map check results to requirements (s01-02)"* ]]
  [[ "$changelog" == *"### Fixes"* ]]
  [[ "$changelog" == *"- keep the evidence hash stable (s01-03)"* ]]
  [[ "$changelog" != *"[green]"* ]]
  [[ "$changelog" != *"explain mapping files"* ]]
  [[ "$changelog" != *"smaller functions"* ]]
  [[ "$changelog" != *"start sprint"* ]]
}

@test "the release notes are the latest version's section only" {
  "$root/scripts/changelog.sh" 1.0.0 "$repo"
  git -C "$repo" add CHANGELOG.md
  git -C "$repo" commit -q -m "chore(release): 1.0.0"
  git -C "$repo" tag -a v1.0.0 -m "Release 1"
  git -C "$repo" commit -q --allow-empty -m "feat(s14-01): essentials mapping"
  run "$root/scripts/changelog.sh" --notes v1.0.0 "$repo"
  [ "$status" -eq 0 ]
  [[ "$output" == *"map check results to requirements"* ]]
  [[ "$output" != *"essentials mapping"* ]]
}

@test "the changelog is lint-clean Markdown: spaced headings, single blank lines, addresses as code" {
  git -C "$repo" commit -q --allow-empty -m "feat(s01-05): remind owner@example.com before the due date"
  "$root/scripts/changelog.sh" 1.0.0 "$repo"
  changelog="$repo/CHANGELOG.md"
  grep -B1 '^## 1.0.0' "$changelog" | head -1 | grep -q '^$'
  ! awk 'prev == "" && $0 == "" && NR > 1 { found = 1 } { prev = $0 } END { exit !found }' "$changelog"
  grep -q 'remind `owner@example.com` before' "$changelog"
}
