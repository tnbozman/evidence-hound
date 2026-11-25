#!/usr/bin/env bats
# s13-02: the docs' relative links and section anchors resolve. External links are not fetched.

setup() {
  root="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
  check="$root/scripts/check-links.py"
  fixtures="$BATS_TEST_DIRNAME/fixtures"
}

@test "links to files, folders, sections and images that exist pass" {
  run python3 -I "$check" "$fixtures/good"
  [ "$status" -eq 0 ]
}

@test "a missing file and a missing section are each reported with file and line" {
  run python3 -I "$check" "$fixtures/bad"
  [ "$status" -eq 1 ]
  [[ "$output" == *"index.md:3: missing.md: no such file"* ]]
  [[ "$output" == *"index.md:5: #not-here: no such section"* ]]
}

@test "the repository's published docs have no broken links" {
  run python3 -I "$check" "$root"
  echo "$output"
  [ "$status" -eq 0 ]
}
