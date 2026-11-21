# s07 Thin deployment

Status: Planned

**Goal:** a small organisation can self-host CISO Assistant and the collector runner on one VM with Docker Compose, with the Microsoft 365 collector running on a schedule, so that evidence starts building history as early as possible.

This is the first half of the old s08 reference deployment, moved forward. Backups, restore testing and the hardening and update guide follow in [s12](s12-release-1.md). Until then the guide tells operators to snapshot the VM before upgrades.

Folder: `deploy/compose/`. Fictional names only.

### s07-01 Collector runner image
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

### s07-02 Compose stack
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

### s07-03 Scheduling
- [ ] Done

**Story:** As an operator, I want collectors to run on their schedules without a human, so that evidence stays fresh.

**Acceptance criteria**
- The runner has a `serve` mode that runs each collector instance on its cron schedule (Cronos library), with jitter and no overlapping runs per instance.
- Alternative documented: host cron or systemd timer calling `docker compose run --rm runner run`.
- The example deployment config runs the Microsoft 365 collector nightly and publishes to the example Essential Eight audit (s06-02); its certificate comes from a Docker secret.

**Tests first**
- `SchedulerTests.Next_occurrence_respects_cron_and_timezone`
- `SchedulerTests.Does_not_overlap_same_instance` (fake clock)

**Functional check**
- Stack runs overnight with a 15-minute test schedule; status shows each run.

## Discovered work

## Sprint review
