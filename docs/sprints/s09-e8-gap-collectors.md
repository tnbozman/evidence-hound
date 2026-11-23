# s09 Essential Eight gap collectors

Status: In progress

**Goal:** close the largest Essential Eight ML2 gaps left after the Microsoft 365, Sophos and Veeam collectors: application control evidenced from Intune and Microsoft Defender for Endpoint, patching evidenced from imported vulnerability scans, and a ready-made manual-upload template for every requirement that stays manual. After this sprint every ML1 and ML2 row in the example Essential Eight audit gets a result from a check or an attested manual source.

New sprint, added in the release 1 reorder. Application control extends the Microsoft 365 collector (same app registration). Vulnerability scans are imported by a new `vuln-scan` collector in `src/Collectors/EvidenceHound.Collectors.VulnScan`, which reuses the manual collector's drop folder and attestation handling. Recorded, fictional responses and exports only (Example Pty Ltd, `example.com`).

The patch windows and scan intervals below are settings, not constants, so the checks stay framework-neutral. The example config sets them to the Essential Eight ML2 values, and the mapping file quotes the ISM control each one comes from.

### REST collector (s09-09 to s09-14)

These tasks come first in this sprint, before s09-01, so the Microsoft 365 application control checks and the Veeam tasks can be written as profile checks instead of new code. They build the generic `rest` collector agreed in [`docs/design/rest-collector.md`](../design/rest-collector.md): every query is called once per run, evidence datasets are extracted from one or more responses, and checks test dataset fields, giving pass, fail or unable to assess. Profiles are YAML loaded only from the organisation's profiles folder; this repo keeps a tested public library in `profiles/` to copy from. There are no runtime code plugins. Where the Veeam tasks (s09-07, s09-08) and s09-01/02 say "collector", read "profile in the library" once these tasks are done.

### s09-09 Profile model and validation
- [x] Done

**Story:** As an operator, I want a profile checked before anything is called, so that a typo or a wrong reference fails clearly instead of producing misleading results.

**Acceptance criteria**
- Immutable records for a profile: `parameters`, `secrets`, `connection` (base URL, `allowed_hosts`, auth, rate limit), `bootstrap`, `queries`, `datasets`, `checks`, `requires`, `source` and `version`; parsed from YAML into the core once.
- Collector type `rest` with settings `profile` plus the profile's own parameters; secrets as for other collectors.
- Validation (pure) reports every problem at once with a code: unknown parameter or secret, missing required one, bad path syntax, a check `over` or dataset `from`/`join` naming nothing, a cycle in query dependencies, a code check name that is not built in, a check without `when_empty`, a request or auth URL outside `allowed_hosts`, `requires` newer than this `hound`.
- `hound profile validate <name>` and `hound plan` both run it; a profile that fails stops only its own instances, which report unable to assess.
- Profiles are read only from `profiles_path` (default `/config/profiles`), mounted read-only.

**Tests first**
- `ProfileParserTests.Reads_example_profile_into_records`
- `ProfileValidationTests.Check_over_unknown_dataset_is_reported`
- `ProfileValidationTests.Query_dependency_cycle_is_reported`
- `ProfileValidationTests.Host_outside_allowed_hosts_is_reported`
- `ProfileValidationTests.Check_without_when_empty_is_reported`
- `ProfileValidationTests.Reports_all_problems_not_just_the_first`

**Functional check**
- `hound profile validate example` passes for `profiles/example.yaml`, and a broken copy lists each problem with its code.

**Functional check result (2026-11-22)**

```
$ hound profile validate example --profiles profiles
example 1.0.0: valid (3 queries, 1 dataset, 4 checks)
$ hound profile validate example --profiles /tmp/broken    # over: people, and a bad path
error: profile.path_invalid: check 'example.identity.mfa_users': '$.mfa[' is not a path; use $, .name, ['name'], [0] or [*], for example $.items[*].id.
error: profile.reference_unknown: Check 'example.identity.mfa_users' is over 'people', which names nothing; queries and datasets: devices, roles, user_roles, users.
error: profile.reference_unknown: Check 'example.identity.owner_count' is over 'people', which names nothing; queries and datasets: devices, roles, user_roles, users.
$ echo $?
3
```

`hound plan` and `hound config validate` log each problem of a rest instance's profile as a warning naming the instance, and carry on (`ProfileCommandTests.Plan_warns_about_an_invalid_rest_profile_and_carries_on`); at run time that instance's checks are unable to assess (`RestCollectorTests.Invalid_profile_reports_its_checks_unable_to_assess`).

### s09-10 Query engine
- [x] Done

**Story:** As an operator, I want every endpoint read once per run, with sign-in, paging and rate limits handled for me, so that a new system needs a profile, not code.

**Acceptance criteria**
- Auth: OAuth 2.0 client credentials (secret or certificate), API key (header or query), bearer token, basic, mutual TLS. Tokens refresh on expiry and are sent only to `allowed_hosts`; cross-host redirects are refused.
- Paging: next link in the body, page number with total, cursor, offset and limit, `Link` header; a per-query page cap.
- Rate limits: per-profile rate, `Retry-After` on 429 and 503, bounded retries.
- Queries run as a dependency graph: `bootstrap` variables first, `for_each` fan-out capped by `max_calls`, `post:` bodies, `async` jobs (start, poll with a timeout, download), `optional_on` status codes for licence-dependent endpoints.
- JSON, XML and CSV responses become the same tree (XML attributes as `@name`, text as `#text`, profile-listed paths always arrays).
- Item and byte caps per query, with `keep:` to hold only listed fields.
- Each query is saved as one evidence file (request without headers, page count, item count, items) with `identities` hashed unless `include_identities`. A final scan refuses to save any file containing a secret in use.
- A failed query is a result for that query, not a failed run: later phases see which queries failed and why.

**Tests first**
- `RestAuthTests.Client_credentials_token_is_sent_only_to_allowed_hosts`
- `RestPagingTests.Follows_next_link_until_absent` (and one test per paging style)
- `RestPagingTests.Stops_at_page_cap_and_reports_it`
- `RestRateLimitTests.Honours_retry_after_then_gives_up`
- `QueryGraphTests.Bootstrap_variables_feed_later_requests`
- `QueryGraphTests.For_each_is_capped_by_max_calls`
- `AsyncQueryTests.Polls_until_done_then_downloads`
- `XmlTreeTests.Repeated_and_single_elements_read_the_same`
- `QueryEvidenceTests.Identities_hashed_and_secrets_never_saved`
- Contract tests against WireMock with fictional recordings.

**Functional check**
- `hound run --only rest-example --dry-run` against the recorded example API lists every query with its page and item counts and its evidence file.
- Result (2026-11-22): `hound run --config hound.yaml --only rest-example --dry-run` with an instance of `profiles/example.yaml` pointed at a local stand-in serving `tests/fixtures/rest/example`. Succeeded with no problems and nothing sent: token, then `whoami` (bootstrap, no file), then `devices.json` (2 pages by cursor, 3 items, identities hashed), `users.json` (2 pages by offset, 3 items, identities hashed) and `roles.json` (1 page, 2 items). No host name or client secret in any evidence file. The run first failed config validation because `token_url` was read as an inline secret by its name; fixed in this task (address and path settings are judged by value). Unit suite green, lint clean.

### s09-11 Datasets and declarative checks
- [x] Done

**Story:** As a compliance owner, I want evidence from several API calls combined into one dataset and tested field by field, so that a control is judged on exactly the evidence it needs and a broken query never shows as a failure.

**Acceptance criteria**
- Datasets: `from`, `join` (on a key, one-to-many, `optional` for a left join), `where`, `fields`, `flatten`. Each dataset is saved as an evidence file listing the query files and SHA-256 hashes it came from, and how many rows did not match a required join.
- A check reads one dataset (or one query directly, for a one-to-one check) and has `where`, `each` and `require` (`all`, `any`, `none`, `count_at_most`, `count_at_least`, `ratio_at_least` with an optional partial band).
- Operators: `equals`, `not_equals`, `in`, `matches`, `any_matches`, `exists`, `version_in`, `version_at_least`, `within`, `older_than`, `at_least`, `at_most`, each with `missing:` and `negate`.
- Results: pass, fail, partial (ratio bands only), not applicable (empty and `when_empty: not_applicable`), and unable to assess when a query the check depends on failed, paging stopped early, a cap was hit, a field was missing (unless `missing:` says otherwise) or a value had the wrong type. The observation names the cause.
- Failure propagates only to what depends on the failed query; a dataset is never built from part of its inputs.
- User-facing text says "unable to assess" for `CheckOutcome.Error`; the mapping still turns it into `not_assessed`.
- Observations use `describe` templates with counts, list offending rows capped at 20, and cite the dataset file.

**Tests first**
- `DatasetTests.Join_attaches_matching_rows_and_counts_unmatched`
- `DatasetTests.Failed_input_query_fails_the_dataset_with_its_name`
- `CheckEvaluatorTests.All_over_empty_uses_when_empty`
- `CheckEvaluatorTests.Missing_field_is_unable_to_assess`
- `CheckEvaluatorTests.Missing_pass_treats_absent_field_as_pass`
- `CheckEvaluatorTests.Ratio_band_gives_partial`
- `CheckEvaluatorTests.Independent_checks_still_run_when_one_query_fails`
- Property tests: evaluation is deterministic for the same snapshot and clock; adding a failed query never turns a fail into a pass.

**Functional check**
- `hound run --only rest-example --dry-run` shows each check's result and dataset evidence; breaking one recorded query makes only its checks unable to assess.
- Result (2026-11-22): `hound run --config hound.yaml --only rest-example --dry-run` against the local stand-in for the recorded example API. Evidence: `devices.json`, `users.json`, `roles.json` and `dataset.user_roles.json` (2 rows, 0 unmatched, sources `users.json` and `roles.json` with SHA-256 values equal to the run manifest's). Status record: 4 checks passed, 0 failed. With `/v1/orgs/org-0001/roles` answering 500: `roles.json` and the dataset file are not saved, the two device checks still pass and the two checks over `user_roles` are unable to assess (status record: 2 passed, 2 other), and the run still succeeds. Observations (counts, offending rows capped at 20, the evidence file cited, and the failed query named) are covered by `RestCollectorTests` and `CheckEvaluatorTests`, because `hound run` does not print them (see discovered work). Unit suite green, lint clean.

### s09-12 Code checks, profiles folder and profile tests
- [x] Done

**Story:** As a profile author, I want to test a profile against recorded responses before it is trusted, and to use a built-in code check when the operators are not enough.

**Acceptance criteria**
- A code-check library in `hound`: named, pure functions taking the datasets they name, parameters and the clock, returning a finding. A profile names them with `code:`; nothing is loaded at runtime.
- `hound profile test <name>` runs a profile offline against its recordings and expected results (`<profile>.tests/`), reporting each check's expected and actual result. `hound` warns when a profile in the folder has no tests; `require_profile_tests: true` makes it an error.
- `hound profile diff <name>` compares a copied profile with the library version named in its `source`.
- Each run's manifest records every profile's name, version, `source` and SHA-256.
- The public library `profiles/` starts with `example.yaml` (a fictional API) and its tests; CI runs every library profile's tests.

**Tests first**
- `CodeCheckRegistryTests.Unknown_name_is_a_validation_problem`
- `ProfileTestRunnerTests.Reports_expected_and_actual_per_check`
- `ProfileTestRunnerTests.Profile_without_tests_warns_or_fails_by_setting`
- `ProfileDiffTests.Shows_changes_from_library_version`
- `RunManifestTests.Records_profile_name_version_and_hash`

**Functional check**
- Copy `profiles/example.yaml` into a scratch profiles folder, run `hound profile test example`, change an expected result, and see the test fail naming the check.
- Result (2026-11-22): copied `profiles/example.yaml` and `profiles/example.tests/` into a scratch folder. `hound profile test example --profiles <scratch>` printed `example/all-pass: passed (4 checks)` and `example/roles-unreadable: passed (4 checks)`, exit 0. After changing `example.identity.owner_count: pass` to `fail` in `all-pass.yaml`: `example/all-pass: FAILED` and `example.identity.owner_count: expected fail, got pass. 1 of 2 active users hold the Owner role; at most 2 allowed. Evidence: dataset.user_roles.json.`, exit 1. `make test-profiles` (new, also in CI) passes. Unit suite green, lint clean. Decisions made here: profile tests are folders of case files (`<profile>.tests/<case>.yaml` with `recordings/`); `require_profile_tests` is a per-instance rest setting rather than a deployment setting; `hound profile diff` takes `--library <evidence-hound checkout>` (or `HOUND_LIBRARY_PATH`) and resolves `source` inside it; the code-check library starts with `sophos.super_admin_count` (moved from the Sophos collector for s09-13).

### s09-13 Sophos as a profile
- [x] Done

**Story:** As an operator, I want Sophos Central read through a profile, so that changes to Sophos checks are configuration, and as the proof that the REST collector can replace a coded one.

**Acceptance criteria**
- `profiles/sophos-central.yaml` with its tests: `whoami` bootstrap, firewalls, admins and roles, the `admin_accounts` and `firewall_firmware` datasets, and every check the coded collector reports (including any added by s08-12 to s08-14).
- A parity test runs the coded collector and the profile on the same recordings and requires the same check IDs, outcomes and evidence content.
- When parity passes, the coded Sophos collector is removed. A `type: sophos` instance fails validation with a message showing the `type: rest`, `profile: sophos-central` replacement; the example config and `docs/collectors/sophos.md` are updated.
- The interactive tutorial (`make trial`) copies the profile into the trial's profiles folder and uses it.

**Tests first**
- `SophosProfileParityTests.Profile_matches_coded_collector_on_every_recording`
- `SophosProfileTests` via `hound profile test sophos-central`
- `CollectorConfigTests.Sophos_type_suggests_rest_profile`

**Functional check**
- `hound run --only sophos-example --dry-run` with the profile gives the same results as before; the trial's Sophos step passes with the profile.

**Result**
- `profiles/sophos-central.yaml` (1.0.0) with three test cases (all pass, unsupported firmware, partner credential). The checks run straight over the `firewalls`, `admins` and `roles` queries (the Super Admin count is the `sophos.super_admin_count` code check over admins and roles), so the `admin_accounts` and `firewall_firmware` datasets were not needed and were not added.
- `SophosProfileParityTests` passed on six recordings (as recorded, unsupported firmware, no firmware, no firewalls, every admin a super admin, no super admin role): same check IDs, outcomes and items in `firewalls.json`, `admins.json` and `roles.json`. The profile hashes `geoLocation`, which the coded collector dropped. The test was removed with the coded collector, since it needs both.
- Removed: the coded collector, its tests, its contract test and its registration. `RecordedSophos` moved to the functional tests, which now run the profile. `type: sophos` fails with `config.collector_type_retired` and the `type: rest`, `profile: sophos-central` replacement.
- One behaviour change: partner and organisation credentials (`tenant_id`) are not supported by the profile; a tenant credential is. The guide says so.
- `hound add collector rest --set profile=<name>` now asks for the profile's required parameters and takes its secrets (`CollectorDescription.SettingsFrom`). The trial copies the profile and its tests into `.dev/trial/profiles` and adds the collector by name; `trial_collector_step` matches by name.

**Functional check (run)**
```console
$ hound config validate hound.yaml --check-secrets      # type: rest, profile: sophos-central, stand-in API on 127.0.0.1 serving tests/fixtures/sophos
valid
$ hound run --config hound.yaml --only sophos-example --dry-run
outcome succeeded, checks_passed 2, checks_failed 0, publish planned 4, sent 0; firewalls.json, admins.json, roles.json; manifest names sophos-central 1.0.0
```
The coded collector gave the same two passes on the same recording (the parity test above). `make test-profiles`: sophos-central all-pass, firmware-unsupported and partner-credential pass. The trial's Sophos step needs a real credential, so it is left for s09-17 (the person's trial); its unattended run and `bats scripts/tests` pass.

### s09-14 REST collector documentation
- [x] Done

**Acceptance criteria**
- `docs/collectors/rest.md`: profile reference (connection, auth, paging, queries, datasets, checks, operators, results, limits), the profiles folder, the library and how to copy from it, and security notes (`allowed_hosts`, secrets, identities).
- `docs/tutorials/write-a-profile.md`: write a profile for the fictional example API test-first, from recording to green `hound profile test`.
- `docs/design/rest-collector.md` status set to Implemented, with any changes made during the build.

**Tests first**
- Docs check that every operator and result in the engine appears in `docs/collectors/rest.md`.
- Docs lint and link check.

**Functional check**
- Following the tutorial verbatim produces a passing profile.

**Result**
- `docs/collectors/rest.md`: the reference (profiles folder and library, `hound profile` commands, every key, auth type, paging style, operator, `require` mode and result, limits, security and run problems). `RestDocsTests` fails if an operator, result, auth type, paging style or parameter type is missing, or a relative link is broken.
- `docs/tutorials/write-a-profile.md`: `example-devices` written test-first from the example recordings. `RestDocsTests.Tutorial_followed_as_written_gives_a_passing_profile` writes the tutorial's files and runs its tests.
- `docs/design/rest-collector.md`: Status Implemented, with an "As built" section. `docs/configuration.md` lists the `rest` type.

**Functional check (run)** The tutorial followed by hand with the built CLI, in a scratch folder:
```console
$ hound profile test example-devices --profiles my-profiles     # after step 2
error: profile.not_found: No profile 'example-devices' in the profiles folder .../my-profiles. ...
$ hound profile validate example-devices --profiles my-profiles # after step 3
example-devices 0.1.0: valid (1 query, 0 datasets, 1 check)
$ hound profile test example-devices --profiles my-profiles     # after step 4
example-devices/all-supported: passed (1 check)
example-devices/old-version: passed (1 check)
```

### Suppliers before systems (s09-19, s09-20, moved from s10)

A system is registered only once its supplier is approved, so the supplier register comes before onboarding. s10-01 and s10-02 moved here unchanged except for the supplier approval gate; s10-03 (documentation and example repo) and s10-04 (evidence plan tasks, including the recurring supplier review task) stay in s10.

### s09-19 Suppliers, solutions and contracts (was s10-01)
- [x] Done

**Story:** As a supplier owner, I want our suppliers, what they provide and our contracts with them declared in the repo, so that the supplier register is reviewed and complete.

**Acceptance criteria**
- `scope/third-parties/<entity>.yaml` declares one entity: `ref_id`, name, description, mission, country (ISO 3166-1 alpha-2), currency (ISO 4217), parent entity, domain, and its dependency (0–4), penetration (0–4), maturity (1–4) and trust (1–4).
- It also declares the entity's solutions (`ref_id`, name, description, criticality 1–4) and contracts (`ref_id`, name, status `draft`, `active`, `expired` or `terminated`, start and end dates, annual expense, currency, the solutions covered, and identifiers such as LEI or VAT).
- Representatives are optional. They are personal data, so the guide recommends keeping them in CISO Assistant unless the repo is private and access-controlled; when declared, they are matched by email and never given a login by `hound`.
- If s06-05 confirmed the API can update the main entity, `organisation` in `scope/setup.yaml` manages it here (the s06 discovered-work item).
- Validation: country and currency codes; contract end after start; an `active` contract past its end date is reported (`supplier.contract_lapsed`); solutions named by a contract belong to the same entity.

**Tests first**
- `SupplierScopeTests.Invalid_country_or_currency_is_reported`
- `SupplierScopeTests.Active_contract_past_end_date_is_reported`
- `SupplierScopeTests.Contract_solution_must_belong_to_entity`
- Contract: `SupplierClientTests.Creates_entity_solution_and_contract`

**Functional check**
- Apply two fictional suppliers to the local CISO Assistant; re-applying writes nothing; a changed contract end date is a one-field update.

Run 2026-11-22 in CI (run 280, functional job, CISO Assistant v4.3.4) by `SupplierScopeFunctionalTests`, from a copy of `examples/compliance-repo` with the organisation's own entity and two fictional suppliers (Example Cloud Pty Ltd with an LEI, Example Backup Ltd):

```console
$ hound scope apply scope --config hound.yaml          # failed: 0
$ hound scope plan scope --config hound.yaml
0 to create, 0 to update, 0 to delete.
$ # end: "2027-12-31" -> "2028-06-30" in example-cloud.yaml
$ hound scope plan scope --config hound.yaml
0 to create, 1 to update, 0 to delete.
| Update | contract | CTR-FT-HOST | end_date: 2027-12-31 → 2028-06-30 |
$ hound scope apply scope --config hound.yaml          # written: 1
```

The first CI run found that CISO Assistant returns an entity's legal identifiers as display text ("LEI: …"), so the re-plan showed an update; fixed in the same task.

### s09-20 Supplier assessments, tiers and approval (was s10-02)
- [ ] Done

**Story:** As a supplier owner, I want each supplier's assessment set up from the repo on a review cycle, so that reviews start on time and nobody sets them up by hand.

**Acceptance criteria**
- An entity file may declare assessments: name, solutions, perimeter, due date, criticality, the questionnaire framework (a library URN), and `review_every` (such as `12 months`). Applying creates the entity assessment and its linked questionnaire audit.
- When a review is due, `hound` opens the next one: a new revision if the API supports it (s06-05), otherwise a new assessment named with its period, using the same periods logic as audits (s06-10).
- Answers, tiering form results and campaigns are never written by `hound`.
- `forced_tier` (one of the criticality scale's keys, such as `critical`) with a required `reason` may be declared; `scope plan` reports when CISO Assistant's current tier differs from it.
- Approval: a supplier is approved when its latest entity assessment is concluded as acceptable in CISO Assistant (its `conclusion` field; confirm the field and its values against the pinned release's schema in the first test) and is within its review cycle, and it has an `active` contract. Approval is read from CISO Assistant, never written by `hound`.
- Supplier gate: a system in `scope/` names its `solution`, and optionally `supplied_through` (the reseller or provider the contract is with). `scope plan` refuses to register the system, or add it to an audit, until every named supplier is approved (`system.supplier_not_approved`, naming the supplier and what is missing). A supplier whose approval later lapses (review overdue, conclusion changed, contract ended) does not unregister the system; `scope plan` reports `supplier.approval_lapsed` and the system is listed for the committee.
- The recurring review task for `review_every` comes with s10-04 (evidence plan tasks), which stays in s10.

**Tests first**
- `SupplierAssessmentTests.Due_review_opens_the_next_one`
- `SupplierAssessmentTests.Answers_are_never_in_the_plan`
- `SupplierScopeTests.Forced_tier_requires_reason`
- `ScopePlanTests.Tier_differing_from_forced_tier_is_reported`
- `SupplierGateTests.System_with_unapproved_supplier_is_not_registered`
- `SupplierGateTests.Reseller_named_by_supplied_through_must_also_be_approved`
- `SupplierGateTests.Lapsed_approval_is_reported_not_unregistered`

**Functional check**
- With a fixed clock past a review date, `scope apply` opens the next fictional supplier assessment in the local CISO Assistant with its questionnaire.
- A fictional system whose supplier has no concluded assessment is refused by `scope plan`; concluding the assessment in CISO Assistant lets it through.

### Onboarding assistant (s09-21 to s09-26)

These tasks come after s09-14, because they draft REST profiles, and after s09-20, because onboarding starts with the supplier. They build `hound onboard` from [`docs/design/onboarding-assistant.md`](../design/onboarding-assistant.md): a guided session in which a language model proposes how a system fits a framework and drafts its profile, and a person accepts or edits every decision. Nothing is applied to CISO Assistant by the session; the output is files for a pull request in the organisation's config repo. Tests use a fake model with recorded replies; no test or CI job calls a live model.

### s09-21 Model port, Anthropic and OpenAI adapters, onboarding config and outbound guard
- [x] Done

**Story:** As an operator, I want `hound` to use a language model I choose, with my key kept as a secret and nothing sensitive sent, so that I can use the assistant without leaking organisation data.

**Acceptance criteria**
- Core port `IDraftingModel`: instructions, inputs and a JSON schema in; a reply validated against the schema, or a problem (`llm.refused`, `llm.invalid_reply`, `llm.budget_reached`, `llm.unreachable`), out.
- Two adapters, each on its provider's official .NET SDK: Anthropic (structured outputs, adaptive thinking, streaming for long replies) and OpenAI (structured outputs with a JSON schema). Both pass one shared adapter contract suite.
- `onboarding.yaml`, separate from the deployment config: `llm.provider` (`anthropic` or `openai`, required), `llm.model` (empty means the provider's default), `llm.api_key` (a secret reference, `env:` or `file:`; a literal key fails validation), `llm.effort`, `llm.max_tokens_per_session`, `llm.deny_terms_file`, `skills_path`, `profiles_path`.
- Default models per provider in `onboarding/models.yaml`, shipped with `hound` and editable without a build: each provider's current top model (`claude-opus-5-5` for Anthropic; OpenAI's confirmed from its model list when this task is built).
- `hound onboard config`: asks for provider and model, reads the key without echo, saves it to `~/.config/hound/secrets/llm_api_key` with owner-only permissions, and writes `onboarding.yaml` with a `file:` reference. `--check` resolves the key and makes one minimal request to confirm it works, never printing the key.
- Outbound guard (pure): every request is scanned for secret values in use, the deny terms, and IP, MAC and email patterns; a hit returns `llm.blocked_content` naming what matched and nothing is sent.
- Response shapes (pure): a response reduced to field names, types and counts, keeping values only for settings-like fields (few distinct values, no identity pattern) and never for profile `identities`.
- Every request and reply is saved locally with the session; `--show-requests` prints each before it is sent.
- No model in evidence collection: the onboarding code and model adapters live in `EvidenceHound.Onboarding`, used only by `hound onboard`. An architecture test fails if the core, runtime, collectors or `hound run` reference it or any model SDK. `hound run` and the scheduler refuse a deployment config that declares an `llm` section or references the onboarding key (`config.llm_in_runner`).

**Tests first**
- `ArchitectureTests.Runner_and_collectors_do_not_reference_onboarding_or_model_sdks`
- `RunnerConfigTests.Llm_key_or_setting_in_runner_config_is_refused`
- `OutboundGuardTests.Secret_value_in_request_blocks_it`
- `OutboundGuardTests.Deny_term_and_email_are_reported`
- `ResponseShapeTests.Keeps_enumerations_drops_identities_and_free_text`
- `DraftingModelContractTests<T>` run for both adapters against recorded exchanges (WireMock): sends the schema and configured model, maps refusals and invalid replies to problems
- `OnboardingConfigTests.Literal_api_key_is_refused`
- `OnboardingConfigTests.Empty_model_uses_provider_default`
- `OnboardConfigCommandTests.Writes_reference_and_owner_only_key_file`

**Functional check**
- With a fake model, `hound onboard --show-requests` on the example system prints requests containing no recorded host names, IDs or secrets.

**Functional check, run 2026-11-22** (the command that onboards a system arrives with s09-22, so its `--show-requests` run is recorded there; the gate it goes through is built and tested here):

```console
$ echo "$KEY" | XDG_CONFIG_HOME=/tmp/fc21/home hound onboard config --yes --provider anthropic --config /tmp/fc21/onboarding.yaml
API key for anthropic (not shown): Wrote /tmp/fc21/onboarding.yaml; the key is in /tmp/fc21/home/hound/secrets/llm_api_key, readable only by you.
$ cat /tmp/fc21/onboarding.yaml
llm:
  provider: anthropic
  model: ""
  api_key: file:/tmp/fc21/home/hound/secrets/llm_api_key
$ stat -c '%a %n' /tmp/fc21/home/hound/secrets /tmp/fc21/home/hound/secrets/llm_api_key
700 /tmp/fc21/home/hound/secrets
600 /tmp/fc21/home/hound/secrets/llm_api_key
$ hound config validate /tmp/fc21/hound.yaml      # a deployment config with an llm section
error: /tmp/fc21/hound.yaml line 2: 'llm' is not allowed here: the runner never uses a language model; ...
```

`--check` against a stand-in provider (`OnboardConfigCommandTests`) makes one request and prints `anthropic claude-opus-5-5: the key works.` without the key. Both adapters pass the shared contract suite on recorded replies. OpenAI's top model was confirmed as `gpt-6.2-astra` from its published model list.

### s09-26 Onboarding skills
- [x] Done

Built straight after s09-21, before s09-22, because every later onboarding step reads its guidance from skills.

**Story:** As a GRC team member, I want the assistant's guidance in text files I can read and adjust without a build, with good-practice defaults and a way to add our own, so that onboarding improves as we learn.

**Acceptance criteria**
- A skill is a Markdown file with YAML frontmatter: `name`, `description`, `version`, `applies_to` (one or more of `supplier`, `system`, `applicability`, `profile`, `mapping`), optional `frameworks` and optional `overrides`. Parsed and validated once into an immutable record.
- Default skills in `skills/`, built into `hound`: supplier assessment and tiering, supplier assurance documents, applicability and not-applicable justification, Essential Eight ML2 mapping, profile authoring, reading an API specification.
- Organisation skills load from `skills_path` (default `/config/skills`, read-only). They add to the defaults; a skill with the name of a default is an error unless it declares `overrides:` with that name.
- `hound skill list` (source, version, SHA-256, overrides), `hound skill new <name>`, `hound skill add <file>` (validate, then copy into the organisation folder) and `hound skill validate` (frontmatter, step names, size budget, and a warning for anything matching the deny terms or identity patterns).
- Selection (pure): for a step and framework, every matching skill, defaults first, within a size budget; over budget is a problem naming the skills, not a silent cut.
- Skills are passed to the model as instructions; vendor documents and specifications are passed separately as material to read. The outbound guard scans skills like any other content.
- The onboarding record lists every skill used with its source, version and SHA-256.
- Skills cannot change the outbound guard, response shapes, the approver rule, the supplier gate, schema validation or profile tests; a test shows a skill telling the model to skip approval or send raw data has no effect on what is sent or saved.

**Tests first**
- `SkillParserTests.Reads_frontmatter_and_body`
- `SkillValidationTests.Unknown_step_and_missing_fields_are_reported`
- `SkillCatalogueTests.Same_name_without_overrides_is_an_error`
- `SkillCatalogueTests.Override_replaces_default_and_is_listed`
- `SkillSelectionTests.Selects_by_step_and_framework_within_budget`
- `SkillSafetyTests.Skill_cannot_weaken_guard_or_approver_rule`
- `OnboardRecordTests.Lists_skills_with_version_and_hash`

**Functional check**
- `hound skill new example-check`, edit it, `hound skill add` it, and `hound skill list` shows it beside the defaults; an onboarding run with a fake model records it.

**Functional check, run 2026-11-22** (the onboarding run that records skills comes with s09-22's command; `SessionRecord.SaveSkillsAsync` writes the list and is tested by `OnboardRecordTests`):

```console
$ hound skill new example-check --skills /tmp/fc21/sk/drafts
Wrote /tmp/fc21/sk/drafts/example-check.md. Edit it, then 'hound skill add /tmp/fc21/sk/drafts/example-check.md'.
$ hound skill add /tmp/fc21/sk/drafts/example-check.md --skills /tmp/fc21/sk/skills
Added example-check 0.1.0 to /tmp/fc21/sk/skills.
$ hound skill list --skills /tmp/fc21/sk/skills
| Skill | Source | Version | SHA-256 | Steps | Overrides |
| applicability | default | 1.0.0 | f0511f73c6a7 | applicability, system |  |
| essential-eight-ml2-mapping | default | 1.0.0 | c8cb33f4728a | applicability, mapping |  |
| profile-authoring | default | 1.0.0 | 495eea9ed2c2 | profile |  |
| reading-an-api-specification | default | 1.0.0 | d5df70328689 | system, applicability, profile |  |
| supplier-assessment | default | 1.0.0 | cc5960da8145 | supplier |  |
| supplier-assurance-documents | default | 1.0.0 | c033e2ac0c0d | supplier |  |
| example-check | organisation | 0.1.0 | e2cb76e71ca5 | applicability |  |
$ hound skill validate --skills /tmp/fc21/sk/skills
7 skills valid (1 from /tmp/fc21/sk/skills), 0 warnings.
```

### s09-28 Onboard the corporate environment: the capability map (added 2026-11-22)
- [ ] Done

**Story:** As a GRC team member, I want to record, for each control, which product in our environment actually provides it, before any system is onboarded, so that `hound` only judges a control on the system that provides it and we never fail an audit because of bad data.

**Acceptance criteria**
- `hound onboard environment --framework <framework file>` walks each mitigation of the framework (Essential Eight ML2 first; NIST SP 800-171 families later) and records, for each, the authoritative product or products: a system in scope (for example Microsoft 365 with Intune, Defender for Endpoint, Airlock Digital, ThreatLocker, Sophos, Veeam), manual evidence, or a gap.
- The result is `scope/capabilities.yaml` in the organisation's config repo: mitigation, the system that provides it, its supplier, the owner, the evidence route (collector, profile or manual) and who approved the entry. A schema and `hound config validate` check it.
- **No blob.** The map only decides which system answers each requirement. Results stay in CISO Assistant's existing structure: one audit per system (scope/audits, as today), each with its own owner. In each system's audit, a requirement another system provides is not applicable with the reason "Provided by <system> (audit <ref>)"; nothing is merged into a combined audit. The map writes those decisions into the existing per-system audit files for review, and `hound scope plan` shows them before anything is applied.
- Signals pre-fill answers that the person confirms, never decide them: read-only Microsoft Graph lookups (subscribed licences, Intune compliance and app control policies present, devices onboarded to Defender) and the systems already in scope. Each pre-filled answer shows the signal it came from.
- The map drives the rest. A system named for a mitigation goes through `hound onboard <system>` (s09-22), and its supplier goes through the supplier gate (s09-19/20). Checks for a mitigation run only on the system the map names.
- A mitigation with no product is a gap: `hound plan` and reports list it as a requirement no system's audit answers, never as a fail on some system.
- A check for a capability that is not in use cannot give pass or fail on that system's audit row; the row is not applicable with the reason naming the providing system.
- Default skill `environment-capabilities` (s09-26): how to tell which product is authoritative per mitigation, common third-party products, and what evidence each gives. Organisation skills can extend it.
- `--no-model` runs the same steps with only the signals and the person's answers.

**Tests first**
- `CapabilityMapTests.Every_mitigation_gets_one_decision_with_an_approver`
- `CapabilityMapTests.Gap_is_reported_as_a_gap_not_a_fail`
- `CapabilitySignalsTests.Signals_prefill_but_never_decide`
- `CapabilityScopeTests.Check_for_an_unused_capability_is_not_applicable_with_the_reason`
- `CapabilityScopeTests.Decisions_land_in_each_systems_own_audit_not_a_combined_one`
- `OnboardEnvironmentTests.Named_system_and_supplier_are_queued_for_onboarding`

**Functional check**
- With a fake model and recorded Graph answers, `hound onboard environment` for Example Pty Ltd records application control as provided by a third-party product, and a following `hound plan --dry-run` shows the application control rows of the Microsoft 365 audit as not applicable, provided by that product, while the third-party product's own audit holds the application control requirements.

### s09-29 Microsoft 365 checks follow the capability map (added 2026-11-22)
- [ ] Done

**Story:** As a compliance owner, I want the Microsoft 365 collector to assess only the capabilities we use from Microsoft 365, so that a third-party product (for example ThreatLocker for application control) is not reported as a Microsoft 365 failure.

**Acceptance criteria**
- The m365 collector's checks are grouped by capability (application control, patching, macros, user application hardening, admin privileges, MFA, backups, endpoint detection). Each instance gets the capabilities it provides from `scope/capabilities.yaml`; the others return not applicable naming the providing system.
- `hound onboard m365` is the Microsoft 365 path of s09-22: it reads licences and policies (read-only), proposes which capabilities Microsoft 365 provides, and the person confirms before any check is enabled.
- The tutorial's Microsoft 365 section and `docs/collectors/m365.md` explain the capability choice.

**Tests first**
- `M365CapabilityTests.Unused_capability_is_not_applicable_naming_the_provider`
- `M365CapabilityTests.Default_without_a_map_keeps_todays_checks`
- `OnboardM365Tests.Licences_and_policies_prefill_capabilities`

**Functional check**
- Against the recorded tenant (s09-27 when ready, the existing recorded `example.com` tenant before), an instance without application control reports those checks as not applicable, provided by the named product.

**Order:** environment onboarding (s09-28) after s09-21 and s09-26 and before s09-22; the Sophos firewall (s09-25) stays the first system onboarded; s09-29 after s09-25.

### s09-22 Understand the system and decide applicability
- [x] Done

**Story:** As a GRC team member, I want `hound` to read a system's API documentation and the target framework and propose, for each requirement, whether it is an API check, manual, not applicable or for another system, so that I decide applicability quickly and with reasons.

**Acceptance criteria**
- `hound onboard <system> --framework <framework file>` asks for the system name, owner and product, and starts from a copy of a library profile when one names the product.
- Supplier first: the session asks who provides the system and who the contract is with. For a supplier not yet in the repo it drafts the entity, solution and contract files (s09-19) and a supplier assessment (s09-20), with proposed dependency, penetration, maturity, trust and criticality and the reasons for each, and a supplier summary drawn from the vendor's public documents (certifications, where data is held, support lifecycle), each with its source. The person accepts or edits each. The summary is a draft kept in the onboarding record, not evidence: the vendor documents the person keeps are downloaded with source URL, retrieval date and SHA-256 and attached to the supplier assessment as its evidence. Questionnaire answers are never drafted into CISO Assistant.
- If a named supplier is not yet approved, the session saves, writes the supplier files and stops before applicability; it resumes from there once the supplier is approved.
- Reads an OpenAPI document, other API specifications or saved documentation from local files or allowed URLs, and shows the model's summary of readable endpoints, sign-in and paging.
- Reads every in-scope requirement from the framework file and shows one proposal per requirement (API check, manual, not applicable, not this system) with its reasoning; the person accepts, edits or rejects each, and can save and resume.
- Not applicable cannot be accepted without a justification and an approver name.
- `--no-model` runs the same steps with empty proposals.

**Tests first**
- `OnboardSupplierTests.Unapproved_supplier_stops_before_applicability_and_resumes_after`
- `OnboardSupplierTests.Supplier_proposals_carry_reasons_and_sources`
- `OnboardSupplierTests.Summary_is_not_evidence_and_kept_documents_are_attached_with_hash`
- `ApplicabilityTests.Not_applicable_without_justification_or_approver_is_refused`
- `ApplicabilityTests.Every_in_scope_requirement_gets_one_decision`
- `OnboardSessionTests.Resume_keeps_accepted_decisions`
- `SpecReaderTests.Reads_openapi_paths_and_security_schemes`

**Functional check**
- Onboard the fictional example API against the example framework with a fake model; the decision list covers every requirement and resumes after a stop.

Run 2026-11-22 against a copy of `examples/compliance-repo`, with a local fake of the Anthropic Messages API (`HOUND_ANTHROPIC_BASE_URL`) that proposes `api_check` or `manual` for every requirement it is sent. It also records s09-21's `--show-requests` check.

```console
$ hound onboard config --provider anthropic --model "" --config repo/onboarding.yaml     # a test key
$ hound onboard EXAMPLE-ASSETS --repo repo --group 2 --spec examples/apis/example-asset-api.openapi.yaml --show-requests
--- request 1 (supplier) ---  (the skills supplier-assessment and supplier-assurance-documents in the instructions; no key, address or deny term)
SUP-EXAMPLE is not approved yet. Review scope/third-parties/sup-example.yaml; once the supplier is approved, make contract CTR-EXAMPLE-ASSETS active and run 'hound onboard EXAMPLE-ASSETS' again to decide applicability.
$ sed -i 's/status: draft/status: active/' repo/scope/third-parties/sup-example.yaml
$ hound onboard EXAMPLE-ASSETS --repo repo --group 2 --spec ...     # n, reasoning, justification, approver, then s
[1/107] ISM-1807: An automated method of asset discovery is used at least fortnightly to support…
  Proposed: api_check. Fake model proposal.
Saved: 1 of 107 requirements decided. Run the same command to carry on.
$ hound onboard EXAMPLE-ASSETS --repo repo --group 2 --spec ...     # l
[2/107] ...
All 107 requirements decided: 53 API check(s), 53 manual, 1 not applicable, 0 for another system. The session is in onboarding/EXAMPLE-ASSETS/session.json.
```

Requests saved: `001-supplier`, `002`-`004-applicability` (107 requirements in batches of 40); the resumed runs sent nothing new. The not-applicable decision keeps its proposal (`api_check`), justification and approver. The test key appears in no file in the repo and in no output.

### s09-23 Draft, sample and test the profile
- [ ] Done

**Story:** As a GRC team member, I want the profile for the accepted API checks drafted, proven against the real system and tested, so that I only review a profile that already works.

**Acceptance criteria**
- The model drafts queries, datasets and checks for every accepted API check, with framework-neutral check IDs, as a structured reply validated against the profile schema.
- With the person's go-ahead, the drafted queries run once, read-only, with the configured credentials; responses are saved as test recordings with identities hashed, and only their shapes go to the model.
- `hound profile validate` and `hound profile test` run after each round; their problems go back to the model, for at most `llm.max_rounds` (default 3) rounds, and the remaining problems are shown.
- The person confirms each check's expected result before it is saved as a test.

**Tests first**
- `ProfileDraftTests.Reply_not_matching_schema_is_a_problem`
- `DraftLoopTests.Validation_problems_are_fed_back_until_clean_or_rounds_used`
- `SampleRunTests.Recordings_are_hashed_and_only_shapes_are_sent`

**Functional check**
- Against the recorded example API, a fake model's first draft with a wrong path is fixed in the second round, and `hound profile test` passes.

### s09-24 Write the onboarding output
- [x] Done

**Story:** As a GRC team member, I want the session to write the profile, mappings, applicability and a record of how it was decided into my config repo, so that I can review it in a pull request and show auditors how applicability was decided.

**Acceptance criteria**
- Writes `profiles/<system>.yaml` and its tests, mapping additions to `mappings/<framework>.yaml` (shown as a diff), scope assessments for not-applicable rows (`result: not_applicable`, justification and approver in the observation) and manual rows, and manual source stubs.
- Writes `onboarding/<system>-<date>.md` with the system summary, every decision with its proposal and who accepted it, the provider and model, and links to the saved requests.
- Writes only into the working copy; never calls CISO Assistant. `hound scope plan` and `hound plan` then show the changes as for any other edit.
- Re-running for another framework keeps the profile and adds only new checks, mappings and applicability; `--update` proposes profile changes from a fresh spec and sample, never applying them.

**Tests first**
- `OnboardOutputTests.Not_applicable_becomes_scope_assessment_with_justification`
- `OnboardOutputTests.Mapping_additions_do_not_change_existing_rows`
- `OnboardOutputTests.Record_lists_every_decision_and_model`
- `OnboardOutputTests.Second_framework_adds_without_rewriting_profile`

**Functional check**
- After onboarding the example system, `hound scope plan` shows the not-applicable rows with their justifications and `hound plan --dry-run` shows the new checks.

As built (2026-11-22): the system's audit, manual rows added to the mapping's `manual_only`, and the record. The profile and its tests come from s09-23 (for Sophos, the library `sophos-central` profile is used as it is), so `hound plan --dry-run` has no new checks to show yet; manual source stubs and `--update` move to s09-23.

Run locally against a copy of `examples/compliance-repo` with the fake model of s09-22 (one requirement not applicable, one for another system, the rest accepted):

```console
$ hound onboard EXAMPLE-ASSETS --repo repo --group 2 --spec examples/apis/example-asset-api.openapi.yaml
All 107 requirements decided: 53 API check(s), 52 manual, 1 not applicable, 1 for another system. The session is in onboarding/EXAMPLE-ASSETS/session.json.
Wrote scope/audits/example-assets-essential-eight-2023.yaml, onboarding/example-assets-essential-eight-2023-2026-11-22.md. mappings/essential-eight-2023.yaml already names every manual requirement.
Next: review the changes in a pull request, then run 'hound scope validate scope', 'hound scope plan scope' and 'hound scope apply scope'.
$ hound scope validate repo/scope --config repo/hound.yaml
scope valid: 1 domain(s), 3 control(s), 4 audit(s), 1 third party(ies)
$ hound onboard EXAMPLE-ASSETS --repo repo ...
EXAMPLE-ASSETS is onboarded and its files are already written: ... Move onboarding/EXAMPLE-ASSETS/session.json aside to onboard it again.
```

The first run found that a single-system audit needs `unlisted:` and that a requirement keyed by URN is already covered when the mapping names its ref_id; both fixed. Plan, apply and re-plan against CISO Assistant run in CI as `OnboardFunctionalTests`.

### s09-25 Onboard the Sophos firewall, and documentation
- [ ] Done

**Story:** As the person running this project, I want to onboard my own Sophos firewall with `hound onboard`, so that the assistant is proven on a real system and real requirements.

**Acceptance criteria**
- The person runs `hound onboard` in the dev container with their own model key and read-only firewall credentials, registers and approves the supplier (the vendor or the reseller their contract is with) first, against the Essential Eight ML2 framework, and commits the output to their private config repo. Nothing from the run is committed here.
- What the run showed (steps that were slow or confusing, wrong proposals, missing operators) is recorded as discovered work, without organisation data.
- `docs/onboarding.md`: how to onboard a system, what is sent to the model and how to check it, how to use another provider or `--no-model`, how to write, add and override skills, and how auditors read the onboarding record. A tutorial section onboards the fictional example API.

**Tests first**
- Docs check that every `hound onboard` option appears in `docs/onboarding.md`.
- Docs lint and link check.

**Functional check**
- The person's onboarding run produces a profile that passes `hound profile test`, and the firewall's checks appear in a `hound plan --dry-run`.

### s09-01 Application control policies from Intune
- [ ] Done

**Story:** As a compliance owner, I want application control policy evidenced from Intune, so that the application control strategy has live evidence instead of screenshots.

**Acceptance criteria**
- The M365 collector reads App Control for Business policies (Intune endpoint security, settings catalog) and custom profiles that configure the AppLocker CSP, with their assignments. `DeviceManagementConfiguration.Read.All` is already granted, so no new permission is needed.
- `endpoint.application_control.enforced`: `pass` when an enforced policy is assigned to all devices or to every group in `application_control_groups`; `partial` when a policy is in audit mode or covers only some of those groups; `fail` when none exists.
- `endpoint.application_control.file_types_covered`: `pass` when the enforced policy covers executables, software libraries, scripts and installers (App Control for Business covers them by design; AppLocker needs the Exe, Dll, Script and Msi rule collections enforced); `fail` naming the missing types.
- Policies set by Group Policy are not seen; the check says so in its detail and the docs list it as a limit.

**Tests first**
- `AppControlChecksTests.Enforced_policy_assigned_to_all_devices_passes`
- `AppControlChecksTests.Audit_mode_policy_is_partial`
- `AppControlChecksTests.Applocker_without_dll_rules_names_missing_type`
- `AppControlChecksTests.No_policy_fails`
- Contract kit run for the M365 collector with the new recordings.

**Functional check**
- `hound run --only m365-example --dry-run` against recordings shows both checks with the expected outcomes.

**Notes**
- Confirm the settings catalog template and setting IDs against a recorded response in the first test, and record them in the docs.

### s09-02 Application control events from Defender for Endpoint
- [ ] Done

**Story:** As a compliance owner, I want proof that allowed and blocked application control events reach a central log, so that the logging requirement is evidenced and blocked files are visible to the committee.

**Acceptance criteria**
- The M365 collector runs two advanced hunting queries through Microsoft Graph (`v1.0/security/runHuntingQuery`, application permission `ThreatHunting.Read.All`): App Control and AppLocker events from `DeviceEvents` over `application_control_window_days`, and onboarding state from `DeviceInfo`.
- `endpoint.application_control.events_centrally_logged`: `pass` when Defender for Endpoint is onboarded on at least `defender_onboarded_percent` of Intune-managed Windows devices and events were received in the window; `partial` when onboarding is below the threshold; `fail` when no events were received.
- Evidence: event counts per device and action type, and the most-blocked files with their publisher. No file content or user names beyond the account name in the event.
- Without a licence that includes advanced hunting the query is refused; the check reports `not_applicable` with that reason and the docs point to the manual template from s09-05.

**Tests first**
- `HuntingQueryTests.Sends_query_and_timespan`
- `AppControlEventsTests.Events_and_onboarding_above_threshold_passes`
- `AppControlEventsTests.Low_onboarding_is_partial`
- `AppControlEventsTests.Missing_licence_is_not_applicable_with_reason`

**Functional check**
- `hound run --only m365-example --dry-run` against recordings shows the check and its evidence file.

**Notes**
- Confirm which licences include advanced hunting through Graph, and record it in `docs/collectors/m365.md`.

### s09-03 Vulnerability scan import
- [ ] Done

**Story:** As an operator, I want scanner exports dropped in a folder and read as structured findings, so that any scanner the organisation already uses can feed patching evidence.

**Acceptance criteria**
- Collector type `vuln-scan` with settings `drop_folder`, `format` (`nessus` or `csv`), `scope` (one of `internet_facing`, `common_apps`, `other_apps`, `operating_systems`), `owner`, and for `csv` a column map (host, finding ID, CVE, severity, first seen, patch published, exploit available, unsupported, scan date).
- Nessus v2 (`.nessus`) and mapped CSV exports both parse to the same immutable `ScanFinding` and `ScanRun` records. A Defender vulnerability management CSV export works through the column map; the docs show the map.
- The newest export in the folder is the current scan; older exports are kept as evidence history. The same content dropped twice is read once (shared with the manual collector).
- Malformed exports fail the run with `vuln_scan.export_invalid` naming the file and line; nothing is published from a partial parse.

**Tests first**
- `NessusParserTests.Reads_hosts_findings_and_exploit_flag`
- `CsvScanParserTests.Column_map_reads_defender_export`
- `CsvScanParserTests.Missing_mapped_column_is_reported_with_line`
- `ScanFolderTests.Newest_export_is_current_scan`
- Contract kit run for `vuln-scan`.

**Functional check**
- Drop a fictional `.nessus` export with `hound manual upload --source vuln-example`; `hound run --only vuln-example --dry-run` lists the parsed scan.

### s09-04 Patching checks from scans
- [ ] Done

**Story:** As a compliance owner, I want scan frequency, patch timeliness and unsupported software measured from scans, so that the patch applications and patch operating systems strategies have evidence.

**Acceptance criteria**
- For each instance's scope, three checks: `vuln.<scope>.scanned_in_interval` (the current scan is newer than `scan_interval_days`), `vuln.<scope>.patched_in_window` (no open finding older than `patch_window_days`, or `exploited_patch_window_hours` when an exploit is available, counted from patch publication, else first seen), and `vuln.<scope>.unsupported_removed` (no finding marked unsupported).
- Each check's detail lists offending hosts and findings, capped at 20 with a count of the rest; the full list is in the evidence file.
- `hound collectors list` shows all twelve check IDs.
- Every new check is mapped in `mappings/essential-eight-2023.yaml`, and the requirements it covers are removed from `manual_only`.

**Tests first**
- `PatchChecksTests.Scan_older_than_interval_fails`
- `PatchChecksTests.Exploited_finding_uses_hours_window`
- `PatchChecksTests.Window_counts_from_patch_publication_when_known`
- `PatchChecksTests.Unsupported_software_fails_naming_hosts`
- `E8MappingTests.Every_reference_check_is_mapped` extended to the `vuln.*` checks.

**Functional check**
- `hound run` with fictional exports for two scopes updates the example Essential Eight audit's patching rows in the local CISO Assistant.

### s09-05 Manual-upload templates for the remaining requirements
- [ ] Done

**Story:** As a system owner, I want a ready template for each Essential Eight requirement that cannot be collected, so that I know what to export, how often, and how to attest it.

**Acceptance criteria**
- `examples/manual-sources/essential-eight/` holds one manual source per remaining area (at least user application hardening, administrative privilege reviews, privileged account restrictions, event log retention, and backup restore tests for organisations without Veeam). Each has a config snippet, a README (what to export, from where, how often, who attests) and the check IDs it supports. Each README is written so it can serve as the runbook an s10-04 evidence plan row points to.
- `hound manual template --source <name>` writes an attestation skeleton (`<file>.attest.yaml`) listing that source's check IDs with empty outcomes.
- Manual check IDs are mapped in `mappings/essential-eight-2023.yaml`, so their rows get attested results rather than `not_assessed`. Requirements still without a check stay under `manual_only` and name their template.

**Tests first**
- `ManualTemplateTests.Skeleton_lists_source_checks_with_empty_outcomes`
- `ExampleManualSourcesTests.Every_template_config_validates`
- `E8MappingTests.Every_manual_only_requirement_names_an_example_source`

**Functional check**
- Write a skeleton for one template, fill it in, upload a fictional export with it, and see the attested result in the example audit.

### s09-06 Documentation
- [ ] Done

**Acceptance criteria**
- `docs/collectors/m365.md` gains the application control checks, the `ThreatHunting.Read.All` permission with its justification, licence notes and limits.
- `docs/collectors/vuln-scan.md`: formats, column maps (including Defender's export), scopes, every check with its limits, and how to schedule exports from common scanners.
- `docs/collectors/manual-templates.md` indexes the templates by Essential Eight strategy.

**Tests first**
- Docs check-ID tests for the M365 and `vuln-scan` collectors.
- Docs lint and link check.

**Functional check**
- Following `docs/collectors/vuln-scan.md` verbatim, import a fictional export and see its checks.

Moved from s08 on 2026-11-22 at the person's request (s08 focuses on Sophos). Was s08-03 and s08-04.

### s09-07 Veeam access
- [ ] Done

**Story:** As the collector, I want Veeam REST API access with a read-only account, so that backup jobs and restore points can be read.

**Acceptance criteria**
- OAuth password grant against the VBR REST API, API version header configurable.
- TLS verification on by default; a pinned certificate thumbprint option for self-signed servers instead of disabling verification.
- Paged jobs, sessions and restore points.

**Tests first**
- `VeeamAuthTests.Sends_api_version_header`
- `VeeamTlsTests.Pinned_thumbprint_accepts_matching_cert`
- `VeeamTlsTests.Mismatched_thumbprint_fails`

**Functional check**
- Run against recordings: jobs and restore points in evidence.

### s09-08 Veeam checks and documentation
- [ ] Done

**Story:** As a compliance owner, I want backup success, retention and restore testing measured, so that backup requirements are evidenced.

**Acceptance criteria**
- `backup.jobs.success_last_24h`: every enabled job succeeded in its last run within 24 hours (window configurable).
- `backup.retention.meets_policy`: restore points span at least the configured retention days.
- `backup.immutable_or_offline_copy`: at least one copy is immutable or on an offline or rotated repository (configured repository names).
- `backup.restore_tested`: a restore test session (SureBackup or instant recovery) within the configured interval; otherwise `Fail`, with the manual collector as the documented fallback.

**Tests first**
- Outcome tests per check; contract kit.

**Functional check**
- Both collectors run together in `hound run` against recordings; status shows both healthy.

**Also (from s08-05):** `docs/collectors/veeam.md` as for M365, with the doc check-ID test; every Veeam check mapped in `mappings/essential-eight-2023.yaml` and removed from `manual_only` where it now covers a requirement; `veeam` added back to `E8MappingTests.ReferenceTypes`.

### Moved from s08

Added 2026-11-22 when s08 closed. The two Sophos tasks are built as checks in the `sophos-central` profile, so they come after s09-13 (Sophos as a profile) and are written in the profile, not in the coded collector. Each still starts by confirming what the Sophos Central API exposes; the Firewall API v1 and Switch API v1 specs could not be reached from the cloud build session, so the person saves them from developer.sophos.com for the build to read (not to commit).

### s09-15 Firewall firmware update timeliness (was s08-13)
- [ ] Done

**Story:** As a compliance owner, I want to know when a firewall has had a firmware update available for longer than allowed, so that ISM-1694 (internet-facing devices patched within two weeks) has live evidence, not only "still supported".

**Acceptance criteria**
- First, confirm in the Sophos Central Firewall API v1 spec how to read available firmware upgrades and when each was released. Known so far: `POST firewall/v1/firewalls/actions/firmware-upgrade-check` with `{"firewalls": [ids]}` returns each firewall's `upgradeToVersion` list and a `firmwareVersions` list. Still to confirm: whether any field gives a release date, and whether the Service Principal Read-Only role may call it (it is a POST that changes nothing). If release dates are not exposed, the check reports an available upgrade without age, and the task says what evidence still has to be manual.
- In the `sophos-central` profile: a `firmware_upgrades` query (fan-out per firewall, or one batched call), joined into the `firewall_firmware` dataset, and `network.firewall.firmware_current`: pass when no firewall has an upgrade outstanding for more than `firmware_update_days` (default 14); fail lists the counts, not names; no firewalls is `not_applicable`.
- Mapped to ISM-1694 for the perimeter firewall, and removed from `manual_only` only if it covers the requirement.

**Tests first**
- Profile tests from fictional recordings: up to date, upgrade within the window, upgrade overdue, no firewalls (`not_applicable`), and the check call refused (unable to assess, not fail).

**Functional check**
- Dry run against the person's tenant.

### s09-16 Sophos switches (was s08-14)
- [ ] Done

**Story:** As a compliance owner with Sophos switches, I want them read like firewalls, so that non-internet-facing network devices (ISM-1501, ISM-1695) have live evidence.

**Acceptance criteria**
- First, confirm from the Switch API v1 spec whether a read-only credential can list managed switches and their firmware. Known so far, from public client libraries only: `switch/v1` has `settings/mac-filtering` and `tasks`, with no switch list; the on-switch REST API is per device with its own credentials, so it is out of scope. If there is no list, close the task with that finding in `docs/collectors/sophos.md` and keep switches a manual source.
- If there is: a `switches` query and `network.switch.firmware_supported` against `supported_switch_firmware` in the profile; no switches is `not_applicable`.

**Tests first**
- Profile tests from fictional recordings, as for firewalls.

**Functional check**
- Dry run against the person's tenant, if it has switches.

### s09-17 Finish the Sophos trial on a real tenant (rest of s08-08)
- [ ] Done

**Story:** As the maintainer, I want the hands-on Sophos trial finished and written up, so that there is a record that Evidence Hound works end to end on a real tenant.

**Done in s08.** The person ran `make trial` through the Sophos dry run against their tenant; that run led to s08-11 (Sophos is read for network devices only) and the s08 discovered work.

**Acceptance criteria (what is left)**
- **(person)** A dry run with the s08-12 collector (version 2.1.0), confirming the Read-Only role can read `common/v1/admins` and `common/v1/roles` and that the predefined role is named Super Admin; outcomes recorded without names.
- **(person)** The real run, results on the mapped requirements in CISO Assistant with evidence attached, and a second `hound scope plan` that plans nothing.
- `docs/evidence/s08/s08-08.md` written from what the person reports: date, versions, the shape of the setup, each step's and check's outcome, compliance counts before and after, defects with links to their fixes. No identifiers.
- If s09-13 has landed by then, the run uses the `sophos-central` profile.

### s09-18 Microsoft 365 trial on a test tenant (rest of s08-10)
- [ ] Done

**Done in s08.** The trial's Microsoft 365 step, `hound add collector --secret-file`, and the tutorial section are built and tested (CI's unattended trial stops before Sophos and Microsoft 365).

**Acceptance criteria (what is left)**
- **(person)** Get a throwaway test tenant (Microsoft 365 Developer Program E5 sandbox if eligible, otherwise a Business Premium trial) and run `make trial` through the Microsoft 365 step.
- Record `docs/evidence/s08/s08-10.md`: date, versions, the kind of tenant, each step's and check's outcome, defects with links to their fixes. No tenant or client IDs, domains, user or device names.
- Fix any defect test-first; correct the tutorial wherever it was wrong or unclear.

### s09-27 Record a Microsoft 365 tenant with Dev Proxy and publish it scrubbed (added 2026-11-22)
- [ ] Done

**Story:** As a maintainer, I want my trial tenant's Microsoft Graph answers recorded once, scrubbed and published, so that `hound`'s Microsoft 365 tests and tutorial run offline against realistic, internally consistent data, and others can learn from it.

**Background (checked 2026-11-22):** the public Dev Proxy Graph mocks (`waldekmastykarz/graph-mocks`, MIT, also published as the PnP "Microsoft Graph sandbox mocks" sample, updated for Dev Proxy v4) cover about 3,200 v1.0 endpoints, including conditional access, Intune `deviceManagement`, authentication method reports, directory roles and secure score. They are built from the Graph API documentation's examples, one example per endpoint, so the same users and devices do not appear across endpoints. Use them for response shapes and credit them; they do not replace a consistent recording.

**Acceptance criteria**
- `docs/tutorials/record-m365-with-dev-proxy.md`: step-by-step for a Business Premium trial tenant: install Dev Proxy v4, trust its root certificate in the dev container (`update-ca-certificates`), run it with `--ip-address 0.0.0.0` and `MockGeneratorPlugin`, point `hound` at it through `HTTPS_PROXY`, record a run, then replay offline with `MockResponsePlugin` (confirm that a generated file replays as is).
- A scrub step (`hound recordings scrub` or a script under `tools/`): replaces tenant IDs, domains, user and device names, emails, IP addresses and serials with consistent fictional `example.com` values (the same input always gives the same output, so joins still work), and fails if anything from a denylist or the tenant's own domains remains. The `org-data` check runs on the result.
- The scrubbed recording is committed as the Microsoft 365 test fixtures and used by at least one replay test; the tutorial's Microsoft 365 section can run against it offline.
- Credits and licence notes for any reused public mocks.

**Order:** after the Sophos onboarding (s09-25); needs the person's recording from s09-18.

### s09-30 Explain a config change in plain English: `hound explain` (added 2026-11-22)
- [x] Done

**Story:** As a GRC team member reviewing a lot of new configuration, I want a plain-English summary of what a change does and what looks risky, so that I review config quickly and approve only what I understand.

**Acceptance criteria**
- `hound explain` reads a config diff (`git diff`, or two files) or the output of `hound plan` or `hound scope plan`, and writes a review summary, for example "marks 12 Essential Eight requirements not applicable on the Microsoft 365 audit because ThreatLocker provides them".
- It flags anything risky or inconsistent for the reviewer: requirements newly marked not applicable, removed checks or controls, deletions in the plan, a system without an owner, a supplier not yet approved, a mapping that no longer matches a profile's checks.
- Review only: it never edits config, never applies, and never runs in collection. It uses the onboarding model connection (`onboarding.yaml`, s09-21) through the same gate: the outbound guard, the deny terms, the session record and `--show-requests`.
- The deterministic plan output stays the source of truth: the summary says so, quotes the plan's counts, and a reply that names an object the plan does not contain is reported as `llm.invalid_reply`.
- It uses the right skill for what it explains, from the same skills system as onboarding (s09-26): a new `explain` step, with skills chosen by what the diff or plan touches (third parties: supplier assessment and assurance; not-applicable rows: applicability; profiles: profile authoring; mappings and audits: the framework's mapping skill such as Essential Eight ML2). Organisation skills are included, `--skill <name>` adds or narrows the choice, and the summary lists every skill used with its source, version and SHA-256. Default explain-oriented skills ("reviewing a config change") are added where the existing ones do not cover the review.

**Tests first**
- `ExplainTests.Summary_counts_match_the_plan`
- `ExplainTests.Object_not_in_the_plan_is_an_invalid_reply`
- `ExplainTests.Secrets_and_identifiers_are_never_sent`
- `ExplainCommandTests.Reads_a_diff_or_a_saved_plan`
- `ExplainSkillTests.Skills_follow_what_the_change_touches`
- `ExplainSkillTests.Skill_option_overrides_the_choice_and_is_recorded`

**Functional check**
- With a fake model, `hound explain` on a scope plan of the example repo prints a summary whose counts match the plan, and `--show-requests` shows no secret or identifier.

**Order:** after the first usable onboarding path (s09-21, s09-26, s09-22, s09-24).

**As built (2026-11-23)**
- `hound explain [<plan-or-diff> | <before> <after>] [--repo] [--framework] [--skill]... [--no-model] [--show-requests] [--config]`, reading a file, two versions of one file, or standard input (`git diff | hound explain`). It reads `hound scope plan` output (Markdown or JSON), `hound plan` output (JSON) and unified diffs.
- `ChangeReader` reads counts, objects, topics and flags in code: not applicable rows, deletions, removed check or requirement lines in mappings and profiles, and supplier contracts that are not active. `Explanation` sends them through the onboarding gate (`onboarding/explain/requests/`), with object IDs, email, IP and MAC addresses, deny terms and secret values replaced first (`OutboundGuard.Redact`), and refuses a reply that names an object the plan or diff does not contain.
- A new `explain` skill step and default skill `reviewing-a-config-change`; `ExplainSkills.Choose` adds the supplier, applicability, profile or mapping skills by what the change touches, and `--skill` chooses instead. The summary lists each skill with source, version and SHA-256.
- Also flagged: a system created or updated without an owner (from the plan's fields), and a check a diff removes from a profile that a mapping in the repo's `mappings/` still uses.

**Functional check (2026-11-23)**
- `hound explain plan.md --repo <copy of the example repo> --show-requests` with a fake Anthropic server: prints "The plan is the source of truth: 1 to create, 0 to update, 0 to delete, 0 drifted.", the not applicable flag, the fake summary and four skills; the requests shown and saved (`001-explain.request.json`) hold no email address from the plan's access review.
- `hound explain before.yaml scope/audits/example-assets-essential-eight-2023.yaml`: "1 file changed, 227 lines added, 1 line removed" and "Marks 2 requirement(s) not applicable".
- `OnboardFunctionalTests` now explains the real scope plan from the local CISO Assistant with `--no-model` and checks the counts match.

### s09-31 Draft organisation skills from onboarding (added 2026-11-22)
- [x] Done

**Story:** As a GRC team member, I want onboarding to help me write skills about my own organisation (its business, risk appetite, IT and AI landscape and conventions), so that later onboarding and `hound explain` give advice that fits us rather than generic advice.

**Acceptance criteria**
- `hound skill draft` (and an optional last step of environment onboarding, s09-28) asks short questions and reads what the config repo already holds (the capability map, systems, suppliers, AI systems in use, naming and approval conventions), then drafts one or more organisation skills, for example `organisation-profile`, `risk-appetite`, `it-landscape` and `ai-landscape`, each tagged with the steps it applies to (`supplier`, `system`, `applicability`, `explain`).
- Drafts are written to the organisation skills folder in the private config repo (`/config/skills`, s09-26), never anywhere in this repo. The person reviews, edits and commits them; nothing is used until it is in that folder and passes `hound skill validate`.
- Everything sent to the model passes the same gate as onboarding (s09-21): the outbound guard, the deny terms, the budget, the session record and `--show-requests`. A draft that holds a secret, tenant ID, host name, IP address or a person's details is refused by `hound skill validate` with `skill.sensitive_content`, and the draft step strips those before writing.
- Each draft carries a version, starting at 1; re-drafting an existing skill shows a diff and bumps the version. Onboarding records (s09-24) and `hound explain` summaries (s09-30) already list each skill's source, version and SHA-256, so the organisation skills used are traceable.
- `--no-model` writes the same skills as templates with the questions as headings, for teams without a model key.

**Tests first**
- `OrganisationSkillTests.Draft_reads_the_capability_map_systems_and_suppliers`
- `OrganisationSkillTests.Sensitive_content_is_stripped_and_refused`
- `OrganisationSkillTests.Redraft_shows_a_diff_and_bumps_the_version`
- `SkillDraftCommandTests.Writes_only_to_the_organisation_skills_folder`
- `SkillDraftCommandTests.No_model_writes_templates`

**Functional check**
- With a fake model, `hound skill draft --skills <temp>` on the example repo writes draft skills that pass `hound skill validate`, and `--show-requests` shows no secret or identifier.

**Order:** after s09-30.

**As built (2026-11-23)**
- `hound skill draft [--skills <folder>] [--repo] [--only <name>]... [--no-model] [--show-requests] [--config]` drafts `organisation-profile` (supplier, system, applicability, explain), `risk-appetite` (supplier, applicability, explain), `it-landscape` (system, applicability, explain) and `ai-landscape` (supplier, system, explain). It asks each skill's questions, then sends the answers and the repo's `scope/setup.yaml`, `scope/third-parties/*.yaml` and `scope/capabilities.yaml` (when s09-28 adds it) through the onboarding gate, with requests in `onboarding/skills/requests/`.
- `OrganisationSkills.Draft` strips secrets, deny terms, object IDs and email, IP and MAC addresses before writing; versions start at 1 and a re-draft prints the diff and bumps it. Drafts are parsed as skills before they are written, and only into the skills folder.
- `hound skill validate` now refuses (exit 3, `skill.sensitive_content`) a skill holding organisation data, instead of warning.
- Host names are not stripped (they cannot be told from public names such as a vendor's domain); the drafting instructions ask the model to leave them out, and deny terms catch the organisation's own domains. Environment onboarding (s09-28) offers the draft as its last step when it lands.

**Functional check (2026-11-23)**
- With a fake Anthropic server, `hound skill draft --repo <copy of the example repo> --skills <temp> --show-requests` (answers piped in) wrote four skills at version 1; the fake reply's `10.20.30.40` was written as `[ip]`; `hound skill validate --skills <temp>` printed "11 skills valid (4 from ...), 0 warnings." and exited 0. The requests shown hold only the example repo's `example.com` addresses.

### s09-32 Onboarding tutorial: Example Pty Ltd's privately hosted GRC platform (added 2026-11-23, refined the same day)
- [ ] Done

**Story:** As a GRC team member learning hound, I want a complete onboarding tutorial for Example Pty Ltd, whose CISO Assistant and Evidence Hound run privately in the cloud and reach the office's Sophos XGS firewall over a site-to-site VPN, so that I can follow one realistic stack end to end and remember that the compliance platform is itself in scope.

**Acceptance criteria**
- A tutorial, `docs/examples/onboarding-tutorial.md`, walks through `hound onboard` for this stack, step by step, with the commands and the answers typed:
  - suppliers first under `scope/third-parties/`: the fictional cloud hosting provider, Sophos, and the CISO Assistant and Evidence Hound open-source projects (no contract value), each with levels and reasons;
  - the GRC platform (CISO Assistant and the `hound` runner on one private cloud VM) as a system, with its Essential Eight ML2 applicability: for example application control and Office macros not applicable to a Linux container host with their justifications, patching and backups as manual evidence, admin MFA through the identity provider as another system;
  - the office XGS firewall as a system, collected through the `sophos-central` profile, with its applicability;
  - then review, `hound scope validate`, `plan` and `apply`.
- The tutorial has a deployment diagram and the network path: a private network with no public address or inbound port, the site-to-site IPsec VPN between the cloud network and the XGS, which traffic goes where (operators to CISO Assistant over the VPN; the runner to Sophos Central's API over outbound HTTPS; the runner to CISO Assistant inside the private network), TLS through Caddy, the database and its backups, where secrets come from, the runner's schedule, and which controls cover each part.
- The example compliance repo holds the files the tutorial produces (systems, suppliers, audits, decisions and onboarding records), as `hound onboard` writes them. Everything is fictional (Example Pty Ltd, `example.com`, documentation IP ranges) apart from the public product names; no employer detail.
- `hound scope validate` passes on the example repo, and `docs/onboarding.md` points to the tutorial as its worked example.

**Tests first**
- `ExampleRepoTests.Tutorial_systems_and_suppliers_are_declared`
- `ExampleRepoTests.Tutorial_audits_validate_with_justified_not_applicable_rows`
- `TutorialTests.Onboarding_tutorial_names_only_files_that_exist`
- `TutorialTests.Onboarding_tutorial_uses_only_documentation_addresses`

**Functional check**
- Following the tutorial with a fake model on a copy of the example repo reproduces the committed files, and `hound scope plan` against the local CISO Assistant shows them as new.

- **Cloud: Oracle Cloud Infrastructure (OCI)**, at the person's suggestion, so the tutorial can be run for real; kept generic enough that another cloud needs only small changes (a short "on another cloud" note). On OCI: a VM in a private subnet with no public IP, OCI Site-to-Site VPN (IPSec) to the XGS, admin access through OCI Bastion or over the VPN only, and Oracle as a supplier in the walk-through. Use an x86 (AMD) shape, as the person prefers, sized for CISO Assistant's Compose stack; the tutorial names the shape and its approximate monthly cost, checked against Oracle's price list when written. No Arm image work. No real tenancy, OCIDs, regions tied to the person, or addresses in the public repo: documentation IP ranges and placeholders only.

**Order:** after the hands-on Sophos path (s09-25).

## Discovered work

- Open items from s08's discovered work (trial token refresh, the seed's compliance owner, HTTP logging at `info`, wizard prompts, trial re-runs, evidence on applied controls, the "unlisted rule" wording, `HOUND_CA_URL` in the dev container) are listed in [s08](s08-sophos-veeam-collectors.md#discovered-work); pick them up here or in s12.

- The config check's long-run rule (32+ letters, digits, `/`, `_`) also matches a deep folder path that contains digits, for example a `profiles_path` under a generated temp folder. Short mounted paths such as `/config/profiles` are fine. Consider excluding `/` from the run for `_path` settings (found in s09-10).

- Scope export (`hound scope export`) does not yet cover third parties or the organisation's own entity; a tenant that already has suppliers in CISO Assistant has to write their files by hand (found in s09-19).

- The Anthropic adapter asks for replies of up to 16,000 tokens without streaming. Steps that need longer replies (a whole drafted profile) should stream through the SDK's streaming call (found in s09-21).

- The official Anthropic .NET SDK sends the API key as `Authorization: Bearer`, and reads `ANTHROPIC_BASE_URL` from the environment when no base URL is set. `hound onboard config --check` confirms the key works end to end; note both in the onboarding guide if a provider proxy is used (found in s09-21).

- Vendor documents kept during `hound onboard` are saved with their SHA-256, source and retrieval date, but are attached to the supplier assessment as evidence only once supplier assessments exist (s09-20). The model reads only text documents (HTML, text, Markdown); PDFs are kept but not read (found in s09-22).

- An Essential Eight audit that declares `groups: ["2"]` is stored by CISO Assistant as groups 1 and 2, so `hound scope plan` shows it as changed after every apply. `hound scope validate` should warn, or the planner compare the groups CISO Assistant keeps; until then declare ML2 as `["1", "2"]` (found in s09-24's functional test).

- `hound onboard` reads OpenAPI 3 files only. Other API specifications, documentation saved as text, and specifications fetched from allowed URLs need further readers; Sophos Firewall's XML API (s09-15) is the first that will need one (found in s09-22).

- `hound run --dry-run` reports only counts of passed, failed and other checks; it does not print each check's outcome and observation. An operator trying a new profile needs them; `hound profile test` (s09-12) or a `--show-checks` option would help (found in s09-11).

## Sprint review
