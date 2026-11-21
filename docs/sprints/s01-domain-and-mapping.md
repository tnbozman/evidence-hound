# s01 Domain model and framework mapping

Status: In progress

**Goal:** the pure core that turns framework-neutral check results into requirement results for any framework, with no I/O.

All code in this sprint lives in `src/EvidenceHound.Core` and is tested in `tests/EvidenceHound.Core.Tests`. No package references beyond the BCL.

### s01-01 Result type
- [x] Done

**Story:** As a developer, I want a small `Result<T>` type, so that expected failures are values, not exceptions.

**Acceptance criteria**
- `Result<T>` is either `Ok(T)` or `Fail(Error)`; `Error` is a record with `Code` and `Message`.
- `Map`, `Bind`, `Match` and `Combine` (list of results to result of list, collecting all errors).
- Immutable; no public setters; no nulls.

**Tests first**
- `ResultTests.Map_on_ok_applies_function`
- `ResultTests.Map_on_fail_keeps_error`
- `ResultTests.Bind_short_circuits_on_first_fail`
- `ResultTests.Combine_collects_all_errors`
- Property: `Map(id) == id` for any result (FsCheck).

**Functional check**
- `make test` green; coverage of `Result` 100 %.

**Functional check result (2026-11-21):** `make coverage` gave `total: 16, failed: 0`; Cobertura shows 100 % line and branch coverage for `Result`, `Result<T>` and `Problem`. Evidence: [`docs/evidence/s01/s01-01.md`](../evidence/s01/s01-01.md).

### s01-02 Check results and evidence
- [x] Done

**Story:** As a collector author, I want standard types for what a collector reports, so that every collector speaks the same language.

**Acceptance criteria**
- `CheckId` value type: lowercase dot-separated segments (`identity.mfa.admins`); invalid IDs rejected by a `Create` returning `Result`.
- `CheckOutcome`: `Pass`, `Fail`, `Partial`, `NotApplicable`, `Error`.
- `CheckResult` record: `CheckId`, `Outcome`, `Observation` (short, plain text), `EvidenceRefs`, `CollectedAt`, `Source` (collector name).
- `Evidence` record: `Id`, `FileName`, `MediaType`, `Sha256`, `SizeBytes`, `CollectedAt`.
- No type mentions any framework.

**Tests first**
- `CheckIdTests.Accepts_lowercase_dotted_segments`
- `CheckIdTests.Rejects_empty_uppercase_or_trailing_dot`
- `CheckResultTests.Observation_longer_than_2000_chars_is_rejected`
- `EvidenceTests.Sha256_must_be_64_hex_chars`

**Functional check**
- `make test` green.

**Functional check result (2026-11-21):** `make test` gave `total: 45, failed: 0`; lint clean. Evidence: [`docs/evidence/s01/s01-02.md`](../evidence/s01/s01-02.md).

### s01-03 Framework mapping model and loader
- [x] Done

**Story:** As a compliance owner, I want to describe in a file how checks satisfy a framework's requirements, so that adding a framework needs no code.

**Acceptance criteria**
- `FrameworkMapping` record: `FrameworkUrn` (CISO Assistant library URN), `Requirements` list, each with `RequirementRef`, `Checks` (list of `CheckId`) and `Rule` (`AllPass`, `AnyPass`, `Threshold(n)`).
- YAML loader (YamlDotNet, in an adapter project `EvidenceHound.Core.Yaml` so the core stays dependency-free) parses `mappings/*.yaml` into `Result<FrameworkMapping>` with line-numbered errors.
- Unknown check IDs are allowed (warn), duplicate requirement refs are rejected.
- A JSON Schema for the mapping file is published in `schemas/mapping.schema.json` and validated in tests.

**Tests first**
- `MappingLoaderTests.Loads_valid_example_mapping`
- `MappingLoaderTests.Duplicate_requirement_ref_fails_with_line_number`
- `MappingLoaderTests.Unknown_rule_fails`
- `MappingSchemaTests.Example_mapping_validates_against_schema`

**Functional check**
- `make run ARGS="mapping validate mappings/example.yaml"` prints `valid: N requirements`.

**Functional check result (2026-11-21):** `make run ARGS="mapping validate mappings/example.yaml"` printed `valid: 4 requirements`; a broken file printed each problem with its line and exited 3. `make test`: `total: 85, failed: 0`. Evidence: [`docs/evidence/s01/s01-03.md`](../evidence/s01/s01-03.md).

### s01-04 Mapping engine
- [ ] Done

**Story:** As a compliance owner, I want check results turned into requirement results, so that CISO Assistant audits reflect what collectors found.

**Acceptance criteria**
- Pure function `ApplyMapping(FrameworkMapping, IReadOnlyList<CheckResult>) -> IReadOnlyList<RequirementResult>`.
- `RequirementResult`: `RequirementRef`, `Result` (`Compliant`, `PartiallyCompliant`, `NonCompliant`, `NotAssessed`), `Observation` (generated, lists contributing checks and their outcomes), `EvidenceRefs` (union).
- Rules: `AllPass` gives Compliant only if every mapped check passed; any `Error` gives `NotAssessed`; missing checks give `NotAssessed`; `NotApplicable` checks are ignored unless all are N/A.
- Latest result per check wins when duplicates exist.
- Deterministic ordering of output.

**Tests first**
- `MappingEngineTests.All_pass_rule_all_pass_is_compliant`
- `MappingEngineTests.All_pass_rule_one_fail_is_non_compliant`
- `MappingEngineTests.Any_pass_rule_mixed_is_partially_compliant`
- `MappingEngineTests.Error_outcome_gives_not_assessed`
- `MappingEngineTests.Missing_check_gives_not_assessed`
- `MappingEngineTests.Latest_duplicate_check_wins`
- Property: output order is independent of input order.

**Functional check**
- `make run ARGS="mapping apply mappings/example.yaml tests/fixtures/checks-sample.json"` prints a table of requirement results.

### s01-05 Audit update plan
- [ ] Done

**Story:** As the publisher, I want a plan of exactly which audit updates to send, so that publishing is a dumb loop over a pure plan.

**Acceptance criteria**
- `AuditUpdate` is a closed set of records: `UploadEvidence`, `UpdateRequirementAssessment`, `UpdateAppliedControl`, matching the dispatcher message types field for field.
- Pure function `PlanUpdates(target, requirementResults, evidence, controlStatuses) -> IReadOnlyList<AuditUpdate>`.
- `target` identifies the audit and controls by configuration keys (never hard-coded IDs).
- Evidence uploads come before the requirement updates that reference them.

**Tests first**
- `PlanTests.Evidence_uploads_precede_requirement_updates`
- `PlanTests.One_update_per_requirement_result`
- `PlanTests.Serialises_to_dispatcher_message_shape` (golden JSON file per message type)

**Functional check**
- `make run ARGS="plan --dry-run --config examples/example-pty-ltd.yaml --checks tests/fixtures/checks-sample.json"` prints the planned messages as JSON.

## Discovered work

- s01-01: the error record is `Problem`, not `Error`, because CA1716 rejects `Error` (a Visual Basic keyword). Later sprint files that say `Error` mean `Problem`.
- s01-03: "unknown check IDs warn" needs a catalogue of known check IDs. The loader takes an optional known set and returns warnings, but nothing supplies one yet, so `hound mapping validate` cannot warn. Wire it up when collectors declare their checks (s04 collector SDK).

## Sprint review
