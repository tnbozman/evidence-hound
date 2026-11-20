#!/usr/bin/env bats
# Tests for scripts/lib/sprint.sh. Run: bats scripts/tests

setup() {
  export SPRINTS_DIR="$BATS_TEST_DIRNAME/fixtures"
  # shellcheck source=../lib/sprint.sh
  source "$BATS_TEST_DIRNAME/../lib/sprint.sh"
}

@test "valid id accepts s07 and rejects s7 and x07" {
  sprint_valid_id s07
  ! sprint_valid_id s7
  ! sprint_valid_id x07
}

@test "finds the single file for a sprint id" {
  run sprint_file s98
  [ "$status" -eq 0 ]
  [ "$output" = "$SPRINTS_DIR/s98-example-sprint.md" ]
}

@test "missing sprint returns non-zero" {
  run sprint_file s42
  [ "$status" -ne 0 ]
}

@test "extracts slug, title and status" {
  f="$SPRINTS_DIR/s98-example-sprint.md"
  [ "$(sprint_slug "$f")" = "s98-example-sprint" ]
  [ "$(sprint_title "$f")" = "Example sprint" ]
  [ "$(sprint_status "$f")" = "Planned" ]
}

@test "lists every task heading as an unchecked item" {
  run sprint_tasks "$SPRINTS_DIR/s98-example-sprint.md"
  [ "${lines[0]}" = "- [ ] s98-01 First task" ]
  [ "${lines[1]}" = "- [ ] s98-02 Second task" ]
  [ "${#lines[@]}" -eq 2 ]
}

@test "sets status in place" {
  tmp="$BATS_TEST_TMPDIR/s97-copy.md"
  cp "$SPRINTS_DIR/s98-example-sprint.md" "$tmp"
  sprint_set_status "$tmp" "In progress"
  [ "$(sprint_status "$tmp")" = "In progress" ]
}
