# s08 Reference Docker deployment

Status: Planned

**Goal:** a small organisation can self-host CISO Assistant and the collector runner on one hardened VM with Docker Compose, scheduled runs, secrets, backups and updates.

Folder: `deploy/compose/`. Fictional names only.

### s08-01 Collector runner image
- [ ] Done

**Story:** As an operator, I want a minimal, signed container image for the runner, so that it is easy and safe to deploy.

**Acceptance criteria**
- Multi-stage Dockerfile; runtime on the .NET chiseled or distroless image; non-root user; read-only root filesystem.
- Image built in CI, scanned (Trivy or Grype) with high and critical findings failing the build, and published to GHCR with build provenance on release.

**Tests first**
- CI job: container starts and `hound --version` returns 0.
- Container structure test: runs as non-root, no shell in final image.

**Functional check**
- `docker run --rm ghcr.io/<owner>/<repo>:dev --version`.

### s08-02 Compose stack
- [ ] Done

**Story:** As an operator, I want one Compose project for the whole stack, so that install and upgrade are repeatable.

**Acceptance criteria**
- Services: CISO Assistant (backend, worker, frontend) pinned to a release, PostgreSQL, Caddy with internal TLS, collector runner.
- Collector runner on its own network with access only to the CISO Assistant API and outbound internet; no inbound ports.
- Secrets through Docker secrets files; Postgres password via `_FILE` variable.
- Optional `kafka` profile adds Redpanda and the official dispatcher.
- `.env.example` documents every variable; real `.env` git-ignored.

**Tests first**
- CI job: `docker compose config` validates; a smoke job brings the stack up, waits for health and runs the runner once against the fake collector.

**Functional check**
- On a fresh VM: follow `deploy/compose/README.md` and reach the CISO Assistant login over HTTPS within 30 minutes.

### s08-03 Scheduling
- [ ] Done

**Story:** As an operator, I want collectors to run on their schedules without a human, so that evidence stays fresh.

**Acceptance criteria**
- The runner has a `serve` mode that runs each collector instance on its cron schedule (Cronos library), with jitter and no overlapping runs per instance.
- Alternative documented: host cron or systemd timer calling `docker compose run --rm runner run`.

**Tests first**
- `SchedulerTests.Next_occurrence_respects_cron_and_timezone`
- `SchedulerTests.Does_not_overlap_same_instance` (fake clock)

**Functional check**
- Stack runs overnight with a 15-minute test schedule; status shows each run.

### s08-04 Backup and restore
- [ ] Done

**Story:** As an operator, I want automated backups and a tested restore, so that the system of record survives failure and the restore test becomes evidence.

**Acceptance criteria**
- `deploy/compose/backup.sh`: `pg_dump`, CISO Assistant attachments volume, encrypted with age to an off-host target.
- `deploy/compose/restore-test.sh`: restores the latest backup into a scratch stack, checks the health endpoint and an audit count, and drops a result file into a manual-collector source so the restore test is recorded as evidence.

**Tests first**
- CI smoke job: back up the smoke stack, restore into a second project, compare audit counts.

**Functional check**
- Run both scripts on the VM; the restore test appears as evidence in CISO Assistant.

### s08-05 Hardening and update guide
- [ ] Done

**Acceptance criteria**
- `docs/deploy/hardening.md`: host patching, firewall, disk encryption, Docker daemon settings, log forwarding, certificate and uptime monitoring, MFA for all CISO Assistant users, service accounts per integration.
- `docs/deploy/updates.md`: Dependabot or Renovate for image tags, monthly window, backup before upgrade, rollback steps.

**Tests first**
- Markdown lint and link check in CI.

**Functional check**
- A reviewer applies the guide to the test VM and records the date.

## Discovered work

## Sprint review
