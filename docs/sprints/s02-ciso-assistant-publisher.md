# s02 CISO Assistant client and publisher

Status: Done

**Goal:** send a planned list of audit updates to a real CISO Assistant instance, safely and repeatably.

Projects: `src/EvidenceHound.CisoAssistant` (adapter), `tests/EvidenceHound.CisoAssistant.Tests` (contract tests with WireMock.Net), `tests/EvidenceHound.Functional.Tests` (Testcontainers).

### s02-01 Local CISO Assistant for tests
- [x] Done

**Story:** As a developer, I want a disposable CISO Assistant, so that functional tests and manual checks hit the real API.

**Acceptance criteria**
- `dev/ciso-assistant/compose.yml` runs the CISO Assistant backend and frontend images pinned to one release tag, with SQLite in a named volume; `make ca-up` / `make ca-down`.
- A seeding script creates a superuser, a service-account user and a personal access token, loads the Essential Eight library and creates an example folder, perimeter and audit for "Example Pty Ltd". It writes the token to `.dev/ca.env` (git-ignored).
- `CisoAssistantFixture` (Testcontainers) does the same for functional tests and is shared across the test collection.

**Tests first**
- `CisoAssistantFixtureTests.Health_endpoint_returns_ok`
- `CisoAssistantFixtureTests.Token_can_list_folders`

**Functional check**
- `make ca-up`, open `https://localhost:8443`, log in as the seeded user, see the example audit.
- Result (2026-11-21): CI `functional` job green, fixture tests pass against v4.3.4. Browser check with `make ca-up` is left for the maintainer; steps in [evidence](../evidence/s02/s02-01.md).

### s02-02 API client port and adapter
- [x] Done

**Story:** As the publisher, I want a narrow client interface, so that the rest of the code never builds HTTP requests.

**Acceptance criteria**
- Port in core: `ICisoAssistantClient` with `FindRequirementAssessment`, `UpdateRequirementAssessment`, `FindOrCreateEvidence`, `UploadEvidenceRevision`, `UpdateAppliedControl`, `CheckHealth`; all return `Task<Result<T>>`.
- Adapter uses typed `HttpClient` with `Authorization: Token <pat>`, base URL from config, resilience handler (retry with jitter on 5xx and 429, timeout, circuit breaker).
- Lookups use the same selector semantics as the dispatcher (`ref_id`, `name`, `compliance_assessment__ref_id` + `requirement__ref_id`).
- 401 maps to `Error("auth")`, 404 to `Error("not_found")`, 4xx validation to `Error("rejected")` with the API's message.
- The token never appears in logs (redacting handler).

**Tests first**
- `ClientContractTests.Sends_token_header`
- `ClientContractTests.Retries_on_503_then_succeeds`
- `ClientContractTests.Maps_401_to_auth_error`
- `ClientContractTests.Selector_builds_expected_query_string`
- `LoggingTests.Token_is_redacted_in_logs`

**Functional check**
- Functional test `Client_updates_requirement_assessment_on_real_instance` passes against the fixture.
- Result (2026-11-21): `Client_updates_requirement_assessment_on_real_instance` passes in CI. [Evidence](../evidence/s02/s02-02.md).

### s02-03 Publisher
- [x] Done

**Story:** As an operator, I want planned updates sent in order with a clear report, so that I know exactly what changed in CISO Assistant.

**Acceptance criteria**
- `Publisher.PublishAsync(IReadOnlyList<AuditUpdate>) -> PublishReport` (counts sent, skipped, failed, with per-item errors).
- Idempotent: re-running the same plan creates no duplicate evidence and leaves results unchanged (uses content hash in evidence name or metadata).
- `--dry-run` produces the report without calling the API.
- Continues after a failed item; overall exit code non-zero if any failed.

**Tests first**
- `PublisherTests.Dry_run_calls_nothing`
- `PublisherTests.Failed_item_does_not_stop_others`
- `PublisherTests.Same_evidence_twice_uploads_once`
- Functional: `Publish_example_plan_twice_is_idempotent`

**Functional check**
- `make run ARGS="publish --config examples/example-pty-ltd.yaml --plan tests/fixtures/plan-sample.json"` against `make ca-up`; open the example audit and see results and evidence; run again and see no duplicates.
- Result (2026-11-21): `make run ARGS="publish --config examples/example-pty-ltd.yaml --checks tests/fixtures/publish/checks.json --evidence-dir tests/fixtures/publish/evidence --dry-run"` exits 0 with 9 planned; `Publish_example_plan_twice_is_idempotent` passes in CI (second run 3 skipped, 0 failed, no duplicate evidence). [Evidence](../evidence/s02/s02-03.md).

### s02-04 Optional Kafka transport
- [x] Done

**Story:** As an operator already running Kafka, I want to publish through CISO Assistant's official dispatcher, so that I reuse its pipeline.

**Acceptance criteria**
- `IAuditUpdateSink` port with two adapters: `RestSink` (default) and `KafkaSink` (Confluent.Kafka) writing dispatcher JSON to the `observation` topic.
- Chosen by config; Kafka package referenced only by the `EvidenceHound.Transport.Kafka` project.

**Tests first**
- `KafkaSinkTests.Writes_dispatcher_json_to_observation_topic` (Testcontainers Redpanda)

**Functional check**
- Optional compose profile `kafka` runs Redpanda and the dispatcher; one plan publishes end to end.
- Result (2026-11-21): `KafkaSinkTests` pass locally and in CI with Redpanda. The dispatcher end-to-end run is left for the maintainer; steps in [evidence](../evidence/s02/s02-04.md).

## Discovered work

- s02-01: this cloud dev box cannot pull CISO Assistant images (ghcr blob storage is blocked), so functional tests against CISO Assistant run in CI only; the Kafka tests (Redpanda from Docker Hub) run locally too. In the dev container both work.
- s02-01: the seed loads a fictional "Example baseline" library (`dev/ciso-assistant/seed/example-baseline.yaml`) whose requirement ref_ids match `mappings/example.yaml`, so the example plan publishes end to end. The Essential Eight library is loaded too, for s08.
- s02-01: `make test` and `make coverage` pass `-p:UnitTestRun=true` so only the functional project may run zero tests; `make test-functional` runs the functional project alone and must run at least one test (the s00 `--ignore-exit-code 8` escape hatch is gone).
- s02-02: method names carry the `Async` suffix (`FindRequirementAssessmentAsync`, ...). Problem codes are `ciso_assistant.auth`, `.not_found`, `.rejected`, `.ambiguous` and `.unavailable`; 403 maps to `auth` like 401.
- s02-02: CISO Assistant's evidence API has no `ref_id` filter (the dispatcher's `ref_id` selector is silently ignored there), so the client searches with `?search=` and matches `ref_id` exactly.
- s02-02: new evidence goes to CISO Assistant's root folder. Put it in the audit's folder once the config names folders (s03), so a service account scoped to one domain can publish.
- s02-03: `hound publish` takes `--checks` and `--evidence-dir` and plans itself, rather than reading a `--plan` file: re-planning is pure and cheap, and a dry-run plan carries no file content. Evidence files are read by file name from one folder; s03's evidence store decides the real layout.
- s02-03: the URL and token come from `HOUND_CA_URL` and `HOUND_CA_TOKEN` (.NET configuration, so environment variables), not the config file. s03's full config schema should add `ciso_assistant.url` and transport settings.
- s02-03: idempotency compares the evidence's latest revision `attachment_hash` (CISO Assistant's SHA-256 of the file) with the recorded SHA-256, so a changed file becomes a new revision of the same evidence, never a new evidence.
- s02-04: Kafka requirement messages leave out `evidences` because the dispatcher PATCHes values unchanged and cannot resolve ref_ids. Upstream publishes no dispatcher image; the `kafka` compose profile builds it from the release tag.
- s02-04: the end-to-end Kafka functional check (`make ca-up-kafka`, publish with `HOUND_TRANSPORT=kafka`) needs a machine that can pull CISO Assistant images; see the s02-04 evidence for the steps.

## Sprint review

**2026-11-21.** All four tasks shipped with red, green and refactor commits and per-task evidence in `docs/evidence/s02/`. `hound publish` now plans from check results and sends the plan to CISO Assistant over REST (default) or through the official dispatcher over Kafka. The REST client sits behind a core port, retries safely, maps API errors to problem codes and never logs the token. Publishing is idempotent: evidence is matched by `ref_id` and a file is uploaded only when its SHA-256 differs from the latest revision. Functional tests start a seeded CISO Assistant with Testcontainers and publish the example plan twice; `make ca-up` gives the same instance for manual checks. 198 unit and contract tests, 6 functional tests, lint clean.

What slipped: nothing was dropped. Two functional checks need the maintainer's dev container (the browser check of the seeded audit and the Kafka end-to-end run), with steps in the s02-01 and s02-04 evidence. Deviations are listed under "Discovered work".

What the feedback taught us:
- A red commit that does not compile cannot pass the pre-commit format hook; stub production types that throw `NotImplementedException` keep red tests compiling and failing for the right reason.
- Microsoft.Testing.Platform exits 8 when a project runs zero tests, and passing `--ignore-exit-code` twice is itself an error; scope the escape hatch to one project.
- The CISO Assistant v4.3.4 backend can export an empty secret key from its own startup script; pass `DJANGO_SECRET_KEY` explicitly, and show the container log when a health wait times out.
- CA1711 rejects type names ending in `Collection`.
- WireMock accepted chunked JSON bodies but CISO Assistant silently ignores them; only the real-instance tests caught it. `JsonContent` is chunked, so send `StringContent`.
