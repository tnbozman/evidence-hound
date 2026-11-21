#!/usr/bin/env bats
# Tests for scripts/start-sprint.sh. Each test runs against a throwaway
# "origin" repository and a stub `gh` that records its arguments.

setup() {
  scripts_dir="$(cd "$BATS_TEST_DIRNAME/.." && pwd)"
  fixtures="$BATS_TEST_DIRNAME/fixtures"
  # shellcheck source=../lib/sprint.sh
  source "$scripts_dir/lib/sprint.sh"

  origin="$BATS_TEST_TMPDIR/origin.git"
  work="$BATS_TEST_TMPDIR/work"
  git init -q --bare -b main "$origin"
  git init -q -b main "$work"
  cd "$work"
  git config user.name "Test"
  git config user.email "test@example.com"
  mkdir -p docs/sprints
  cp "$fixtures/s98-example-sprint.md" docs/sprints/
  git add docs
  git commit -q -m "initial"
  git remote add origin "$origin"
  git push -q -u origin main

  # Stub gh: no open PRs unless GH_OPEN_HEADS is set; record pr create calls.
  bin="$BATS_TEST_TMPDIR/bin"
  mkdir -p "$bin"
  cat > "$bin/gh" <<'STUB'
#!/usr/bin/env bash
echo "$*" >> "$GH_LOG"
if [[ "$1 $2" == "pr list" ]]; then printf '%s\n' ${GH_OPEN_HEADS:-}; fi
if [[ "$1 $2" == "pr create" ]]; then
  while [[ $# -gt 0 ]]; do [[ "$1" == "--body-file" ]] && cp "$2" "$GH_BODY"; shift; done
fi
STUB
  chmod +x "$bin/gh"
  export PATH="$bin:$PATH" GH_LOG="$BATS_TEST_TMPDIR/gh.log" GH_BODY="$BATS_TEST_TMPDIR/body.md"
}

commit_sprint() {
  printf '%s\n' "$2" > "docs/sprints/$1"
  git add docs
  git commit -q -m "add $1"
  git push -q origin main
}

@test "PR body has the plan path and every task as an unchecked box" {
  run sprint_pr_body "$fixtures/s98-example-sprint.md"
  [ "$status" -eq 0 ]
  [[ "$output" == *"s98-example-sprint.md"* ]]
  [[ "$output" == *"- [ ] s98-01 First task"* ]]
  [[ "$output" == *"- [ ] s98-02 Second task"* ]]
}

@test "lists other sprints that are in progress" {
  commit_sprint s97-running.md $'# s97 Running\n\nStatus: In progress'
  SPRINTS_DIR=docs/sprints run sprint_others_in_progress s98
  [ "$status" -eq 0 ]
  [ "$output" = "docs/sprints/s97-running.md" ]
}

@test "starts a sprint: branch, status, commit, push and draft PR" {
  run "$scripts_dir/start-sprint.sh" s98
  [ "$status" -eq 0 ]
  git ls-remote --exit-code --heads origin sprint/s98-example-sprint
  [ "$(git show origin/sprint/s98-example-sprint:docs/sprints/s98-example-sprint.md | sed -n 's/^Status: //p')" = "In progress" ]
  grep -q -- "pr create --draft --base main --head sprint/s98-example-sprint --title Sprint s98: Example sprint" "$GH_LOG"
  grep -q -- "- \[ \] s98-02 Second task" "$GH_BODY"
}

@test "rejects a missing sprint" {
  run "$scripts_dir/start-sprint.sh" s42
  [ "$status" -ne 0 ]
  [[ "$output" == *"no single docs/sprints/s42-*.md"* ]]
}

@test "rejects an invalid sprint id" {
  run "$scripts_dir/start-sprint.sh" sprint1
  [ "$status" -ne 0 ]
  [[ "$output" == *"usage"* ]]
}

@test "rejects when another sprint file is in progress" {
  commit_sprint s97-running.md $'# s97 Running\n\nStatus: In progress'
  run "$scripts_dir/start-sprint.sh" s98
  [ "$status" -ne 0 ]
  [[ "$output" == *"already in progress"*"s97-running.md"* ]]
}

@test "rejects when another sprint PR is open" {
  GH_OPEN_HEADS="sprint/s97-running" run "$scripts_dir/start-sprint.sh" s98
  [ "$status" -ne 0 ]
  [[ "$output" == *"another sprint is open"* ]]
}

@test "rejects when the branch already exists" {
  git push -q origin main:sprint/s98-example-sprint
  run "$scripts_dir/start-sprint.sh" s98
  [ "$status" -ne 0 ]
  [[ "$output" == *"already exists"* ]]
}
