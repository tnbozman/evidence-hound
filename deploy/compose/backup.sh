#!/usr/bin/env bash
# Back up the reference stack: a PostgreSQL dump of CISO Assistant, CISO Assistant's data volume (attachments and
# evidence files) and the runner's data volume (evidence store, run history, manual drop folders), in one tar file
# encrypted with age to the recipients given. Nothing is written unencrypted outside a private temporary folder.
#
# Usage: deploy/compose/backup.sh             (run from deploy/compose with the stack up, as for manage.sh)
# Environment:
#   HOUND_BACKUP_DIR         where backups go. Make it off-host: an NFS or SMB mount, or a folder a sync tool copies
#                            off the host (docs/deploy/backups.md). Required.
#   HOUND_BACKUP_RECIPIENTS  an age recipients file (public keys). The private key stays off this host. Required.
#   HOUND_BACKUP_KEEP        how many backups to keep in HOUND_BACKUP_DIR (default 30).
# docker compose's own settings (.env, COMPOSE_FILE, COMPOSE_PROJECT_NAME) apply. Needs Docker with Compose and age.
# The PostgreSQL commands run in the container's shell, which expands its own POSTGRES_USER and POSTGRES_DB.
# shellcheck disable=SC2016
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=deploy/compose/backup-lib.sh
source "$here/backup-lib.sh"

usage() { sed -n '2,12p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; }
die() { echo "backup: $*" >&2; exit "${2:-1}"; }

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && { usage; exit 0; }
[[ -n "${HOUND_BACKUP_DIR:-}" ]] || die "set HOUND_BACKUP_DIR to the (off-host) folder backups go to" 2
[[ -n "${HOUND_BACKUP_RECIPIENTS:-}" ]] || die "set HOUND_BACKUP_RECIPIENTS to an age recipients file (public keys only)" 2
[[ -d "$HOUND_BACKUP_DIR" ]] || die "HOUND_BACKUP_DIR $HOUND_BACKUP_DIR is not a folder" 2
[[ -s "$HOUND_BACKUP_RECIPIENTS" ]] || die "HOUND_BACKUP_RECIPIENTS $HOUND_BACKUP_RECIPIENTS is missing or empty" 2
command -v age >/dev/null || die "age is not installed (apt install age)" 2

compose() { docker compose "$@"; }
project="$(compose config | sed -n 's/^name: //p' | head -n 1)"
[[ -n "$project" ]] || die "could not read the Compose project name; run from deploy/compose with its settings"
tar_image="$(compose config --images postgres)"

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
name="$(backup_file_name "$stamp")"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
chmod 0700 "$work"
mkdir "$work/backup"

echo "backup: dumping the CISO Assistant database"
compose exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom' >"$work/backup/ciso-assistant.dump"
audits="$(compose exec -T postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc "select count(*) from core_complianceassessment"' | tr -d '[:space:]')"

volume_tar() {
  docker run --rm -v "${project}_$1:/data:ro" --entrypoint tar "$tar_image" --numeric-owner -C /data -cf - .
}
echo "backup: copying the ca-data and runner-data volumes"
volume_tar ca-data >"$work/backup/ca-data.tar"
volume_tar runner-data >"$work/backup/runner-data.tar"

ca_version="$(compose config --images backend | sed 's/.*://')"
cat >"$work/backup/manifest.txt" <<EOF
taken_at=$stamp
project=$project
ciso_assistant=$ca_version
audits=$audits
EOF
(cd "$work/backup" && sha256sum ciso-assistant.dump ca-data.tar runner-data.tar >SHA256SUMS)

echo "backup: encrypting to $HOUND_BACKUP_DIR/$name"
tar -C "$work/backup" -cf - . | age -R "$HOUND_BACKUP_RECIPIENTS" -o "$HOUND_BACKUP_DIR/$name.part"
mv "$HOUND_BACKUP_DIR/$name.part" "$HOUND_BACKUP_DIR/$name"
backup_prune "$HOUND_BACKUP_DIR" "${HOUND_BACKUP_KEEP:-30}"
echo "backup: wrote $name ($audits audits, sha256 $(sha256sum "$HOUND_BACKUP_DIR/$name" | cut -d' ' -f1))"
