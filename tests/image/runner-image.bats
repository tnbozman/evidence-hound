#!/usr/bin/env bats
# Tests for the collector runner image (src/EvidenceHound.Cli/Dockerfile).
# Run: make test-image (builds the image first). Needs Docker and bats 1.5 or later.
bats_require_minimum_version 1.5.0

setup() {
  IMAGE="${HOUND_IMAGE:-evidence-hound-runner:test}"
}

@test "the runner starts and hound --version returns 0" {
  run docker run --rm "$IMAGE" --version
  [ "$status" -eq 0 ]
  [[ "$output" =~ ^[0-9]+\.[0-9]+\.[0-9]+ ]]
}

@test "the runner runs as a non-root user" {
  user="$(docker image inspect --format '{{.Config.User}}' "$IMAGE")"
  [ -n "$user" ]
  [ "$user" != "root" ]
  [ "$user" != "0" ]
  [[ "$user" != 0:* ]]
}

@test "the final image has no shell" {
  run -127 docker run --rm --entrypoint /bin/sh "$IMAGE" -c true
  run -127 docker run --rm --entrypoint /bin/bash "$IMAGE" -c true
}

@test "the runner works on a read-only root filesystem" {
  run docker run --rm --read-only "$IMAGE" collectors list
  [ "$status" -eq 0 ]
  [[ "$output" == *m365* ]]
}

@test "the runner knows IANA time zones such as Australia/Sydney" {
  config="$BATS_TEST_TMPDIR/config"
  mkdir -p "$config"
  cat >"$config/hound.yaml" <<'YAML'
ciso_assistant:
  url: http://backend:8000/api/
  token: env:HOUND_CA_TOKEN
evidence:
  root: /var/lib/evidence-hound/evidence
scheduler:
  time_zone: Australia/Sydney
collectors:
  - name: fake
    type: fake
    schedule: "0 2 * * *"
target:
  mapping: /app/mappings/example.yaml
  compliance_assessment: EXAMPLE-BASELINE-2026
YAML
  chmod -R a+rX "$config"
  run docker run --rm --read-only -v "$config:/config:ro" "$IMAGE" config validate /config/hound.yaml
  [[ "$output" != *time_zone_unknown* ]]
  [ "$status" -eq 0 ]
}
