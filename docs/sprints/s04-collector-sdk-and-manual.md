# s04 Collector SDK, contract test kit and manual-upload collector

Status: Done

**Goal:** anyone can write a collector for their system in about a day, and systems without APIs are covered from day one.

Projects: `src/EvidenceHound.Collectors.Abstractions` (published package), `src/EvidenceHound.Collectors.Testing` (published test kit), `src/Collectors/EvidenceHound.Collectors.Manual`, `templates/collector`.

### s04-01 Collector interface
- [x] Done

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
- Result (2026-11-21): lists `fake 1.0.0` with its four check IDs, setting `tenant (optional)` and no secrets, exit 0. `manual` appears once s04-04 adds it. [Evidence](https://github.com/tnbozman/evidence-hound/blob/d05f4d042885553cfd94a16a8189b535be63b84e/docs/evidence/s04/s04-01.md).

### s04-02 Contract test kit
- [x] Done

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
- Result (2026-11-21): `make test` passes 362 of 362 (0 skipped), including the six `FakeCollectorContractTests` contracts and `ContractCoverageTests.Every_built_in_collector_has_contract_tests`; making the fake's contract class non-public fails the coverage test, naming `FakeCollector`. [Evidence](https://github.com/tnbozman/evidence-hound/blob/d05f4d042885553cfd94a16a8189b535be63b84e/docs/evidence/s04/s04-02.md).

### s04-03 Collector template
- [x] Done

**Story:** As a contributor, I want `dotnet new hound-collector`, so that a new collector starts with structure, tests and docs.

**Acceptance criteria**
- `templates/collector` is a `dotnet new` template producing a collector project, a test project using the contract kit, a README with setup steps, and a sample recorded response.
- The generated project builds and its tests pass unmodified.

**Tests first**
- CI job: instantiate the template into a temp folder, build and test it.

**Functional check**
- `dotnet new hound-collector -n Example.Widget` then `make test` passes.
- Result (2026-11-21): the template was installed and `Example.Widget` was generated in the repo root against locally packed SDK packages (`--HoundVersion 0.1.0-local.1`). Its own `dotnet test` passed 8 of 8 (six contracts and two behaviour tests). The repo's `make test` passed 362 of 362 and `make lint-dotnet` was clean with the generated folder present. `make test-template` passes. [Evidence](https://github.com/tnbozman/evidence-hound/blob/d05f4d042885553cfd94a16a8189b535be63b84e/docs/evidence/s04/s04-03.md).

### s04-04 Manual-upload collector
- [x] Done

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
- Result (2026-11-21): the example config plus manual source `example-firewall-config` passed `hound config validate`. `hound manual upload` copied `examples/manual-exports/example-firewall-config-export.txt` and wrote its attestation; a second upload copied nothing. `hound run` against a local CISO Assistant published 8 updates for the fake and 3 for the manual source. In audit `EXAMPLE-BASELINE-2026`, the new fictional requirement EX-4.1 reads `compliant`, with the attestation in its observation, and the export and its sidecar are linked as evidence. `make test-functional` passed 7 of 7. [Evidence](https://github.com/tnbozman/evidence-hound/blob/d05f4d042885553cfd94a16a8189b535be63b84e/docs/evidence/s04/s04-04.md).

## Discovered work

- `scripts/start-sprint.sh`: the open-sprint check (`gh pr list ... || true`) passes silently when `gh` is not logged in, so the script pushes the branch and fails only at `gh pr create`. Check `gh auth status` first. (Found starting s04.)
- `PublishCommandTests.Without_a_url_exits_three` fails when `HOUND_CA_URL` is set in the environment, as it is in the dev container. The CLI tests should not read the developer's environment. (Found in s04-01.)
- Config validation reports missing required settings but not unknown ones; a typo in an optional setting name goes unnoticed. Consider `config.setting_unknown` once every collector describes its settings. (Found in s04-01.)
- `CollectStep` reads each evidence stream into memory before hashing and storing it. Stream to the store for large exports (manual collector, s04-04). (Found in s04-01.)
- The contract kit does not yet check that check results use the context's instance name as `Source` and the context's clock for `CollectedAt`. Consider adding both contracts. (Found in s04-02.)
- Nothing publishes the `Collectors.Abstractions` and `Collectors.Testing` packages yet; `dotnet pack` works locally. Add a pack-and-publish job before the template (s04-03) refers to them by package. (Found in s04-02.) Update from s04-03: `EvidenceHound.Core` must be published too, because the Abstractions package depends on it. Until all three are on a feed, a collector generated with the default `--HoundVersion 0.1.0` cannot restore outside `make test-template`.
- The runner registers only built-in collectors (`AddBuiltInCollectors`). There is no way yet to load a collector built from the template into `hound` without editing the CLI. Decide how plugins are loaded: a package reference in a custom host, or assembly loading from config. (Found in s04-03.)
- `make ca-up` fails at the seed step: the Makefile passes `HOUND_CA_SECRET_KEY` to `docker compose up` but not to `scripts/ca-seed.sh`, whose `docker compose exec` needs it to read the compose file. Workaround: `HOUND_CA_SECRET_KEY="$(scripts/ca-secret-key.sh)" scripts/ca-seed.sh`. (Found in s04-04.)
- A manual source whose drop folder does not exist yet fails its run (`manual.drop_folder_missing`) until the first upload creates it. Consider reporting `error: no evidence uploaded yet` instead, so a new source shows as overdue rather than failing. (Found in s04-04.)
- Config validation does not check the check IDs in a `ChecksSetting` setting, such as the manual collector's `checks`. A bad ID is reported only when the collector runs. (Found in s04-04.)
- Attestations are not checked against the source's `owner`: anyone can be named in `who`. Decide whether only the owner may attest. (Found in s04-04.)

## Sprint review

**2026-11-21.** All four tasks shipped with red, green and refactor commits and per-task evidence in `docs/evidence/s04/`.

- **Collector SDK.** A collector implements a small `ICollector`, registers with `services.AddCollector<T>()`, and describes its checks, settings and secrets; config is validated against that description.
- **Contract kit and template.** `EvidenceHound.Collectors.Testing` gives every collector the same six contracts and WireMock-based recorded responses. `dotnet new hound-collector` produces a collector, its contract-tested test project, a README and a sample response, and CI builds and tests a generated one.
- **Manual-upload collector.** Systems without an API are covered: a dropped, optionally attested export becomes evidence and a pass, partial or overdue result in CISO Assistant, and `hound manual upload` adds files with an attestation prompt.

397 unit and contract tests, 38 script tests, 7 functional tests, the template job and lint all pass locally.

What slipped: nothing was dropped. Two items are needed before outside collectors are practical: publishing the SDK packages, and loading collectors that are not built in. Both are under "Discovered work".

What the feedback taught us:
- The template job found that the Abstractions package depends on an unpacked `EvidenceHound.Core`. Generating and building a consumer project catches packaging gaps that in-repo project references hide.
- WireMock.Net brings Humanizer 2.x, which clashes with JsonSchema.Net's Humanizer.Core 3.x. Humanizer is pinned centrally to 3.0.10, within WireMock's allowed range.
- A test-kit contract that skips cannot itself be tested and shows up as a skip in `make test`. Contracts that do not apply pass instead, and say so in the kit's README.
- Real files carry quirks that synthetic ones miss: a UTF-8 byte-order mark broke attestation parsing until a test wrote its sidecar the way Notepad does.
- Example data feeds count assertions in CLI and functional tests. A new example requirement moves those counts, and the functional suite against a real CISO Assistant confirms the seed library and the mapping agree.
- The first CI run of the `template` job failed. CI sets `CONFIGURATION=Release`, which MSBuild reads as a property, so the build produced Release binaries while `dotnet test --no-build` looked for Debug ones. Scripts that build and then test with `--no-build` now pass `-c` to both. Reproducing CI's environment variables locally found the cause, not the job's output.
- GitHub's automatic NuGet dependency submission restores every `.csproj` in the repo, including the template's, which reference SDK packages at the placeholder version `0.0.0-hound` and cannot restore. The template keeps its project and solution files as `.csproj.in` and `.slnx.in`, and `template.json` renames them when a collector is generated.
- CI and code-scanning results were not readable from the cloud session that ran this sprint. Check the PR's checks before merging.
