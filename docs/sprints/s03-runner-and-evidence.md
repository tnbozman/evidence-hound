# s03 Runner, configuration, evidence packaging and run status

Status: In progress

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
- Result (2026-11-21): prints `valid`, exit 0; a broken config lists all four problems with lines and exits 3 without printing the inline token. [Evidence](../evidence/s03/s03-01.md).

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
- Result (2026-11-21): `hound config validate --check-secrets examples/example-pty-ltd.yaml` without `HOUND_CA_TOKEN` logs `Secret env:HOUND_CA_TOKEN could not be resolved` and exits 3; with it set prints `valid`. [Evidence](../evidence/s03/s03-02.md).

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
- Result (2026-11-21): `make run ARGS="collect --config examples/example-pty-ltd.yaml"` wrote `.dev/evidence/example-fake/<timestamp>/` with four files and `_manifest.json`; `sha256sum -c` on the manifest's hashes printed `OK` for all four. [Evidence](../evidence/s03/s03-03.md).

### s03-04 Runner pipeline
- [ ] Done

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

### s03-05 Run status and collector health
- [ ] Done

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

### s03-06 Dev container with CISO Assistant on one network (added during the sprint)
- [ ] Done

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

## Discovered work

- s03-01: the config keeps s01's single `target` (mapping, `compliance_assessment`, `controls`) rather than a list of targets; "control refs per check" are the existing control keys. Add a list when a deployment needs two audits.
- s03-02: the problem code is `secret.missing`, dotted like the other codes. `hound config validate --check-secrets` resolves every reference before a run.
- s03-03: `ICollector` (with `Version`) and `CollectorOutput` are minimal: check results cite evidence by file name. s04's SDK and shared collector contract tests build on them. `hound collect` now stores evidence; `--dry-run` still contacts nothing.
- s03-01: the only collector type is `fake`; s04's SDK should replace `CollectorTypes.BuiltIn` with a registry the CLI builds.

## Sprint review
