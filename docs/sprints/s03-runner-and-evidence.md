# s03 Runner, configuration, evidence packaging and run status

Status: In progress

**Goal:** one command runs every configured collector, packages its evidence, maps and publishes results, and reports its own health into CISO Assistant.

Projects: `src/EvidenceHound.Runtime` (runner, stores, config), `src/EvidenceHound.Cli`.

### s03-01 Deployment configuration
- [ ] Done

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

### s03-02 Secret provider
- [ ] Done

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

### s03-03 Evidence store and manifest
- [ ] Done

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

## Discovered work

## Sprint review
