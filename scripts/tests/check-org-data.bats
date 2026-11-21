#!/usr/bin/env bats
# Tests for scripts/check-org-data.sh, run in a throwaway git repository.
# "acmewidgets" is a made-up stand-in for a denylisted organisation term.

setup() {
  script="$BATS_TEST_DIRNAME/../check-org-data.sh"
  repo="$BATS_TEST_TMPDIR/repo"
  git init -q -b main "$repo"
  cd "$repo"
  git config user.name "Test"
  git config user.email "test@example.com"
  echo "Example Pty Ltd" > readme.txt
  git add readme.txt
  git commit -q -m "initial"
  base="$(git rev-parse HEAD)"
}

@test "refuses to pass when the deny-list is empty" {
  ORG_DENYLIST="" run "$script" "$base..HEAD"
  [ "$status" -eq 2 ]
}

@test "passes a clean tree" {
  ORG_DENYLIST="acmewidgets" run "$script" "$base..HEAD"
  [ "$status" -eq 0 ]
  [[ "$output" == *"clean"* ]]
}

@test "fails on a term in a tracked file, case-insensitively, without printing the term" {
  echo "Hosted at AcmeWidgets" > notes.txt
  git add notes.txt
  git commit -q -m "add notes"
  ORG_DENYLIST="acmewidgets" run "$script" "$base..HEAD"
  [ "$status" -eq 1 ]
  [[ "$output" == *"notes.txt"* ]]
  [[ "$output" != *"AcmeWidgets"* ]]
  [[ "$output" != *"acmewidgets"* ]]
}

@test "fails on a term in a commit message in the range" {
  echo "more" >> readme.txt
  git commit -q -am "fix for acmewidgets"
  ORG_DENYLIST="acmewidgets" run "$script" "$base..HEAD"
  [ "$status" -eq 1 ]
  [[ "$output" == *"commit message"* ]]
}

@test "accepts comma and newline separated terms with whitespace" {
  echo "sample text: otherco" > notes.txt
  git add notes.txt
  git commit -q -m "add notes"
  ORG_DENYLIST=$' first , otherco \n' run "$script" "$base..HEAD"
  [ "$status" -eq 1 ]
}
