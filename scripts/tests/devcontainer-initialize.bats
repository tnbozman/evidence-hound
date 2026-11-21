#!/usr/bin/env bats
# Tests for .devcontainer/initialize.sh, which runs on the host before the dev container starts.
# Runs against a copy of the scripts so the real .dev/ folder is untouched.

setup() {
  repo="$BATS_TEST_TMPDIR/repo"
  mkdir -p "$repo/.devcontainer" "$repo/scripts"
  cp "$BATS_TEST_DIRNAME/../../.devcontainer/initialize.sh" "$repo/.devcontainer/" 2>/dev/null || true
  cp "$BATS_TEST_DIRNAME/../ca-secret-key.sh" "$repo/scripts/"
}

@test "writes the generated secret key to .devcontainer/.env for compose" {
  run bash "$repo/.devcontainer/initialize.sh"
  [ "$status" -eq 0 ]
  [ "$(cat "$repo/.devcontainer/.env")" = "HOUND_CA_SECRET_KEY=$(cat "$repo/.dev/ca-secret-key")" ]
  [ "$(stat -c %a "$repo/.devcontainer/.env")" = "600" ]
  [[ "$output" != *"$(cat "$repo/.dev/ca-secret-key")"* ]]
}

@test "keeps the same key on every run" {
  bash "$repo/.devcontainer/initialize.sh"
  first="$(cat "$repo/.devcontainer/.env")"
  bash "$repo/.devcontainer/initialize.sh"
  [ "$(cat "$repo/.devcontainer/.env")" = "$first" ]
}
