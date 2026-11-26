#!/usr/bin/env bats
# Static checks of the reference Compose stack (deploy/compose). Needs Docker Compose and jq, no images.
# Run: make test-compose. The smoke test (make compose-smoke) brings the stack up for real.

setup() {
  root="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
  dir="$root/deploy/compose"
  config() { docker compose --project-directory "$dir" -f "$dir/compose.yml" --env-file "$dir/.env.example" "$@" config --format json; }
}

@test "the stack validates with the documented example settings" {
  run docker compose --project-directory "$dir" -f "$dir/compose.yml" --env-file "$dir/.env.example" config --quiet
  [ "$status" -eq 0 ]
}

@test "the stack has CISO Assistant, PostgreSQL, Caddy and the runner" {
  services="$(config | jq -r '.services | keys | sort | join(" ")')"
  [ "$services" = "backend caddy frontend postgres runner worker" ]
}

@test "the CISO Assistant images are pinned to one release" {
  tags="$(config | jq -r '[.services.backend.image, .services.worker.image, .services.frontend.image] | map(split(":")[1]) | unique | join(" ")')"
  [[ "$tags" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]
}

@test "only Caddy publishes ports" {
  published="$(config | jq -r '[.services | to_entries[] | select(.value.ports != null and (.value.ports | length) > 0) | .key] | join(" ")')"
  [ "$published" = "caddy" ]
}

@test "Caddy serves HTTPS with its internal certificate authority" {
  grep -Eq '^\s*tls internal' "$dir/Caddyfile"
}

@test "the runner is only on its own network, which it shares only with the CISO Assistant API" {
  runner_networks="$(config | jq -r '.services.runner.networks | keys | join(" ")')"
  [ "$runner_networks" = "collect" ]
  sharing="$(config | jq -r '[.services | to_entries[] | select(.value.networks // {} | has("collect")) | .key] | sort | join(" ")')"
  [ "$sharing" = "backend runner" ]
  internal="$(config | jq -r '.networks.collect.internal // false')"
  [ "$internal" = "false" ]
}

@test "the database network is internal" {
  [ "$(config | jq -r '.networks.data.internal')" = "true" ]
  [ "$(config | jq -r '.services.postgres.networks | keys | join(" ")')" = "data" ]
}

@test "secrets come from Docker secrets files, never from plain variables" {
  json="$(config)"
  [ "$(jq -r '.services.postgres.environment.POSTGRES_PASSWORD_FILE' <<<"$json")" = "/run/secrets/postgres_password" ]
  [ "$(jq -r '.services.postgres.environment.POSTGRES_PASSWORD // "unset"' <<<"$json")" = "unset" ]
  [ "$(jq -r '.services.runner.environment.HOUND_CA_TOKEN_FILE' <<<"$json")" = "/run/secrets/hound_ca_token" ]
  for service in backend worker runner; do
    [ "$(jq -r --arg s "$service" '.services[$s].environment | to_entries | map(select(.key | test("PASSWORD$|SECRET_KEY$|TOKEN$"))) | length' <<<"$json")" = "0" ]
  done
  [ "$(jq -r '.secrets | to_entries | map(select(.value.file == null)) | length' <<<"$json")" = "0" ]
}

@test "the runner is locked down: read-only, no capabilities, no new privileges" {
  json="$(config)"
  [ "$(jq -r '.services.runner.read_only' <<<"$json")" = "true" ]
  [ "$(jq -r '.services.runner.cap_drop | join(" ")' <<<"$json")" = "ALL" ]
  jq -e '.services.runner.security_opt | index("no-new-privileges:true")' <<<"$json" >/dev/null
}

@test "every variable the stack uses is documented in .env.example" {
  [ -f "$dir/compose.yml" ]
  used="$(grep -oE '\$\{[A-Z0-9_]+' "$dir/compose.yml" | tr -d '${' | sort -u)"
  for name in $used; do
    grep -Eq "^#? ?${name}=" "$dir/.env.example" || { echo "undocumented: $name"; false; }
  done
}

@test "the real .env and the secrets folder are git-ignored" {
  git -C "$root" check-ignore -q deploy/compose/.env
  git -C "$root" check-ignore -q deploy/compose/secrets/postgres_password
}

@test "no Kafka profile: the stack is the same with it (removed in release 3)" {
  services="$(config --profile kafka | jq -r '.services | keys | sort | join(" ")')"
  [ "$services" = "backend caddy frontend postgres runner worker" ]
}

@test "the runner serves the collectors on their schedules and restarts unless stopped" {
  json="$(config)"
  [ "$(jq -r '.services.runner.command[0]' <<<"$json")" = "serve" ]
  [ "$(jq -r '.services.runner.restart' <<<"$json")" = "unless-stopped" ]
}

@test "ca-env.sh exports the database password and secret key from the secrets files, then runs its command" {
  secrets="$(mktemp -d)"
  printf 'db-example\n' >"$secrets/postgres_password"
  printf 'key-example\n' >"$secrets/django_secret_key"
  run env HOUND_SECRETS_PATH="$secrets" sh "$dir/ca-env.sh" sh -c 'echo "$POSTGRES_PASSWORD $DJANGO_SECRET_KEY"'
  rm -rf "$secrets"
  [ "$status" -eq 0 ]
  [ "$output" = "db-example key-example" ]
}

@test "CISO Assistant's backend and worker start through ca-env.sh, which is mounted read-only" {
  for service in backend worker; do
    entrypoint="$(config | jq -r ".services.$service.entrypoint | join(\" \")")"
    [ "$entrypoint" = "/bin/sh /hound/ca-env.sh" ]
    mount="$(config | jq -r ".services.$service.volumes[] | select(.target == \"/hound/ca-env.sh\") | .read_only")"
    [ "$mount" = "true" ]
  done
}

@test "no script or guide runs manage.py in the backend without the secrets (use manage.sh)" {
  run grep -rnE 'exec[^|]* backend python manage\.py' "$dir" "$root/docs" --include='*.sh' --include='*.md' --include='*.yml'
  [ "$status" -eq 1 ]
}

@test "the template compliance repo's deploy folder validates with the stack and adds its override" {
  repo="$root/examples/compliance-repo"
  # deploy.env names the override by its path on the host; here it is the template's copy.
  files="$dir/compose.yml:$repo/deploy/compose.override.yml"
  run env COMPOSE_FILE="$files" docker compose --project-directory "$dir" --env-file "$repo/deploy/deploy.env" config --quiet
  [ "$status" -eq 0 ]
  grep -Eq '^COMPOSE_FILE=compose\.yml:/opt/compliance/deploy/compose\.override\.yml$' "$repo/deploy/deploy.env"
}
