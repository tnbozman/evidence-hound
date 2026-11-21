#!/usr/bin/env bats
# Tests for .devcontainer/compose.yml: the dev container and CISO Assistant share one network.
# Uses `docker compose config`, which reads the files and needs no running daemon.

setup() {
  compose="$BATS_TEST_DIRNAME/../../.devcontainer/compose.yml"
  export HOUND_CA_SECRET_KEY="local-test-only"
}

model() {
  docker compose -f "$compose" "$@" config --format json
}

@test "runs the dev container with CISO Assistant and a one-shot seed" {
  services="$(model | jq -r '.services | keys | join(" ")')"
  [ "$services" = "backend ca-seed caddy dev frontend" ]
}

@test "every service is on the same network" {
  networks="$(model | jq -r '[.services[] | .networks | keys] | unique | map(join(",")) | join(" ")')"
  [ "$networks" = "default" ]
}

@test "the dev container reaches CISO Assistant by service name" {
  url="$(model | jq -r '.services.dev.environment.HOUND_CA_URL')"
  [ "$url" = "http://backend:8000/api/" ]
}

@test "the seed waits for a healthy backend and shares the env file with the dev container" {
  m="$(model)"
  [ "$(jq -r '.services["ca-seed"].depends_on.backend.condition' <<<"$m")" = "service_healthy" ]
  [ "$(jq -r '.services["ca-seed"].environment.SEED_API_URL' <<<"$m")" = "http://backend:8000/api" ]
  seed_volume="$(jq -r '.services["ca-seed"].volumes[] | select(.target == "/run/hound") | .source' <<<"$m")"
  dev_volume="$(jq -r '.services.dev.volumes[] | select(.target == "/run/hound") | .source' <<<"$m")"
  [ -n "$seed_volume" ]
  [ "$seed_volume" = "$dev_volume" ]
}

@test "Kafka and the dispatcher only start with the kafka profile" {
  [ "$(model | jq -r '.services | has("redpanda")')" = "false" ]
  [ "$(model --profile kafka | jq -r '.services | has("redpanda") and has("dispatcher")')" = "true" ]
}

@test "refuses to start without the generated secret key" {
  unset HOUND_CA_SECRET_KEY
  run docker compose -f "$compose" --env-file /dev/null config --quiet
  [ "$status" -ne 0 ]
}
