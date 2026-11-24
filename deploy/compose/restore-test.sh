#!/usr/bin/env bash
# Restore test: decrypt a backup made by backup.sh, restore it into a separate, temporary Compose project, start
# CISO Assistant on it, call its health endpoint, compare its audit count with the backup's, then remove the temporary
# project. The live stack is only read, except that the result is dropped into a manual source's drop folder on the
# runner's data volume, so the next run collects it and a person attests it as restore-test evidence.
#
# Usage: deploy/compose/restore-test.sh [backup-file]   (default: the newest in HOUND_BACKUP_DIR; run from deploy/compose)
# Environment:
#   HOUND_BACKUP_IDENTITY   the age identity (private key) file that decrypts the backup. Required. Bring it to the host
#                           for the test and remove it afterwards.
#   HOUND_BACKUP_DIR        where backups are, when no file is given.
#   HOUND_RESTORE_SOURCE    the manual source whose drop folder gets the result, such as backup-restore-test. Optional.
# Exit status: 0 when the restore test passed, 1 when it failed (the result is still dropped), 2 for a usage problem.
# The PostgreSQL commands run in the container's shell, which expands its own POSTGRES_USER and POSTGRES_DB.
# shellcheck disable=SC2016
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deploy/compose/backup-lib.sh
source "$here/backup-lib.sh"

usage() { sed -n '2,13p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; }
die() { echo "restore-test: $*" >&2; exit "${2:-1}"; }

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && { usage; exit 0; }
[[ -n "${HOUND_BACKUP_IDENTITY:-}" ]] || die "set HOUND_BACKUP_IDENTITY to the age identity file that decrypts the backup" 2
[[ -s "$HOUND_BACKUP_IDENTITY" ]] || die "HOUND_BACKUP_IDENTITY $HOUND_BACKUP_IDENTITY is missing or empty" 2
command -v age >/dev/null || die "age is not installed (apt install age)" 2
if [[ -n "${1:-}" ]]; then
  backup="$1"
else
  [[ -n "${HOUND_BACKUP_DIR:-}" ]] || die "give a backup file, or set HOUND_BACKUP_DIR" 2
  backup="$(backup_latest "$HOUND_BACKUP_DIR")" || die "no backup in $HOUND_BACKUP_DIR" 2
fi
[[ -f "$backup" ]] || die "no backup file $backup" 2

started="$(date -u +%s)"
live() { docker compose "$@"; }
live_project="$(live config | sed -n 's/^name: //p' | head -n 1)"
[[ -n "$live_project" ]] || die "could not read the Compose project name; run from deploy/compose with its settings"
tar_image="$(live config --images postgres)"

work="$(mktemp -d)"
chmod 0700 "$work"
scratch="hound-restore-test-$$"
# The temporary project: the same Compose files and settings, its own name (so its own volumes) and throwaway secrets.
scratch_compose() { COMPOSE_PROJECT_NAME="$scratch" HOUND_SECRETS_DIR="$work/secrets" docker compose "$@"; }
cleanup() {
  scratch_compose down --volumes --remove-orphans >/dev/null 2>&1 || true
  rm -rf "$work"
}
trap cleanup EXIT

echo "restore-test: decrypting $(basename "$backup")"
mkdir "$work/backup"
age -d -i "$HOUND_BACKUP_IDENTITY" "$backup" | tar -C "$work/backup" -xf -
(cd "$work/backup" && sha256sum --check --quiet SHA256SUMS) || die "the backup's contents do not match its checksums"
audits_before="$(sed -n 's/^audits=//p' "$work/backup/manifest.txt")"

mkdir "$work/secrets"
openssl rand -hex 24 >"$work/secrets/postgres_password"
openssl rand -hex 32 >"$work/secrets/django_secret_key"
: >"$work/secrets/hound_ca_token"
: >"$work/secrets/m365_client_certificate"
chmod 0444 "$work/secrets/"*

echo "restore-test: restoring the database into project $scratch"
scratch_compose up -d --wait --wait-timeout 300 postgres
scratch_compose exec -T postgres sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --exit-on-error' \
  <"$work/backup/ciso-assistant.dump"

echo "restore-test: restoring CISO Assistant's data volume"
scratch_compose up --no-start backend
docker run --rm -i -v "${scratch}_ca-data:/data" --entrypoint tar "$tar_image" --numeric-owner -C /data -xpf - \
  <"$work/backup/ca-data.tar"

echo "restore-test: starting CISO Assistant on the restored data"
health=0
if scratch_compose up -d --wait --wait-timeout 600 backend; then
  health="$(scratch_compose exec -T backend python3 -c \
    "import urllib.request; print(urllib.request.urlopen('http://localhost:8000/api/health/', timeout=10).status)" \
    2>/dev/null | tr -d '[:space:]' || echo 0)"
fi
audits_after="$(scratch_compose exec -T postgres sh -c \
  'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc "select count(*) from core_complianceassessment"' | tr -d '[:space:]' || true)"

finished="$(date -u +%Y%m%dT%H%M%SZ)"
outcome="$(restore_outcome "$health" "$audits_before" "$audits_after")"
result="$(restore_result "$outcome" "$(basename "$backup")" "$(sha256sum "$backup" | cut -d' ' -f1)" \
  "$health" "$audits_before" "$audits_after" "$finished" "$(($(date -u +%s) - started))")"
echo "$result"

if [[ -n "${HOUND_RESTORE_SOURCE:-}" ]]; then
  [[ "$HOUND_RESTORE_SOURCE" =~ ^[a-z0-9][a-z0-9-]*$ ]] || die "HOUND_RESTORE_SOURCE must be a collector name" 2
  file="restore-test-$finished.txt"
  printf '%s\n' "$result" >"$work/$file"
  # Into the live runner's data volume, owned by the runner's non-root user (1654), where the manual source reads it.
  docker run --rm -i -v "${live_project}_runner-data:/data" -v "$work/$file:/in/$file:ro" --entrypoint sh "$tar_image" -c \
    "mkdir -p /data/manual/$HOUND_RESTORE_SOURCE && cp /in/$file /data/manual/$HOUND_RESTORE_SOURCE/ \
     && chown -R 1654:1654 /data/manual/$HOUND_RESTORE_SOURCE"
  echo "restore-test: dropped $file into manual source $HOUND_RESTORE_SOURCE"
fi

[[ "$outcome" == "passed" ]]
