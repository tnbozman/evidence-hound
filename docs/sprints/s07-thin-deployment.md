# s07 Thin deployment

Status: Done

**Goal:** a small organisation can self-host CISO Assistant and the collector runner on one VM with Docker Compose, with the Microsoft 365 collector running on a schedule, so that evidence starts building history as early as possible. Everything specific to the organisation, from CISO Assistant's deployment settings to its users and the scope from s06, lives in the organisation's own private compliance repo, and a tutorial walks through creating it.

This is the first half of the old s08 reference deployment, moved forward. Backups, restore testing and the hardening and update guide follow in [s13](s13-release-1.md). Until then the guide tells operators to snapshot the VM before upgrades.

Folder: `deploy/compose/`. Fictional names only.

### s07-01 Collector runner image
- [x] Done

**Story:** As an operator, I want a minimal, signed container image for the runner, so that it is easy and safe to deploy.

**Acceptance criteria**
- Multi-stage Dockerfile; runtime on the .NET chiseled or distroless image; non-root user; read-only root filesystem.
- Image built in CI, scanned (Trivy or Grype) with high and critical findings failing the build, and published to GHCR with build provenance on release.

**Tests first**
- CI job: container starts and `hound --version` returns 0.
- Container structure test: runs as non-root, no shell in final image.

**Functional check**
- `docker run --rm ghcr.io/<owner>/<repo>:dev --version`.
- Result (2026-11-21): the same Dockerfile built locally passed `make test-image` (starts, `--version` prints `0.1.0`, user `app`, no shell, read-only root); amd64 and arm64 both build; CI job `image` passed with no high or critical Grype finding. The `dev` tag is published from main after merge. Evidence: [`docs/evidence/s07/s07-01.md`](../evidence/s07/s07-01.md).

### s07-02 Compose stack
- [x] Done

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
- Result (2026-11-22): the `compose` job in [CI run 44975475229](https://github.com/tnbozman/evidence-hound/actions/runs/44975475229) brought the stack up, issued the runner token, applied the example scope through the runner image, recorded 4 scheduled runs and reached CISO Assistant over HTTPS through Caddy. The fresh-VM run is part of the s07-06 check. Evidence: [`docs/evidence/s07/s07-02.md`](../evidence/s07/s07-02.md).

### s07-03 Scheduling
- [x] Done

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
- Result (2026-11-22): in [CI run 44975475229](https://github.com/tnbozman/evidence-hound/actions/runs/44975475229), `hound serve` ran the fake collector every minute and 4 runs were recorded. **Owed by the person:** the overnight run with a 15-minute schedule. Evidence: [`docs/evidence/s07/s07-03.md`](../evidence/s07/s07-03.md).

### s07-04 Users, teams and role assignments as configuration
- [x] Done

**Story:** As the capability owner, I want who can do what in CISO Assistant declared in the private compliance repo, so that access is reviewed through pull requests and a rebuilt instance gets the same access.

**Acceptance criteria**
- `scope/access.yaml` declares users (email as the key, first and last name, active), teams, and role assignments (a built-in or custom role, on a domain or the global domain, to a user or team). It is applied with the rest of the scope by `scope plan` and `scope apply`, through `/api/users/`, `/api/teams/`, `/api/user-groups/` and `/api/role-assignments/` (recorded in s06-05).
- `hound` never sets or reads passwords and never creates API tokens. A new user gets CISO Assistant's own invitation; a file that sets a password is an error (`scope.secret_in_file`).
- A user removed from the file is deactivated, not deleted, and only with `--prune`. Service accounts used by collectors are declared with `service_account: true`, and `scope plan` reports any service account holding more than the role the guide recommends.
- Validation: emails are unique and well formed, roles and domains exist, and every domain has at least one user who can approve (so risk acceptances and sign-offs have an approver).

**Tests first**
- `AccessScopeTests.Duplicate_email_is_reported_with_line`
- `AccessScopeTests.Password_in_file_is_rejected`
- `AccessScopeTests.Domain_without_approver_is_reported`
- `AccessPlanTests.Removed_user_is_deactivated_only_with_prune`
- Contract: `AccessClientTests.Creates_user_team_and_role_assignment` (recorded responses)

**Functional check**
- Apply a fictional `access.yaml` with two users and a team to the local CISO Assistant; both appear with their roles; re-applying writes nothing.
- Result (2026-11-22): the functional seed applies the example `access.yaml` (four users, a team, their roles); `ScopePlanFunctionalTests` finds them with their groups and a second plan is empty; 19 of 19 functional tests passed in [CI run 44975475229](https://github.com/tnbozman/evidence-hound/actions/runs/44975475229). Roles are given as memberships of built-in user groups, not role assignments (see Discovered work). Evidence: [`docs/evidence/s07/s07-04.md`](../evidence/s07/s07-04.md).

**Notes**
- Users' names and emails are personal data. They belong only in the private repo; the public example uses fictional `example.com` users.
- Check whether the API can require MFA per user; record the answer and, if it cannot, make MFA a step in the tutorial (s07-06).

### s07-05 CISO Assistant settings as configuration
- [x] Done

**Story:** As the capability owner, I want CISO Assistant's feature flags and general settings declared in the repo, so that the instance is configured the same way after every rebuild or upgrade.

**Acceptance criteria**
- `scope/settings.yaml` declares feature flags (such as `tprm`, `metrology`, `bia`) and general settings (such as the default score scale) by name, applied through `/api/settings/feature-flags/` and `/api/settings/general/` (recorded in s06-05).
- Only settings named in the file are managed; others are left as they are and listed by `scope plan --show-unmanaged`.
- Validation fails on an unknown flag or setting name, using a settings snapshot committed next to the OpenAPI snapshot from s06-05.
- `scope validate` reports when a scope file uses a feature that a flag it turns off would hide, such as third-party files with `tprm: false`.

**Tests first**
- `SettingsScopeTests.Unknown_flag_is_reported`
- `SettingsPlanTests.Only_named_settings_are_changed`
- `SettingsScopeTests.Disabled_feature_in_use_is_reported`

**Functional check**
- Turn a fictional flag off and on through the file against the local CISO Assistant; the UI follows.
- Result (2026-11-22): `ttps` turned on and off through the file against the local CISO Assistant, read back from the API each time, and a second plan was empty; the snapshot matches the live flags; 19 of 19 functional tests passed in [CI run 44975475229](https://github.com/tnbozman/evidence-hound/actions/runs/44975475229). Evidence: [`docs/evidence/s07/s07-05.md`](../evidence/s07/s07-05.md).

### s07-06 Tutorial: your private compliance repo and first deployment
- [x] Done

**Story:** As an organisation adopting Evidence Hound, I want a step-by-step tutorial from an empty VM and an empty private repo to a scheduled collection run, so that I can stand up the whole thing without guessing where each setting belongs.

**Acceptance criteria**
- `docs/tutorials/first-deployment.md` walks through, with the fictional Example Pty Ltd:
  1. Creating a new **private** git repo from the compliance repo template (s06-12), with branch protection, required review and secret scanning with push protection turned on.
  2. What goes in the repo and what never does. In the repo: `deploy/` (the Compose override, a non-secret `deploy.env` with the CISO Assistant version, hostname, time zone and the runner image tag), `hound.yaml` with collector schedules, `scope/` (setup, controls, audits, `access.yaml`, `settings.yaml`), mappings and manual source folders. Never in the repo: passwords, tokens, certificates, private keys, the Postgres password, or exported evidence. Those stay on the host as Docker secrets files (or in a secret manager) and are named by `file:` or `env:` references.
  3. Installing the stack on the VM from the public `deploy/compose` (s07-02) plus the private repo's `deploy/` folder, creating the first CISO Assistant admin, and creating the runner's service account and token.
  4. Registering the Microsoft 365 app (linking `docs/collectors/m365.md`) and storing its certificate as a Docker secret.
  5. `scope validate`, `scope plan` and `scope apply` for setup, settings and access, then for the Essential Eight audit.
  6. Turning on the schedule (s07-03) and checking the first run with `hound status` and in CISO Assistant.
  7. Day-2 changes: every change to access, settings, scope or schedules is a pull request; the repo's CI posts `scope plan` and applies on merge.
- The template repo gains `deploy/`, `scope/access.yaml`, `scope/settings.yaml`, a `.gitignore` that excludes secrets and evidence, and a gitleaks step in its CI workflow.
- A test checks that every file path and command in the tutorial exists in the template repo or the CLI, so the tutorial cannot drift from the code.

**Tests first**
- `ExampleRepoTests.Example_compliance_repo_validates` covers `access.yaml`, `settings.yaml` and `deploy/`.
- `TutorialTests.Every_path_and_command_in_tutorial_exists`
- `ExampleRepoTests.Gitignore_excludes_secret_and_evidence_paths`

**Functional check**
- A reviewer follows the tutorial verbatim on a fresh VM with a new private repo and reaches a scheduled run that publishes to the example audit. Record the time taken and every step that needed help.
- Result (2026-11-22): `TutorialTests` and `ExampleRepoTests` pass, and the template's deploy folder validates with the stack. **Owed by a reviewer:** following the tutorial on a fresh VM, which a cloud session cannot do. Evidence: [`docs/evidence/s07/s07-06.md`](../evidence/s07/s07-06.md).

### s07-07 `hound publish` writes the run marker
- [x] Done

**Story:** As the capability owner, I want results published with `hound publish` to be marked as a run's, like those of `hound run`, so that applying the scope after a nightly publish keeps that night's results.

**Acceptance criteria**
- `hound publish` ends each requirement observation it writes with the same `[hound run <id> state <fingerprint>]` marker `hound run` writes, with the fingerprint of the result, its checks' outcomes and its evidence hashes.
- `scope apply` then keeps those rows (they are the runs' own, not `unlisted: not_assessed`), and `scope export` leaves them out (s06 discovered work).
- `publish` still sends every update, as before; only the observation changes.

**Tests first**
- `PlanTests.Planning_every_update_for_a_run_marks_each_observation_as_planning_only_changes_does`
- `PublishCommandTests.Each_result_it_writes_carries_the_run_marker_so_scope_apply_and_export_leave_it_to_the_runs`

**Functional check**
- Publish a run's results with `hound publish`, then `scope plan` on the example scope lists no change to those rows.
- Result (2026-11-22): `PublishFunctionalTests` publishes with `hound publish`, finds the marker on the row, and `scope plan` lists no change to `EXAMPLE-BASELINE-2026/EX-1.1`; passed in [CI run 44975475229](https://github.com/tnbozman/evidence-hound/actions/runs/44975475229). Evidence: [`docs/evidence/s07/s07-07.md`](../evidence/s07/s07-07.md).

## Discovered work

- **Roles are user groups (s07-04).** CISO Assistant v4.3.4 returns HTTP 403 to administrators on POST `/api/role-assignments/`. A role on a domain is membership of that domain's built-in group, so each user carries a `roles` field and apply sets their `user_groups`. The task's "role assignments" wording and its contract test name changed to match. Folders made through the API need `create_iam_groups: true` to get those groups.
- **Translated names.** Roles and user groups are listed by display name in the request's language. hound sends `Accept-Language: en` and maps English names to codes. A future CISO Assistant release that renames a role breaks this; the functional tests would catch it.
- **Gated feature flags (s07-05).** `chat_mode` and `infra_config_management` exist only when the backend sets `ENABLE_CHAT` or `ENABLE_INFRA_CONFIG_MANAGEMENT`. The snapshot keeps them apart and validation reports `settings.flag_gated`. If the stack ever turns either on, it moves back.
- **Runner image time zones (s07-02).** The chiseled runtime has no tzdata; the image copies it from the SDK stage. Check this again when the base image changes.
- **Owed checks:** the overnight 15-minute schedule (s07-03, the person) and the tutorial on a fresh VM (s07-06, a reviewer).

## Sprint review

**2026-11-22.** All seven tasks shipped with red and green commits and evidence in `docs/evidence/s07/`. s07-07 (the run marker for `hound publish`) was added during the sprint at the person's request.

- **One Compose stack** pinned to CISO Assistant v4.3.4, with Caddy TLS, Docker secrets files, a locked-down runner on its own network and an optional Kafka profile. CI's smoke test brings it up, applies the example scope and watches scheduled runs publish.
- **Scheduling** with `hound serve`: cron schedules in a time zone, jitter, no overlapping runs, missed runs caught up once.
- **Access and settings as configuration**: `access.yaml` (users, teams, roles, service accounts, an access review in `scope plan`) and `settings.yaml` (feature flags and general settings, checked offline against a snapshot).
- **A tutorial and template** for an organisation's private compliance repo, with a test that keeps every path and command in the tutorial real.

852 unit and contract tests, 19 functional tests, 17 compose checks, 5 image checks and lint pass.

What slipped: two checks only a person can do, the overnight schedule and the fresh-VM tutorial run. They are listed under Discovered work.

What the feedback taught us:
- The real stack finds what contract tests cannot. Translated role names, the 403 on role assignments, folders without IAM groups and the gated flags all came from CI's functional and smoke runs, each turned into a failing test first.
- Run the image the way it runs in production. The missing time zone database only showed in the image, never in `dotnet test`.
