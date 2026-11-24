# Keep it up to date

The stack has three things to update: the host (patched daily, see [hardening](hardening.md#1-patch-the-host)), CISO Assistant
(`CA_VERSION`) and the Evidence Hound runner (`HOUND_RUNNER_IMAGE`). Both images are pinned in your private compliance repo's
`deploy/deploy.env` (or `.env`), so every update is a reviewed pull request and every version you ran is in git history.

## Get told about new versions

Let a bot open the pull request for you. In your private compliance repo:

- **Renovate** reads versions out of any file with a custom manager. For example, in `renovate.json`:

  ```json
  {
    "extends": ["config:recommended"],
    "customManagers": [
      {
        "customType": "regex",
        "fileMatch": ["^deploy/deploy\\.env$"],
        "matchStrings": ["CA_VERSION=(?<currentValue>v\\S+)"],
        "depNameTemplate": "ghcr.io/intuitem/ciso-assistant-community/backend",
        "datasourceTemplate": "docker"
      },
      {
        "customType": "regex",
        "fileMatch": ["^deploy/deploy\\.env$"],
        "matchStrings": ["HOUND_RUNNER_IMAGE=ghcr\\.io/tnbozman/evidence-hound:(?<currentValue>\\S+)"],
        "depNameTemplate": "ghcr.io/tnbozman/evidence-hound",
        "datasourceTemplate": "docker"
      }
    ],
    "schedule": ["before 9am on the first day of the month"]
  }
  ```

- **Dependabot** reads image tags from Compose files only, not from `.env` files. To use it, pin the two images in your
  `deploy/compose.override.yml` (`image:` for `backend`, `worker`, `frontend` and `runner`) and add a `docker-compose` entry for
  `/deploy` to `.github/dependabot.yml`.

Security releases of either project are announced on its GitHub releases page; watch both repositories for releases.

## Update once a month

Take updates in a monthly window, such as the first Tuesday morning, and sooner for a security release. For each update:

1. **Read the release notes** of every version between yours and the new one. Look for breaking changes, database migrations and
   changed settings. For CISO Assistant, check that its API still has what hound uses: the release notes of the Evidence Hound version
   you run name the CISO Assistant versions it was tested with.
2. **Back up**, and check the backup is there:
   `HOUND_BACKUP_DIR=... HOUND_BACKUP_RECIPIENTS=... ./backup.sh` ([backups](backups.md)). Note its name: it is your rollback point.
3. **Merge the pull request** that changes the version, then on the host:

   ```bash
   cd /opt/compliance && git pull
   cd /opt/evidence-hound/deploy/compose
   docker compose pull
   docker compose up -d --wait
   ```

   CISO Assistant migrates its database on start; the backend can take a few minutes to become healthy.
4. **Check it works**: sign in, open an audit, then run the collectors once and check their status:

   ```bash
   docker compose run --rm runner run --config /config/hound.yaml
   docker compose run --rm runner status --config /config/hound.yaml
   ```

5. **Run a restore test** of the backup from step 2 ([backups](backups.md#run-a-restore-test)). It proves the rollback point works,
   and it is evidence for your backup requirements.
6. **Record the update** as evidence on your patching control: the pull request, the date and the result of step 4.

## Roll back

CISO Assistant's database migrations only go forward, so going back to an older version means going back to the backup taken before
the update. Anything recorded since the update is lost and must be entered again, which is why the window is short and announced.

1. Revert the version change in your private repo (`git revert`), and `git pull` on the host.
2. Stop the stack: `docker compose down` (keep the volumes for now).
3. Restore the pre-update backup as in [restore for real](backups.md#restore-for-real), into fresh volumes:
   `docker compose down --volumes`, then steps 1 to 5 there.
4. Run the checks in step 4 above, and tell the GRC team what must be entered again.

A runner-only update rolls back without a restore: revert `HOUND_RUNNER_IMAGE` and `docker compose up -d runner`. The runner's evidence
store keeps working across versions.
