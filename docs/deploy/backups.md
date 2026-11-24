# Backups and restore tests

CISO Assistant is your system of record, so its data must survive a lost disk, a bad upgrade or ransomware on the host. The reference
stack ships two scripts in [`deploy/compose`](../../deploy/compose/README.md):

| Script | What it does | How often |
| --- | --- | --- |
| [`backup.sh`](../../deploy/compose/backup.sh) | Dumps CISO Assistant's PostgreSQL database and copies its data volume (attachments and evidence files) and the runner's data volume (evidence store, run history, manual drop folders) into one file, encrypted with [age](https://github.com/FiloSottile/age), in an off-host folder. Keeps the newest 30. | Daily |
| [`restore-test.sh`](../../deploy/compose/restore-test.sh) | Decrypts the newest backup, restores it into a separate, temporary Compose project, starts CISO Assistant on it, checks its health endpoint and that it holds as many audits as the backup did, then removes the temporary project. It drops the result into a manual source's drop folder, so the test becomes evidence. | Quarterly, and after every upgrade |

Nothing is written unencrypted outside a private temporary folder, and the live stack is never changed by a restore test.

## Set up once

1. **Install age** on the host: `sudo apt install age`.
2. **Make the key pair off the host**, on the laptop of the person who looks after backups or in your password manager's secure
   notes, never on the VM:

   ```console
   $ age-keygen -o evidence-hound-backup.key
   Public key: age1...
   ```

   Keep `evidence-hound-backup.key` (the identity) in your password manager. Copy only the public key to the host:
   `echo age1... | sudo tee /etc/evidence-hound/backup-recipients.txt`. Add a second person's public key on another line, so one lost
   key does not lose the backups.
3. **Make the backup folder off-host.** Mount a share from another machine or a NAS (NFS or SMB) at, say, `/mnt/backups/evidence-hound`,
   or point it at a local folder that a sync tool (such as rclone or your backup agent) copies off the host every day. A backup on
   the same disk as the VM is not a backup.
4. **Schedule it.** As root, in `/etc/cron.d/evidence-hound-backup`:

   ```cron
   # Daily at 01:30, before the collectors' 02:00 runs.
   30 1 * * * root cd /opt/evidence-hound/deploy/compose && HOUND_BACKUP_DIR=/mnt/backups/evidence-hound HOUND_BACKUP_RECIPIENTS=/etc/evidence-hound/backup-recipients.txt ./backup.sh >>/var/log/evidence-hound-backup.log 2>&1
   ```

   Check the log the next morning: the last line reads `backup: wrote ciso-assistant-<time>.tar.age (<n> audits, sha256 ...)`.

## Run a restore test

Every quarter, and after every CISO Assistant upgrade:

1. Bring the identity to the host for the test only: `sudo install -m 0400 /dev/stdin /root/backup.key` and paste it.
2. Run the test, naming the manual source that records restore tests in your `hound.yaml` (the example config calls it
   `example-backup-restore-test`, with the check `backup.restore_tested`):

   ```console
   $ cd /opt/evidence-hound/deploy/compose
   $ sudo HOUND_BACKUP_DIR=/mnt/backups/evidence-hound HOUND_BACKUP_IDENTITY=/root/backup.key \
       HOUND_RESTORE_SOURCE=example-backup-restore-test ./restore-test.sh
   ...
   Restore test: passed
   Audits at backup: 3
   Audits after restore: 3
   restore-test: dropped restore-test-20261125T030000Z.txt into manual source example-backup-restore-test
   ```

3. Remove the identity: `sudo shred -u /root/backup.key`.
4. The next run collects the result file. Your agent proposes the result (skill "process-manual-evidence") and you attest it in the
   [weekly review](../manual-evidence.md#the-weekly-review), like any other manual evidence. A failed test is evidence too: record it
   and open an action.

The test needs about 2 GB of free memory and disk for the temporary project, and takes 5 to 10 minutes, most of it CISO Assistant
starting. It exits 0 when the test passed, 1 when it failed and 2 when it could not start.

## Restore for real

When the live stack is lost, on a new host with the stack installed but not started ([deploy/compose](../../deploy/compose/README.md#install)):

1. Decrypt the newest backup: `age -d -i backup.key ciso-assistant-<time>.tar.age | tar -C /root/restore -xf -`, then check it:
   `(cd /root/restore && sha256sum --check SHA256SUMS)`.
2. Start PostgreSQL only: `docker compose up -d --wait postgres`.
3. Restore the database:
   `docker compose exec -T postgres sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --exit-on-error' </root/restore/ciso-assistant.dump`.
4. Create the other containers without starting them (`docker compose up --no-start`), then restore the two volumes:

   ```bash
   docker run --rm -i -v evidence-hound_ca-data:/data --entrypoint tar postgres:16.11 --numeric-owner -C /data -xpf - </root/restore/ca-data.tar
   docker run --rm -i -v evidence-hound_runner-data:/data --entrypoint tar postgres:16.11 --numeric-owner -C /data -xpf - </root/restore/runner-data.tar
   ```

5. Start everything: `docker compose up -d --wait`, sign in, and check the audits. Use the same `django_secret_key` as before if you
   still have it; with a new one, everyone signs in again and the runner needs a new token (`./runner-token.sh`).

`manifest.txt` in the backup names the CISO Assistant version it came from. Restore into the same version, then upgrade.
