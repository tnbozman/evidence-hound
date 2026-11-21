#!/usr/bin/env bats
# Tests for the collector runner image (src/EvidenceHound.Cli/Dockerfile).
# Run: make test-image (builds the image first). Needs Docker.

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
  run docker run --rm --entrypoint /bin/sh "$IMAGE" -c true
  [ "$status" -ne 0 ]
  run docker run --rm --entrypoint /bin/bash "$IMAGE" -c true
  [ "$status" -ne 0 ]
}

@test "the runner works on a read-only root filesystem" {
  run docker run --rm --read-only "$IMAGE" collectors list
  [ "$status" -eq 0 ]
  [[ "$output" == *m365* ]]
}
