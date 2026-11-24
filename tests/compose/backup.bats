#!/usr/bin/env bats
# s13-01: backup and restore-test scripts for the reference Compose stack. These tests need no Docker: they cover the
# scripts' own logic (names, retention, the restore result) and their refusals. The smoke test (make compose-smoke) backs
# up a real stack and restores it into a second one.

setup() {
  root="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
  dir="$root/deploy/compose"
  # shellcheck source=deploy/compose/backup-lib.sh
  source "$dir/backup-lib.sh"
  work="$(mktemp -d)"
}

teardown() {
  rm -rf "$work"
}

@test "a backup is named by its UTC time, so names sort in time order" {
  [ "$(backup_file_name 20261124T231500Z)" = "ciso-assistant-20261124T231500Z.tar.age" ]
}

@test "the latest backup is the newest by name, ignoring other files" {
  touch "$work/ciso-assistant-20261101T020000Z.tar.age" "$work/ciso-assistant-20261124T020000Z.tar.age" \
        "$work/ciso-assistant-20261110T020000Z.tar.age" "$work/notes.txt" "$work/ciso-assistant-20261130T020000Z.tar.age.part"
  [ "$(backup_latest "$work")" = "$work/ciso-assistant-20261124T020000Z.tar.age" ]
}

@test "there is no latest backup in an empty folder" {
  run backup_latest "$work"
  [ "$status" -ne 0 ]
}

@test "pruning keeps the newest backups and removes only older backups" {
  for day in 01 02 03 04 05; do touch "$work/ciso-assistant-202611${day}T020000Z.tar.age"; done
  touch "$work/keep-me.txt"
  run backup_prune "$work" 2
  [ "$status" -eq 0 ]
  [ "$(cd "$work" && ls | sort | tr '\n' ' ')" = "ciso-assistant-20261104T020000Z.tar.age ciso-assistant-20261105T020000Z.tar.age keep-me.txt " ]
}

@test "pruning refuses to keep fewer than one backup" {
  touch "$work/ciso-assistant-20261101T020000Z.tar.age"
  run backup_prune "$work" 0
  [ "$status" -ne 0 ]
  [ -f "$work/ciso-assistant-20261101T020000Z.tar.age" ]
}

@test "the restore result names the backup, the checks and the outcome for the evidence reader" {
  run restore_result passed ciso-assistant-20261124T020000Z.tar.age abc123 200 3 3 20261125T030000Z 412
  [ "$status" -eq 0 ]
  [[ "$output" == *"Restore test: passed"* ]]
  [[ "$output" == *"Backup: ciso-assistant-20261124T020000Z.tar.age"* ]]
  [[ "$output" == *"SHA-256: abc123"* ]]
  [[ "$output" == *"Health endpoint: 200"* ]]
  [[ "$output" == *"Audits at backup: 3"* ]]
  [[ "$output" == *"Audits after restore: 3"* ]]
  [[ "$output" == *"Restored at: 20261125T030000Z"* ]]
  [[ "$output" == *"Duration: 412 seconds"* ]]
}

@test "the restore passes only when the health endpoint answers and the audit counts match" {
  [ "$(restore_outcome 200 3 3)" = "passed" ]
  [ "$(restore_outcome 200 3 2)" = "failed" ]
  [ "$(restore_outcome 500 3 3)" = "failed" ]
  [ "$(restore_outcome 200 "" 3)" = "failed" ]
}

@test "backup.sh refuses to run without a destination or age recipients" {
  run env -u HOUND_BACKUP_DIR -u HOUND_BACKUP_RECIPIENTS "$dir/backup.sh"
  [ "$status" -eq 2 ]
  [[ "$output" == *"HOUND_BACKUP_DIR"* ]]
  run env -u HOUND_BACKUP_RECIPIENTS HOUND_BACKUP_DIR="$work" "$dir/backup.sh"
  [ "$status" -eq 2 ]
  [[ "$output" == *"HOUND_BACKUP_RECIPIENTS"* ]]
}

@test "restore-test.sh refuses to run without the age identity" {
  run env -u HOUND_BACKUP_IDENTITY HOUND_BACKUP_DIR="$work" "$dir/restore-test.sh"
  [ "$status" -eq 2 ]
  [[ "$output" == *"HOUND_BACKUP_IDENTITY"* ]]
}

@test "both scripts print their usage" {
  run "$dir/backup.sh" --help
  [ "$status" -eq 0 ]
  [[ "$output" == *"Usage:"* ]]
  run "$dir/restore-test.sh" --help
  [ "$status" -eq 0 ]
  [[ "$output" == *"Usage:"* ]]
}

@test "a failed restore result says where it failed" {
  run restore_result failed ciso-assistant-20261124T020000Z.tar.age abc123 0 "" "" 20261125T030000Z 12 "checking the backup's checksums"
  [[ "$output" == *"Restore test: failed"* ]]
  [[ "$output" == *"Failed at: checking the backup's checksums"* ]]
  run restore_result passed b.tar.age abc 200 3 3 20261125T030000Z 4
  [[ "$output" != *"Failed at:"* ]]
}

@test "restore-test.sh still drops a failed result when the backup cannot be decrypted" {
  # Stubs: age fails; docker answers the Compose questions and records every call.
  mkdir "$work/bin"
  printf '#!/bin/sh\necho "age: no identity matched" >&2\nexit 1\n' >"$work/bin/age"
  cat >"$work/bin/docker" <<'STUB'
#!/bin/sh
echo "$*" >>"$STUB_LOG"
case "$*" in
  "compose config --images postgres") echo postgres:16 ;;
  "compose config") echo "name: example" ;;
  run*) cat >/dev/null 2>&1 || true ;;
esac
exit 0
STUB
  chmod +x "$work/bin/age" "$work/bin/docker"
  echo "AGE-SECRET-KEY-EXAMPLE" >"$work/identity"
  echo "not a backup" >"$work/ciso-assistant-20261124T020000Z.tar.age"

  STUB_LOG="$work/docker.log" PATH="$work/bin:$PATH" HOUND_BACKUP_IDENTITY="$work/identity" HOUND_RESTORE_SOURCE=backup-restore-test \
    run "$dir/restore-test.sh" "$work/ciso-assistant-20261124T020000Z.tar.age"

  [ "$status" -eq 1 ]
  [[ "$output" == *"Restore test: failed"* ]]
  [[ "$output" == *"Failed at: decrypting the backup"* ]]
  [[ "$output" == *"dropped restore-test-"* ]]
  grep -q 'example_runner-data:/data' "$work/docker.log"
}
