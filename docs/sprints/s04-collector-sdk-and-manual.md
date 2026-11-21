# s04 Collector SDK, contract test kit and manual-upload collector

Status: In progress

**Goal:** anyone can write a collector for their system in about a day, and systems without APIs are covered from day one.

Projects: `src/EvidenceHound.Collectors.Abstractions` (published package), `src/EvidenceHound.Collectors.Testing` (published test kit), `src/Collectors/EvidenceHound.Collectors.Manual`, `templates/collector`.

### s04-01 Collector interface
- [ ] Done

**Story:** As a collector author, I want a small, stable interface, so that I only write code that talks to my target system.

**Acceptance criteria**
- `ICollector` with `Type`, `Describe()` (supported check IDs and required settings) and `CollectAsync(CollectorContext, CancellationToken) -> Task<Result<CollectionResult>>`.
- `CollectionResult` holds evidence files (streams plus metadata) and `CheckResult`s.
- `CollectorContext` provides typed settings, resolved secrets, `IClock`, logger and an `HttpClient` factory; nothing else.
- Registration through `services.AddCollector<T>()`; the runner discovers collectors from DI, not reflection over assemblies.

**Tests first**
- `CollectorRegistryTests.Registered_collector_is_resolved_by_type`
- `CollectorRegistryTests.Duplicate_type_fails_at_startup`
- `DescribeTests.Config_validation_uses_described_settings`

**Functional check**
- `make run ARGS="collectors list"` shows the fake and manual collectors with their check IDs.

### s04-02 Contract test kit
- [ ] Done

**Story:** As a collector author, I want a ready-made test suite every collector must pass, so that all collectors behave consistently.

**Acceptance criteria**
- `CollectorContractTests<TCollector>` abstract xUnit class: describes at least one check; every emitted check ID is declared in `Describe()`; evidence has hashes; honours cancellation; returns `Fail` (not throws) on auth error; never logs secrets.
- Recorded-response helpers built on WireMock.Net.
- Shipped as the `EvidenceHound.Collectors.Testing` package.

**Tests first**
- Apply the kit to the fake collector: all contract tests pass.
- A deliberately broken fake (undeclared check ID) fails the right contract test.

**Functional check**
- `make test` runs the contract suite for every collector in the solution.

### s04-03 Collector template
- [ ] Done

**Story:** As a contributor, I want `dotnet new hound-collector`, so that a new collector starts with structure, tests and docs.

**Acceptance criteria**
- `templates/collector` is a `dotnet new` template producing a collector project, a test project using the contract kit, a README with setup steps, and a sample recorded response.
- The generated project builds and its tests pass unmodified.

**Tests first**
- CI job: instantiate the template into a temp folder, build and test it.

**Functional check**
- `dotnet new hound-collector -n Example.Widget` then `make test` passes.

### s04-04 Manual-upload collector
- [ ] Done

**Story:** As a system owner of a system with no API, I want to drop an export file and have it treated like collected evidence, so that manual systems are part of continuous compliance.

**Acceptance criteria**
- Config per manual source: drop folder, accepted file patterns, check IDs it supports, due interval (for example 30 days), owner.
- Each run picks up new files, hashes them, emits evidence and a check result per configured check: `Pass` when an owner-attested file is present within the interval, `Error` with "overdue since <date>" when not.
- An optional attestation sidecar (`<file>.attest.yaml`: who, date, outcome per check, note) sets the outcome; without it outcome is `Partial` ("evidence present, not attested").
- `hound manual upload --source <name> <file>` copies a file into the drop folder with an attestation prompt.

**Tests first**
- `ManualCollectorTests.New_file_with_attestation_passes`
- `ManualCollectorTests.File_without_attestation_is_partial`
- `ManualCollectorTests.No_file_within_interval_is_overdue_error`
- `ManualCollectorTests.Same_file_twice_is_not_reuploaded`
- Contract kit passes.

**Functional check**
- Configure a manual source "Example firewall config", upload a fictional config export with attestation, run, and see evidence and result in the example audit.

## Discovered work

## Sprint review
