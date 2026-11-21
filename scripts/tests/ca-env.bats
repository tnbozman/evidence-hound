#!/usr/bin/env bats
# Tests for dev/ciso-assistant/seed/write-env.sh, which turns the seed script's output
# into an env file for hound. The token is a made-up stand-in.

setup() {
  script="$BATS_TEST_DIRNAME/../../dev/ciso-assistant/seed/write-env.sh"
  env_file="$BATS_TEST_TMPDIR/out/ca.env"
  seed_output=$'seed: Example Pty Ltd is ready\nHOUND_CA_TOKEN=abcd1234abcd1234'
}

@test "writes the URL and token to the env file" {
  run bash "$script" "http://backend:8000/api/" "$env_file" <<<"$seed_output"
  [ "$status" -eq 0 ]
  [ "$(cat "$env_file")" = $'HOUND_CA_URL=http://backend:8000/api/\nHOUND_CA_TOKEN=abcd1234abcd1234' ]
}

@test "the env file is readable by its owner only" {
  bash "$script" "http://backend:8000/api/" "$env_file" <<<"$seed_output"
  [ "$(stat -c %a "$env_file")" = "600" ]
}

@test "prints the other seed lines but never the token" {
  run bash "$script" "http://backend:8000/api/" "$env_file" <<<"$seed_output"
  [[ "$output" == *"seed: Example Pty Ltd is ready"* ]]
  [[ "$output" != *"abcd1234"* ]]
}

@test "keeps the last token when the seed printed several" {
  bash "$script" "http://backend:8000/api/" "$env_file" <<<$'HOUND_CA_TOKEN=old\nHOUND_CA_TOKEN=new'
  grep -qx "HOUND_CA_TOKEN=new" "$env_file"
}

@test "fails without a token and writes nothing" {
  run bash "$script" "http://backend:8000/api/" "$env_file" <<<"seed: something went wrong"
  [ "$status" -eq 1 ]
  [[ "$output" == *"printed no token"* ]]
  [ ! -e "$env_file" ]
}

@test "can leave the file readable by other users of a shared volume" {
  HOUND_CA_ENV_MODE=644 bash "$script" "http://backend:8000/api/" "$env_file" <<<"$seed_output"
  [ "$(stat -c %a "$env_file")" = "644" ]
}
