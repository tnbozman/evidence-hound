#!/usr/bin/env bash
# Pure helpers for backup.sh and restore-test.sh: names, retention and the restore result. No Docker here, so
# tests/compose/backup.bats tests them directly.

# The file name of a backup taken at a UTC time stamp such as 20261124T231500Z. Names sort in time order.
backup_file_name() {
  printf 'ciso-assistant-%s.tar.age\n' "$1"
}

# The newest backup in a folder, by name; fails when there is none.
backup_latest() {
  local latest
  latest="$(find "$1" -maxdepth 1 -type f -name 'ciso-assistant-*.tar.age' -printf '%f\n' | sort | tail -n 1)"
  [[ -n "$latest" ]] || return 1
  printf '%s/%s\n' "$1" "$latest"
}

# Removes all but the newest <keep> backups in a folder, printing each one removed. Other files are left alone.
backup_prune() {
  local dir="$1" keep="$2" old
  [[ "$keep" =~ ^[0-9]+$ && "$keep" -ge 1 ]] || { echo "backup: keep at least one backup (HOUND_BACKUP_KEEP=$keep)" >&2; return 2; }
  find "$dir" -maxdepth 1 -type f -name 'ciso-assistant-*.tar.age' -printf '%f\n' | sort -r | tail -n +"$((keep + 1))" |
    while read -r old; do
      rm -f -- "${dir:?}/$old"
      echo "removed $old"
    done
}

# passed when the restored stack's health endpoint answered 200 and it holds as many audits as the backup did.
restore_outcome() {
  local health="$1" before="$2" after="$3"
  if [[ "$health" == "200" && -n "$before" && "$before" == "$after" ]]; then echo passed; else echo failed; fi
}

# The restore test's result, as the text file a manual source collects and a person attests.
# Arguments: outcome, backup file name, its SHA-256, health status, audits at backup, audits after restore, restore time, seconds.
restore_result() {
  cat <<EOF
Restore test: $1
Backup: $2
SHA-256: $3
Health endpoint: $4
Audits at backup: $5
Audits after restore: $6
Restored at: $7
Duration: $8 seconds

What was done: the backup was decrypted, its PostgreSQL dump and CISO Assistant's data volume were restored into a
separate, temporary Compose project, CISO Assistant was started on them, its health endpoint was called and its audits
were counted. The temporary project was then removed. The live stack was not touched.
The test passes only when the health endpoint answers 200 and the audit counts match.
EOF
}
