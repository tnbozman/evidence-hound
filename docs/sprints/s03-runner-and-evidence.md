# s03 Runner, configuration, evidence packaging and run status

Status: Done

**Goal:** one command runs every configured collector, packages its evidence, maps and publishes results, and reports its own health into CISO Assistant.

Projects: `src/EvidenceHound.Runtime` (runner, stores, config), `src/EvidenceHound.Cli`.

### s03-01 Deployment configuration
- [x] Done

**Story:** As an operator, I want one config file per deployment, so that nothing organisation-specific lives in code.

**Acceptance criteria**
- Config schema: CISO Assistant URL and token source; evidence root; list of collector instances (name, type, schedule, settings, secret references); targets (audit ref, framework mapping file, control refs per check).
- Secrets are references only (`env:NAME`, `file:/run/secrets/name`), never values; a value that looks like a secret fails validation.
- `examples/example-pty-ltd.yaml` is complete and fictional.
- JSON Schema in `schemas/config.schema.json`; `hound config validate` reports all errors at once.

**Tests first**
- `ConfigTests.Example_config_is_valid`
- `ConfigTests.Inline_secret_value_is_rejected`
- `ConfigTests.Unknown_collector_type_lists_known_types`
- `ConfigTests.All_errors_reported_together`

**Functional check**
- `make run ARGS="config validate examples/example-pty-ltd.yaml"` prints `valid`.
- Result (2026-11-21): prints `valid`, exit 0; a broken config lists all four problems with lines and exits 3 without printing the inline token. [Evidence](https://github.com/tnbozman/evidence-hound/blob/d05f4d042885553cfd94a16a8189b535be63b84e/docs/evidence/s03/s03-01.md).

### s03-02 Secret provider
- [x] Done

**Story:** As an operator, I want secrets resolved at run time from the environment or Docker secrets, so that they are never stored by the tool.

**Acceptance criteria**
- `ISecretProvider` port; adapters for `env:` and `file:`; composite resolves by prefix.
- Missing secret returns `Error("secret_missing")` naming the reference, not the value.
- Resolved secrets are wrapped in a `Secret` type whose `ToString()` is redacted.

**Tests first**
- `SecretProviderTests.Resolves_env_reference`
- `SecretProviderTests.Resolves_file_reference_trimming_newline`
- `SecretProviderTests.Missing_reports_reference_only`
- `SecretTests.ToString_is_redacted`

**Functional check**
- Run the CLI with a missing env var: clear error, exit code 3, no value printed.
- Result (2026-11-21): `hound config validate --check-secrets examples/example-pty-ltd.yaml` without `HOUND_CA_TOKEN` logs `Secret env:HOUND_CA_TOKEN could not be resolved` and exits 3; with it set prints `valid`. [Evidence](https://github.com/tnbozman/evidence-hound/blob/d05f4d042885553cfd94a16a8189b535be63b84e/docs/evidence/s03/s03-02.md).

### s03-03 Evidence store and manifest
- [x] Done

**Story:** As an auditor, I want every run's evidence stored with a hash manifest, so that I can prove what was collected and when.

**Acceptance criteria**
- `IEvidenceStore` writes to `<root>/<collector>/<UTC timestamp>/` with `_manifest.json` (file name, size, SHA-256, media type, collected at, collector version).
- Pure `BuildManifest` function; filesystem adapter is thin.
- Retention setting deletes runs older than N days, never the latest successful run.

**Tests first**
- `ManifestTests.Hash_matches_known_vector`
- `ManifestTests.Manifest_is_deterministic_for_same_inputs`
- `RetentionTests.Keeps_latest_successful_run`
- `FileEvidenceStoreTests.Writes_files_and_manifest` (temp dir)

**Functional check**
- Dry run with the fake collector writes a folder and a manifest you can verify with `sha256sum`.
- Result (2026-11-21): `make run ARGS="collect --config examples/example-pty-ltd.yaml"` wrote `.dev/evidence/example-fake/<timestamp>/` with four files and `_manifest.json`; `sha256sum -c` on the manifest's hashes printed `OK` for all four. [Evidence](https://github.com/tnbozman/evidence-hound/blob/d05f4d042885553cfd94a16a8189b535be63b84e/docs/evidence/s03/s03-03.md).

### s03-04 Runner pipeline
- [x] Done

**Story:** As an operator, I want `hound run` to execute collect, evaluate, map, plan and publish for each collector, so that one command keeps audits current.

**Acceptance criteria**
- Pipeline per collector instance composed of pure steps plus the I/O steps from earlier sprints.
- One collector failing does not stop others.
- `--only <name>`, `--dry-run`, `--no-publish` options.
- Exit codes: 0 all ok, 1 any collector failed, 2 usage, 3 configuration or secrets.

**Tests first**
- `RunnerTests.Runs_each_configured_collector`
- `RunnerTests.Failure_in_one_collector_continues_others`
- `RunnerTests.Exit_code_reflects_worst_outcome`
- Functional: `Run_with_fake_collector_updates_example_audit`

**Functional check**
- `make ca-up && make run ARGS="run --config examples/example-pty-ltd.yaml"` updates the example audit with the fake collector's results.
- Result (2026-11-21): the CI functional test ran `hound run` against CISO Assistant v4.3.4: EX-1.1 non_compliant with two evidences, EX-3.1 compliant, exit 0. The dry run planned 8 updates. [Evidence](https://github.com/tnbozman/evidence-hound/blob/d05f4d042885553cfd94a16a8189b535be63b84e/docs/evidence/s03/s03-04.md).

### s03-05 Run status and collector health
- [x] Done

**Story:** As the security committee, I want to see in CISO Assistant whether each collector ran and when, so that stale evidence is visible without another tool.

**Acceptance criteria**
- Every run produces a `RunRecord` (start, end, outcome, checks passed and failed, publish report).
- The runner publishes a "collector health" evidence item per collector instance with the latest run record as JSON, linked to a configurable applied control (for example "Automated evidence collection").
- `hound status` prints the latest run per collector from the local store.

**Tests first**
- `RunRecordTests.Summarises_outcomes`
- `HealthTests.Health_evidence_planned_after_each_run`
- `StatusCommandTests.Shows_latest_run_per_collector`

**Functional check**
- After `make run ARGS="run ..."`, the example audit's control shows a fresh "collector health" evidence revision.
- Result (2026-11-21): in CI, `hound-health/example-fake` was linked to AC-AUTO-EVIDENCE and a second run added one new health revision. [Evidence](https://github.com/tnbozman/evidence-hound/blob/d05f4d042885553cfd94a16a8189b535be63b84e/docs/evidence/s03/s03-05.md).

### s03-06 Dev container with CISO Assistant on one network (added during the sprint)
- [x] Done

**Story:** As a developer, I want the dev container and a seeded CISO Assistant on the same Docker network, so that I can develop and run `hound` against CISO Assistant without starting it by hand.

**Acceptance criteria**
- The dev container is defined by Docker Compose (`.devcontainer/compose.yml`): a `dev` service plus the CISO Assistant backend, frontend and Caddy from `dev/ciso-assistant/compose.yml`, on one network. Redpanda and the dispatcher stay behind the `kafka` profile.
- A one-shot `ca-seed` service waits for the backend health check, seeds it and writes `HOUND_CA_URL=http://backend:8000/api/` and a local-only token to a shared volume. The token never appears in logs.
- On start, the dev container waits (with a timeout) for that file and copies it to `.dev/ca.env`, so `hound` reaches CISO Assistant by service name.
- The Django secret key is generated on the host before the containers start and is never committed. Docker-in-docker stays for Testcontainers.

**Tests first**
- `ca-env.bats`: writes the URL and token with mode 600 and hides the token; fails without a token.
- `dev-ca-env.bats`: copies the shared file into `.dev/ca.env`; waits for it; times out with a message.
- `devcontainer-compose.bats`: the compose model has the services on one network, `HOUND_CA_URL` on `dev`, and Kafka only under the profile.

**Functional check**
- Rebuild the dev container, then `set -a && . .dev/ca.env && set +a && make run ARGS="run --config examples/example-pty-ltd.yaml"` publishes to `backend:8000`. The person runs this (this cloud box cannot run the dev container).
- Result (2026-11-21): compose model, seed env file, start-up wait and secret key covered by 18 bats tests that pass in CI; the rebuild check is listed in the [evidence](https://github.com/tnbozman/evidence-hound/blob/d05f4d042885553cfd94a16a8189b535be63b84e/docs/evidence/s03/s03-06.md).

## Discovered work

- s03-01: the config keeps s01's single `target` (mapping, `compliance_assessment`, `controls`) rather than a list of targets; "control refs per check" are the existing control keys. Add a list when a deployment needs two audits.
- s03-02: the problem code is `secret.missing`, dotted like the other codes. `hound config validate --check-secrets` resolves every reference before a run.
- s03-03: `ICollector` (with `Version`) and `CollectorOutput` are minimal: check results cite evidence by file name. s04's SDK and shared collector contract tests build on them. `hound collect` now stores evidence; `--dry-run` still contacts nothing.
- s03-01: the only collector type is `fake`; s04's SDK should replace `CollectorTypes.BuiltIn` with a registry the CLI builds.
- s03-04: a collector only publishes requirements it fully owns (every mapped check reported). A requirement fed by two collectors is not updated by either; merging each collector's latest stored results before planning would fix that when a mapping needs it.
- s03-04: CISO Assistant needs evidence names unique within a folder, so evidence is named by `EvidenceNames.For` (the collector-scoped ID, or `file name (ID)`). Evidence created earlier keeps its old name, because it is found by `ref_id`.
- s03-04: exit code 1 means both "a collector failed" and "unexpected error". Split them if a scheduler needs to tell them apart.
- s03-05: over the Kafka transport, health evidence is uploaded but not linked to `health_control` (the dispatcher cannot link evidence to controls), as with requirement evidence in s02.
- s03-06: not tried in GitHub Codespaces, where the UI port is not published to the host. If the `ca-seed` user cannot write to the fresh `hound-ca-env` volume, give the volume an owner. Kafka in the dev container needs `HOUND_CA_TOKEN` at compose time, so it is started from the host (`--profile kafka`) after the first seed.

## Sprint review

**2026-11-21.** All six tasks shipped (s03-06 was added during the sprint) with red, green and refactor commits and per-task evidence in `docs/evidence/s03/`. A deployment is now one YAML file with secret references, validated with file and line numbers. `hound run` collects each configured collector into a local evidence store with a SHA-256 manifest, evaluates, maps, plans and publishes, isolating failures per collector. Every run leaves a run record, `hound status` shows the latest, and "collector health" evidence linked to an applied control shows in CISO Assistant when each collector last ran. The dev container now runs on one Docker network with a seeded CISO Assistant. 330 unit and contract tests, 38 script tests, 7 functional tests, lint clean.

What slipped: nothing was dropped. The s03-06 rebuild check needs a developer's machine (steps in the s03-06 evidence). Deviations are listed under "Discovered work".

What the feedback taught us:
- The real CISO Assistant rejects two evidences with the same name in a folder; WireMock contract tests could not show it. Evidence is now named by its collector-scoped ID.
- gitleaks flags random-looking fake tokens in tests. Use obviously fake, low-entropy stand-ins rather than working around the scanner.
- PR CI cancels the running functional job on every push, so batch pushes while a real-instance run is in flight.
- `docker compose config` checks a compose model without a daemon, so the dev container's network and seed wiring are unit-tested in the scripts job.
- An `include` entry with an explicit `env_file` fails when the file is missing; the including project's `.env` already reaches included files.
