#!/usr/bin/env bats
# make ca-up: every docker compose call against the local CISO Assistant gets its secret key, including the seed step.

setup() {
  root="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
}

@test "make ca-up passes the secret key to the seed step as well as to compose up" {
  run make -s -n -C "$root" ca-up
  [ "$status" -eq 0 ]
  seed_line="$(grep 'scripts/ca-seed.sh' <<<"$output")"
  [[ "$seed_line" == *'HOUND_CA_SECRET_KEY='* ]]
}
