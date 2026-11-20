# s02 CISO Assistant client and publisher

Status: Planned

**Goal:** send a planned list of audit updates to a real CISO Assistant instance, safely and repeatably.

Projects: `src/EvidenceHound.CisoAssistant` (adapter), `tests/EvidenceHound.CisoAssistant.Tests` (contract tests with WireMock.Net), `tests/EvidenceHound.Functional.Tests` (Testcontainers).

### s02-01 Local CISO Assistant for tests
- [ ] Done

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

### s02-02 API client port and adapter
- [ ] Done

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

### s02-03 Publisher
- [ ] Done

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

### s02-04 Optional Kafka transport
- [ ] Done

**Story:** As an operator already running Kafka, I want to publish through CISO Assistant's official dispatcher, so that I reuse its pipeline.

**Acceptance criteria**
- `IAuditUpdateSink` port with two adapters: `RestSink` (default) and `KafkaSink` (Confluent.Kafka) writing dispatcher JSON to the `observation` topic.
- Chosen by config; Kafka package referenced only by the `EvidenceHound.Transport.Kafka` project.

**Tests first**
- `KafkaSinkTests.Writes_dispatcher_json_to_observation_topic` (Testcontainers Redpanda)

**Functional check**
- Optional compose profile `kafka` runs Redpanda and the dispatcher; one plan publishes end to end.

## Discovered work

## Sprint review
