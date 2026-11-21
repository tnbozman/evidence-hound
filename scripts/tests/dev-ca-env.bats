#!/usr/bin/env bats
# Tests for scripts/dev-ca-env.sh, which copies the env file written by the ca-seed service
# into .dev/ca.env when the dev container starts.

setup() {
  script="$BATS_TEST_DIRNAME/../dev-ca-env.sh"
  export HOUND_SHARED_CA_ENV="$BATS_TEST_TMPDIR/shared/ca.env"
  export HOUND_DEV_DIR="$BATS_TEST_TMPDIR/dev"
  export HOUND_CA_WAIT_SECONDS=3
  mkdir -p "$(dirname "$HOUND_SHARED_CA_ENV")"
}

@test "copies the shared env file into .dev/ca.env" {
  printf 'HOUND_CA_URL=http://backend:8000/api/\nHOUND_CA_TOKEN=abcd1234\n' >"$HOUND_SHARED_CA_ENV"
  run "$script"
  [ "$status" -eq 0 ]
  [ "$(cat "$HOUND_DEV_DIR/ca.env")" = "$(cat "$HOUND_SHARED_CA_ENV")" ]
  [ "$(stat -c %a "$HOUND_DEV_DIR/ca.env")" = "600" ]
  [[ "$output" != *"abcd1234"* ]]
}

@test "waits for the seed to write the file" {
  (sleep 1 && printf 'HOUND_CA_TOKEN=abcd1234\n' >"$HOUND_SHARED_CA_ENV") &
  run "$script"
  [ "$status" -eq 0 ]
  grep -qx "HOUND_CA_TOKEN=abcd1234" "$HOUND_DEV_DIR/ca.env"
}

@test "times out with a message when CISO Assistant never gets seeded" {
  HOUND_CA_WAIT_SECONDS=1 run "$script"
  [ "$status" -eq 1 ]
  [[ "$output" == *"not seeded after 1s"* ]]
  [ ! -e "$HOUND_DEV_DIR/ca.env" ]
}
