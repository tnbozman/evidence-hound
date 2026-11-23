# s09 Essential Eight gap collectors

Status: In progress

**Goal:** close the largest Essential Eight ML2 gaps left after the Microsoft 365, Sophos and Veeam collectors: application control evidenced from Intune and Microsoft Defender for Endpoint, patching evidenced from imported vulnerability scans, and a ready-made manual-upload template for every requirement that stays manual. After this sprint every ML1 and ML2 row in the example Essential Eight audit gets a result from a check or an attested manual source.

New sprint, added in the release 1 reorder. Application control extends the Microsoft 365 collector (same app registration). Vulnerability scans are imported by a new `vuln-scan` collector in `src/Collectors/EvidenceHound.Collectors.VulnScan`, which reuses the manual collector's drop folder and attestation handling. Recorded, fictional responses and exports only (Example Pty Ltd, `example.com`).

The patch windows and scan intervals below are settings, not constants, so the checks stay framework-neutral. The example config sets them to the Essential Eight ML2 values, and the mapping file quotes the ISM control each one comes from.

### REST collector (s09-09 to s09-14)

These tasks come first in this sprint, before s09-01, so the Microsoft 365 application control checks and the Veeam tasks (since dropped) could be written as profile checks instead of new code. They build the generic `rest` collector agreed in [`docs/design/rest-collector.md`](../design/rest-collector.md): every query is called once per run, evidence datasets are extracted from one or more responses, and checks test dataset fields, giving pass, fail or unable to assess. Profiles are YAML loaded only from the organisation's profiles folder; this repo keeps a tested public library in `profiles/` to copy from. There are no runtime code plugins. Where the Veeam tasks (s09-07, s09-08) and s09-01/02 say "collector", read "profile in the library" once these tasks are done.

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
- [x] Done

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

**As built**
- Scope files: `forced_tier: {tier, reason}` and `assessments:` on a third party; `solution` and `supplied_through` on a system ([scope.md](../scope.md#supplier-reviews-and-tiers)). CISO Assistant v4.3.4's field is `conclusion` (`blocker`, `warning`, `ok`, `not_applicable`) with an `expiry_date`; approval needs `ok`, unexpired, no overdue later review, and an active contract.
- Reviews with `review_every` are keyed `<ref_id>-<yyyy-MM>` by due date (CISO Assistant has no assessment revisions); the next opens once today reaches the previous due date. A review created with a questionnaire sends `create_audit`, so CISO Assistant makes the questionnaire's audit itself.
- `SupplierReview.Check` (pure, in Core) runs in `scope plan`, `scope apply` and the saved-plan apply. A refusal stops before planning, so the order is: apply the supplier and its review, conclude it in CISO Assistant, then add the system. Lapsed approvals and differing tiers appear under **Suppliers** in the Markdown plan and as `suppliers` in JSON.
- Extra tests: `SupplierClientTests.Reads_supplier_assessments_with_their_conclusion_and_each_suppliers_tier_as_observed`, `SupplierClientTests.Creates_a_supplier_assessment_with_its_questionnaire_audit_and_never_sends_a_conclusion`, and three `ScopePlanCommandTests` for refusal and the Suppliers section.

**Functional check:** `SupplierGateFunctionalTests` (CI, local CISO Assistant, `HOUND_TODAY=2026-07-15`): applying a fictional supplier with a yearly review due 2026-06-30 creates `EA-FT-GATE-2026-06` and opens `EA-FT-GATE-2027-06`, each with a questionnaire audit; a system it supplies is refused with `system.supplier_not_approved`; after the first review is concluded `ok` through the API, the system plans, applies and re-plans to no changes.

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
- [x] Done

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

**As built so far**
- `scope/capabilities.yaml` (loader, schema, `hound scope validate`), `CapabilityMap` (Core, pure): mitigations are the framework's requirement groups by parent node; every mitigation decided once with an approver; per-system not-applicable rows "Provided by <system> (audit <ref>)", skipping hand-written rows and groups the audit leaves out; gaps listed apart. A system named before it is onboarded is pending, not an error, so the map can be written first.
- `CapabilitySignals` (Onboarding, pure): Microsoft licences and Intune configuration kinds suggest the Microsoft 365 system; systems tagged `capabilities: ac, rb` are suggested first. `CapabilityAnswers` turns a proposal and the person's answer into a decision; a proposal never decides.
- `hound onboard environment --signals <recorded Graph answers> --no-model` decides each mitigation, writes `scope/capabilities.yaml` after each, appends the rows to each audit with the YAML editor (comments kept), and lists systems to onboard with their supplier and the gaps. Default skill `environment-capabilities`.
- Live read-only Graph lookups: `--graph <collector>` signs in as that `m365` collector of `hound.yaml` (its settings and secrets) through `EnvironmentLookup` in the M365 collector project, reading `v1.0/subscribedSkus` (enabled or warning SKUs; `Organization.Read.All`, onboarding only) and the `@odata.type` of `v1.0/deviceManagement/deviceConfigurations` (none without Intune). `--graph` and `--signals` together are refused. Tests: `EnvironmentLookupTests` (WireMock), `OnboardEnvironmentTests.Graph_signals_come_from_the_m365_collector_in_hound_yaml`.
- Model proposals: `CapabilityProposals` (Onboarding) sends one request through the gate with the mitigations, systems and signals, guided by the `environment` skills; a reply about an unknown mitigation or one twice is refused; a system proposal without systems is no suggestion; mitigations it leaves out keep the signals' suggestion. Accepting records "from the model's proposal: <reasoning>". `--config` is the onboarding config, as in `hound onboard <system>`; the deployment config for `--graph` is `--deployment`.
- `hound scope plan` lists the map's gaps ("Capability gaps", JSON `gaps`) through `CapabilityMap.Gaps`.
- Capability files keep apostrophes readable (relaxed JSON escaping in the quoted YAML).
- Not done here, noted under Discovered work: Defender for Endpoint onboarded devices as a signal.
- Functional test: `CapabilityMapFunctionalTests` (recorded signals, no model, then `hound scope plan` against the local CISO Assistant).

**Functional check (so far):** on a copy of the example repo with recorded answers (`SPE_E3`), `hound onboard environment --no-model` wrote `scope/capabilities.yaml` and 14 application control rows in `ex-e8-2026.yaml` as "Provided by Example app control (no audit yet; …)", listed the app control product to onboard and patch applications as a gap; `hound scope validate` passed.

**Functional check (CI run 311, 2026-11-23):** `CapabilityMapFunctionalTests` against the local CISO Assistant: on a copy of the example repo with recorded `SPE_E3` signals and no model, `hound onboard environment` recorded application control as provided by Example app control and patch applications as a gap; `hound scope plan scope --config hound.yaml` then exited 0 with the not-applicable rows "Provided by Example app control" and patch applications under "Capability gaps". The first CI run found that rows for requirements the audit's mapping computes conflicted with it (`scope.result_conflict`); those are now left to the mapping and listed, and `hound scope validate --config hound.yaml` passes after onboarding.

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

- Result (2026-11-23): against the recorded `example.com` tenant (`RecordedTenant`, the one the collector's tests replay; s09-27's recording is not ready), `M365CapabilityTests.Unused_capability_is_not_applicable_naming_the_provider` routes MFA and Office macros to other products: the four MFA checks and the macro check answer `not_applicable` with "Not judged on Microsoft 365: scope/capabilities.yaml records mfa as provided by Example identity provider (audit EX-IDP-E8-2026)", with no evidence, while the ASR check (macros and user application hardening) and the rest pass as before. With no map, all 11 checks pass as before. The full unit suite passed (1301 tests) and lint is clean. **Still to do:** a run against the maintainer's own tenant, after `hound onboard environment --graph`; the task stays unticked until then.
- As built: `CollectorCapabilities` on the collector context (Abstractions) lists the capabilities another product provides, by mitigation code, with its name; `CapabilityRouting.For` (Core, pure) works them out from the map for one instance: its own when the route names it or its `system` setting is among the decision's systems; manual evidence and gaps are never a collector's. `CollectStep.Scope` is the scope with its map, read by `hound run`, `collect` and `serve` when `hound.yaml` names a scope folder holding `capabilities.yaml`. Each m365 check names its capabilities (`mfa`, `rap`, `pos`, `rmom`, `uah`), and a check whose every capability is elsewhere is not applicable, naming the provider. The m365 collector has a new optional `system` setting. `hound onboard environment --graph <collector>` records `evidence: collector` and `route: <collector>` on an accepted Microsoft 365 suggestion.
- Deviations: the maintainer chose (2026-11-23) that these rows answer not applicable naming the provider, as this task was written, rather than staying open with `provided_by` as s09-36 does for hand-written rows; the mapping's own results are what reach the audit, so `scope.other_system_as_not_applicable` (hand-written rows only) does not apply to them. `hound onboard m365` is not a separate command: `hound onboard environment --graph <m365 collector>` is that path, since it already reads the licences and policies and the person confirms each mitigation; the test is `OnboardM365Tests` in the onboarding project. The capability codes are the Essential Eight mitigation codes the map uses; a map for another framework routes nothing until its codes are mapped. The m365 collector has no application control, backup or endpoint detection checks today, so those groups are empty. The tutorial has no Microsoft 365 section; the choice is explained in `docs/collectors/m365.md` and `docs/onboarding.md`. A broken `capabilities.yaml` is reported by `hound scope validate`; a run uses what parses.

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
- [x] Done

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

**As built**
- `ProfileDraft` (Onboarding): the request (system, API summary, accepted checks, shapes; on later rounds the previous draft and its problems), the reply schema (`profile_yaml`, `notes`), and the loop that checks each draft and sends problems back for at most `llm.max_rounds` (new key, default 3) drafts. `ShapeInputs` sends only `ResponseShape` shapes; dates now count as data, so their values are never sent.
- `SampleRun` (rest collector): runs a profile's queries once through a recording handler; identities hashed with the evidence hash, `access_token`/`refresh_token`/`id_token` replaced, the API's address as `{{api}}`; a failed query or an answer holding a secret keeps nothing. `WriteTestCase` writes the test case with placeholder secrets and the confirmed results.
- `hound onboard profile <system>` (`--name`, `--profiles`, `--set`, `--secret`, `--sample`, `--rounds`, `--show-requests`): validate, then (with the go-ahead) sample, ask the reviewer to confirm each check's result, save `profiles/<name>.tests/onboarding.yaml` and run the test, every round. Exit 1 when problems remain.

**Functional check (recorded):** `OnboardProfileTests.A_wrong_path_is_fixed_in_the_second_round_and_the_confirmed_results_become_a_passing_test`: a fake Anthropic model's first draft reads `devcies`; the sample run against the recorded Example Asset API fails with `profile.sample_failed`, which goes back in round 2; the corrected draft samples, the four confirmed results are saved with hashed recordings (no `alex@example.com`, no secret), and `hound profile test example` then exits 0. Without the go-ahead the API is never called.

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

**Sophos firewall run (2026-11-23):** onboarded end to end on the person's tenant; see [`docs/evidence/s09/s09-25.md`](../evidence/s09/s09-25.md). The functional check is met with the shipped `sophos-central` profile (3 of 3 cases pass, both checks pass on the tenant); the model-drafted profile did not validate (discovered work), so the task stays open until drafting produces a passing profile.

**As built so far (2026-11-23).** The docs part is done: `OnboardingDocsTests` checks that every option of `hound onboard`, `onboard config`, `onboard environment` and `onboard profile` is in `docs/onboarding.md`, that its relative links resolve, and that it has a tutorial on the example API. The tutorial ("Tutorial: onboard the example API") was followed as written against a copy of `examples/compliance-repo` with `--no-model` and `--group 1`: the supplier stop, then 48 decisions (1 API check, 47 manual), the audit, `scope/setup.yaml` and the record written, `hound scope validate scope` valid, and `hound profile test example` passing both cases. Still open: the person's own Sophos firewall run, and the discovered work it shows.

### s09-43 Representative Microsoft 365 dev server (added 2026-11-23)
- [ ] Done

Added at the maintainer's request (2026-11-23): s09-01 to s09-04 and the Microsoft 365 checks should not wait on a test tenant (s09-18).

**Story:** As the maintainer without a test tenant, I want a fictional Example Pty Ltd tenant that answers Microsoft Graph offline with realistic, internally consistent data, so that Microsoft 365 controls and their automated evidence can be built and tested against representative configurations.

**Acceptance criteria**
- One fictional tenant described once (users, groups, licences, devices, Conditional Access, role assignments, Intune policies), from which every Graph collection the m365 collector reads is generated with the same IDs everywhere, so joins between collections hold. Two variants: `compliant` and `gaps`, the second a short list of named deviations, each expected to fail a named check.
- Intune and Conditional Access settings for `compliant` come from the ASD Blueprint for Secure Cloud's published configurations (`static/content/files`: the Intune policy exports and Microsoft365DSC configurations), pinned to a commit and kept with their attribution (CC BY 4.0, Commonwealth of Australia); a third-party file under another licence is used only under its own terms, or not at all.
- Response shapes, paging (`@odata.nextLink`) and errors (403 for a missing licence or permission, 429 with `Retry-After`) follow Microsoft's Graph documentation and the public Dev Proxy Graph mocks (MIT), credited.
- Served by the existing test server in unit tests, and on a local port in the dev container (`make m365-dev`), so `hound collect --dry-run` with the instance's `graph_url` pointed at it runs end to end. No sign-in leaves the machine.
- `docs/tutorials/m365-dev-server.md`: what the tenant holds, the two variants, how to add a setting or a deviation, and what it cannot show (throttling patterns, licence quirks and results of a real tenant), which s09-18 and s09-27 still check.

**Tests first**
- `M365DevTenantTests.Every_collection_the_collector_reads_is_served`
- `M365DevTenantTests.Ids_are_the_same_across_collections`
- `M365DevTenantTests.Compliant_tenant_passes_every_check`
- `M365DevTenantTests.Each_deviation_fails_its_named_check`
- `M365DevTenantTests.Blueprint_settings_keep_their_source_and_attribution`

**Functional check**
- `make m365-dev`, then `hound collect --dry-run` for `example-m365` against each variant: every check as the variant expects.

**Order:** before s09-01, which adds its application control collections to this tenant. s09-27 then checks the tenant against a real recording instead of being its only source.

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
- Moved to s10-04 (2026-11-23, maintainer's decision): the templates and the evidence plan describe the same thing from two ends, so they are built together. Kept here as a record.

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
- ~~`docs/collectors/manual-templates.md` indexes the templates by Essential Eight strategy.~~ Moved to s10-04 with s09-05.

**Tests first**
- Docs check-ID tests for the M365 and `vuln-scan` collectors.
- Docs lint and link check.

**Functional check**
- Following `docs/collectors/vuln-scan.md` verbatim, import a fictional export and see its checks.

Moved from s08 on 2026-11-22 at the person's request (s08 focuses on Sophos). Was s08-03 and s08-04.

### s09-07 Veeam access
- Dropped (2026-11-23): the maintainer does not want Veeam collection. Kept here as a record; not built. Backup evidence stays manual (s09-05 templates).

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
- Dropped (2026-11-23): the maintainer does not want Veeam collection. Kept here as a record; not built. Backup evidence stays manual (s09-05 templates).

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
- Confirmed 2026-11-23 from the published spec (`https://developer.sophos.com/assets/specs/firewall-v1.yaml`, OpenAPI 3.0.0, version 1.5.0): each `firmwareVersions` item has only `version`, `size`, `bugs` and `news`, so **no release date is exposed**. An upgrade's age has to come from when hound first saw it offered (kept in the collector's local state between runs), which starts counting only once the check is running; say so in the observation. Whether the read-only role may call the POST is still to confirm on the person's tenant.
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
- [x] Done

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

**As built so far (2026-11-23).** Tutorial `docs/examples/onboarding-tutorial.md`: the deployment (mermaid diagram, which traffic goes where, each part and what covers it), building it on OCI (`VM.Standard.E5.Flex`, 2 OCPUs, 16 GB, 100 GB balanced boot volume: about USD 71 per month from Oracle's price list on 2026-11-23; private subnet with no public IP, Site-to-Site VPN to the XGS, Bastion, NAT and service gateways, backups to Object Storage, Terraform in its own repository) and "On another cloud"; then suppliers first (`SUP-CISO-ASSISTANT` and `SUP-EVIDENCE-HOUND` by hand, no contract value; `SUP-ORACLE` and `SUP-SOPHOS` drafted by `hound onboard`, then reviewed), `GRC-PLATFORM` (37 manual, 61 not applicable, 9 provided by Example 365 or the Example laptops), `OFFICE-FIREWALL` (ISM-1501 and ISM-1883 as API checks through `sophos-central`, 40 manual, 61 not applicable, 4 another system), drift through `terraform-drift`, then validate, plan and apply. Addresses are documentation ranges only.

**Deviation:** the files the tutorial writes are in `examples/onboarding-tutorial/` (same paths as a config repo, with `answers/` to replay each run), not inside `examples/compliance-repo`. The template is what a new repo copies and what the functional tests and `make ca-up` apply in one go; systems behind the supplier gate are refused until their suppliers' reviews are concluded, so in the template they would block every first apply. `ExampleRepoTests.The_template_holds_none_of_the_tutorials_systems` keeps it that way.

Functional check, run 2026-11-23 with `--no-model` (the answers files stand in for the fake model): following the tutorial on a fresh copy of the template reproduces `scope/setup.yaml`, both audits and both onboarding records exactly (apart from the schema comment line), `hound config validate` and `hound scope validate scope --config hound.yaml` pass (4 third parties, 5 audits), and `hound profile test` passes for `sophos-central` and `terraform-drift`. `TutorialTests.Onboarding_tutorial_answers_reproduce_the_committed_files` repeats this in the unit suite. The plan against CISO Assistant is `OnboardingTutorialFunctionalTests` (suppliers applied, reviews concluded ok, then the plan creates both systems and audits), run in CI: passed in CI run 45121842725 (2026-11-23, head 6648e42), with the whole functional suite.

**Order:** after the hands-on Sophos path (s09-25).

### s09-33 Infrastructure drift from the IaC pipeline's plan artefact, without a Terraform dependency (added 2026-11-23)
- [x] Done

**Story:** As a GRC team member, I want `hound` to tell me whether the cloud hosting our GRC platform still matches its reviewed infrastructure code, without `hound` running Terraform or holding cloud credentials or state, so that drift is evidence like any other check.

**Acceptance criteria**
- The organisation's IaC pipeline runs `terraform plan -refresh-only -json` (or `-detailed-exitcode` plus the JSON plan) and publishes the plan JSON as a build artefact. `hound` never runs Terraform, never reads state, and never holds cloud credentials.
- A `terraform-drift` REST profile fetches the artefact over HTTP using the engine's existing job pattern (`async`: start, poll the status until done, then fetch the result). Profiles cover GitHub Actions first (workflow runs and artifacts); GitLab and Azure DevOps follow by profile only, with no code per vendor.
- Two modes: read the newest existing artefact of a named workflow (the default, read only), or trigger the workflow first (`workflow_dispatch`), poll the run to completion, then download its artefact.
- The trigger is the only write any profile makes, so it needs an opt-in flag in the profile and in the collector's settings (off by default), and the docs give the narrowest token: run that one workflow and read its artefacts, nothing else. Hosts are declared as for any profile (`profile.host_not_allowed` otherwise).
- If the engine cannot yet read a downloaded zip (GitHub wraps artefacts in one) or a file body, that is added as a small engine extension (`format: zip` naming the member, size caps as today), not a new collector.
- Checks: no drift (no `resource_drift` entries and no changed resources) passes; drift fails and names each changed resource address and action; a missing, failed or stale artefact (older than a `max_age` setting) is unable to assess, saying which.
- Each piece of drift evidence records its provenance: the IaC repository, the commit SHA the plan was made from, and the pipeline run ID (read from the run, not trusted from the artefact's name), so it is clear which version was assessed when the Terraform lives in its own repository.
- The OCI tutorial (s09-32) uses it for Example Pty Ltd's GRC platform, with a fictional workflow and a recorded artefact; no real tenancy, repository or token. It presents separate repositories as the recommended setup (the IaC repository with its pipeline, and the GRC config repository `hound` reads), and notes that a monorepo works too: the workflow and artefact are named the same way.

**Tests first**
- `TerraformDriftProfileTests.No_drift_passes`
- `TerraformDriftProfileTests.Drift_fails_naming_each_changed_resource`
- `TerraformDriftProfileTests.Missing_failed_or_stale_artefact_is_unable_to_assess`
- `TerraformDriftProfileTests.Evidence_records_repository_commit_and_run`
- `RestEngineTests.Zip_member_is_read_with_the_size_caps`
- `RestEngineTests.Trigger_is_refused_unless_both_profile_and_settings_opt_in`
- `RestEngineTests.Trigger_polls_the_run_then_reads_its_artefact`

**Functional check**
- Against a recorded GitHub Actions API (WireMock, fictional repository), `hound run --dry-run` with the `terraform-drift` profile reports pass for a plan with no drift and fail naming `oci_core_instance.grc` for one with drift; with triggering off, no POST is sent.

**As built so far (2026-11-23).** Engine: `format: zip` with `member:` reads one file of a zip answer, the archive and the unzipped member both under `max_bytes` (`rest.zip_member_missing`, `rest.response_too_large`); `redirect_hosts:` follows a GET's redirect to signed storage with a plain, unsigned request (GitHub artefact downloads); `write: true` marks a query that changes something, and it runs only when the instance sets `allow_writes: true` (otherwise nothing is sent and the check is unable to assess). Profiles `terraform-drift` (read the newest completed run, its `drift-plan` artefact and `plan.json`) and `terraform-drift-trigger` (`workflow_dispatch` with `return_run_details`, poll the run, then read), both with check `infrastructure.drift.none` through the code check `terraform.no_drift` (over `[run, plan]`; provenance from the run). Test cases replay folder recordings as zips (`recordings/plan-drift.zip/plan.json`). Guide: `docs/collectors/terraform-drift.md`.

Functional check, run 2026-11-23 against a local fake of the GitHub Actions API serving the profile's recordings (fictional `example/grc-infra`), with `hound run --no-publish` and `hound status`:

```console
$ hound run --config hound.yaml --no-publish     # plan-clean
grc-infra-drift  ...  succeeded  1  0  -         # infrastructure.drift.none: pass
$ hound run --config hound.yaml --no-publish     # plan-drift
grc-infra-drift  ...  succeeded  0  1  -         # fail: "1 resource differs ...: oci_core_instance.grc (update). Plan from example/grc-infra at commit 0123..., run 7001."
$ hound run --config trigger.yaml --no-publish   # terraform-drift-trigger, no allow_writes
grc-infra-drift  ...  succeeded  0  0  -         # unable to assess; the fake API received no request at all
```

The OCI tutorial (s09-32) uses it for the GRC platform (`example-grc-infra-drift`), with separate repositories recommended and a monorepo noted. Still open: GitLab or Azure DevOps profiles if wanted.

**Order:** after the hands-on Sophos path (s09-25), with s09-32.

### s09-34 Ref ID conventions enforced at the prompt and in scope validate (added 2026-11-23)
- [ ] Done

Added at the person's request after the s09-22 onboarding trial, where a contract ref was saved as "Renewal" because prompts only suggested the prefix.

**Story:** As a GRC lead, I want every ref ID to follow one convention (SUP-, SOL-, CTR- and the rest), shown in the terminal so I only type the part after the prefix, so that the repo and CISO Assistant read the same way whoever wrote them.

**Acceptance criteria**
- One table of conventions in the core, used by every prompt and by validation: `SUP-` supplier (third party), `SOL-` solution, `CTR-` contract, `SYS-` system, `AC-` applied control, `PER-` perimeter, `EA-` entity assessment. The part after the prefix is upper case letters, digits and hyphens.
- Every prompt for a ref ID (`hound init`, `hound add system|control`, `hound onboard <system>`) prints the prefix as part of the prompt (`Supplier ref_id: SUP-`) and takes only the rest; the answer is upper-cased, and anything else (spaces, other characters, empty) is asked again with the rule. A `--ref-id` option given in full with the right prefix is accepted as it is; with the prefix left out, the prefix is added.
- `hound scope validate` reports a ref ID without its kind's prefix or with other characters as `scope.ref_id_convention`, naming the file, line, kind and expected prefix, with the suggested fix. Audit ref IDs keep their own form (`<CODE>-<FRAMEWORK>-<YEAR>`, as `hound init` writes) and are checked against it.
- The example compliance repo, tutorials and `docs/scope.md` follow the conventions and document the table once.

**Tests first**
- `RefIdConventionTests.Prefix_and_allowed_characters_per_kind`
- `OnboardSystemCommandTests.Ref_id_prompts_show_the_prefix_and_take_the_rest`
- `AddSystemCommandTests.Ref_id_without_its_prefix_gets_it`
- `ScopeValidatorTests.Ref_id_without_the_kinds_prefix_is_reported`
- `ExampleRepoTests`: the example repo validates under the conventions.

**Functional check**
- `hound onboard SOPHOS-FIREWALL ... --no-model` in the trial repo: the supplier, solution and contract prompts show `SUP-`, `SOL-` and `CTR-`; typing "renewal" for the contract gives `CTR-RENEWAL`; a hand-edited `ref_id: Renewal` in `scope/third-parties/` fails `hound scope validate` with `scope.ref_id_convention`.
- Result (2026-11-23), with the built CLI in a copy of `examples/compliance-repo` (the trial repo is on the maintainer's computer): `hound onboard EXAMPLE-ASSETS --repo <copy> --group ML2 --spec examples/apis/example-asset-api.openapi.yaml` showed `Supplier ref_id: SUP-`, `Solution ref_id (what the supplier provides): SOL-` and `Contract ref_id: CTR-`; "example assets" for the solution was asked again with the rule; "renewal" for the contract gave `CTR-RENEWAL` ("make contract CTR-RENEWAL active"). With the contract hand-edited to `ref_id: Renewal`, `hound scope validate` answered "sup-example.yaml line 15: contract ref_id 'Renewal' does not follow the convention: CTR- then upper case letters, digits and hyphens. [scope.ref_id_convention] Rename it to 'CTR-RENEWAL', here and wherever it is named." **Still to do:** the same run in the trial repo; the task stays unticked until then.
- As built: `RefIds` (core, pure) holds the table, with `Follows`, `Complete`, `Suggest` and `Rule`; `RefIdValidator` checks every declared perimeter, system, control, third party, solution, contract, supplier assessment and audit. `RefIdPrompt` (CLI) prints the prefix and asks again until the answer fits; `hound add system|control`, `hound onboard <system>` and the options of `hound supplier draft` use it, and `hound audit draft` checks the audit's form.
- Deviations: the scope validation test is in the existing `ScopeValidationTests` (there is no `ScopeValidatorTests`). `hound init` asks for no ref ID (it writes `PER-<code>`, `AC-` and the audit from the short code), so it has no prompt to change. An audit's form is checked as two or more parts of upper case letters and digits joined by hyphens, not as code, framework and a year, because audits with periods and the tutorial's audits name more parts. The organisation's own entity is not checked, since it is not a supplier. An answer typed in full with its prefix is also accepted at a prompt, so answer files written before this task still work. Supplier assessments in the tests were `ENA-`; they are `EA-` now, as `hound onboard` writes them.

### s09-35 Guidance in the terminal for hound onboard (added 2026-11-23)
- [ ] Done

Added at the person's request after the s09-22 Sophos onboarding trial, where the prompts gave too little to go on: a bare "type 0 to 4" for each supplier level, no example answers, a session written into the wrong folder, and the model's open questions shown as results rather than asked. The terminal is the place for it for now; a help page can follow.

**Story:** As a GRC lead onboarding a system for the first time, I want each prompt to tell me what it means and show an example, so that I can answer well without leaving the terminal or reading the code.

**Acceptance criteria**
- **Scales explained before each level.** Before a supplier level is confirmed, the terminal prints its scale, one line per value, then the proposal and its reason. The scale text lives in one table in the onboarding project (dependency and penetration 0 to 4, maturity, trust and criticality 1 to 4, and "not yet assessed"), checked first against what CISO Assistant shows for third parties and EBIOS RM's stakeholder criteria; `docs/onboarding.md` prints the same table.
- **An example and a reason for every opening question.** Each prompt (name, owner, product, supplier, solution, contract, what it provides, documents) shows a one-line hint and an example, such as `What does Sophos provide (the service, and what depends on it), e.g. "Perimeter firewall; all office internet traffic passes through it"`.
- **Before the session starts,** hound says what is about to happen (supplier, then applicability, then files written) and stops with a clear message when `--repo` holds no `scope/setup.yaml` (it is not a compliance repo) or when no `--spec` is given (the model cannot propose API checks; continue only on yes).
- **The model's open questions are asked.** When a proposal's reason asks for information ("Confirm whether there is a failover unit"), hound asks it as a question and, if answered, runs the step again with the answer added; empty skips.
- **Plain `--help`.** `hound onboard --help` lists the steps and points to `docs/onboarding.md`.

**Tests first**
- `SupplierScalesTests.Every_level_has_a_line_per_value`
- `OnboardSystemCommandTests.Scale_is_shown_before_each_level`
- `OnboardSystemCommandTests.Every_opening_question_has_an_example`
- `OnboardSystemCommandTests.Repo_without_setup_is_refused_before_writing`
- `OnboardSystemCommandTests.Without_a_spec_it_asks_before_starting`
- `OnboardSystemCommandTests.An_open_question_from_the_model_is_asked_and_the_step_rerun`

**Functional check**
- `hound onboard SOPHOS-FIREWALL ... --spec ../specs/sophos-firewall-v1.yaml` in the trial repo: each prompt shows its hint and example, each level its scale; run from the source checkout, it refuses before writing anything.
- Result (2026-11-23), with the built CLI: in a copy of `examples/compliance-repo` (the trial repo is on the maintainer's computer), `hound onboard EXAMPLE-ASSETS --repo <copy> --group ML2 --spec examples/apis/example-asset-api.openapi.yaml` printed "Onboarding EXAMPLE-ASSETS in three steps" before the first question, a hint with an example above each of the eleven opening questions, and each level's scale above its question ("dependency (0 to 4): how much the system relies on this supplier to keep working." and a line per value); answering 3 and a reason wrote `dependency: 3` with the reason and "decided by Alex Example" in the supplier file, and the levels left empty stayed "to decide". Run from the source checkout, it answered "is not a compliance repo: it has no scope/setup.yaml" and wrote nothing. `hound onboard --help` lists the three steps and names `docs/onboarding.md`. **Still to do:** the same run in the trial repo with the Sophos specification; the task stays unticked until then.
- As built: `SupplierScales` (onboarding project, pure) holds what each value of each level means; `OnboardGuide` (CLI) holds the steps, the note for a missing `--spec` and the hint for each of the eleven opening questions. `hound onboard <system>` now asks each level after printing its scale: a number and a reason, written in the supplier file as decided by the reviewer, or empty to leave it to decide. `docs/onboarding.md` prints the same tables, checked by `OnboardingDocsTests`.
- Deviations, because the built-in model was removed first (s09-40): there is no proposal to show before a level, so the person gives the level and its reason; there are no open questions from a model to ask, so `An_open_question_from_the_model_is_asked_and_the_step_rerun` was not written (with an agent, the agent asks the person, as its skills say). Without `--spec`, hound says what is missing and carries on, and does not stop for a yes: nothing is proposed from the specification any more, and the tutorial's runs have none (`Without_a_spec_it_says_what_is_missing_before_starting`). Two tests were added: the steps are printed before the first question, and `--help` lists them. The scale text follows EBIOS Risk Manager's stakeholder criteria in plain words, with 0 added for dependency and penetration as CISO Assistant allows; it was not compared with a running CISO Assistant's screens here, and criticality's wording is hound's own.

**Order:** with s09-34; both before the next hands-on onboarding.

### s09-36 Met by another system: an open state, never not applicable (added 2026-11-23)
- [x] Done

From the s09-25 Sophos run and the [agent-driven onboarding design](../design/agent-driven-onboarding.md) (decision 4).

**Story:** As a GRC lead, I want a requirement another system provides to stay open until that system's audit shows it met, so that no gap shows as closed.

**Acceptance criteria**
- An applicability decision and an environment capability can be `other_system`, naming the system. It is written as `not_assessed` with an observation naming the system, never `not_applicable`, and needs no not-applicable approval.
- `hound scope plan` lists open dependencies: each such row, the system it waits on, and whether that system has an audit and the requirement's result there.
- `hound onboard environment` writes no row the audit's mapping computes, says which it skipped, and never overwrites collector evidence.
- Existing not-applicable rows that name another system are reported by `hound scope validate` as `scope.other_system_as_not_applicable`, with the fix.

**Tests first**
- `ApplicabilityTests.Other_system_is_written_open_naming_the_system`
- `ScopePlanTests.Open_dependencies_are_listed`
- `OnboardEnvironmentTests.Rows_the_mapping_computes_are_skipped_and_named`
- `ScopeValidatorTests.Not_applicable_naming_another_system_is_reported`

**Functional check**
- The trial repo: the Sophos firewall audit's other-system rows are reported, rewritten as open, and the plan lists them as dependencies.
- Result (2026-11-23), in the trial repo against the local CISO Assistant: `hound scope validate scope --config hound.yaml` exited 3 with `scope.other_system_as_not_applicable` for 109 rows (82 in the organisation's audit from the capability map, 27 in the Sophos firewall audit, the 27 "another system" decisions). The rows were rewritten as `to_do` / `not_assessed` with `provided_by` (a one-off script in the trial workspace); validate then passed, `hound scope plan` planned 109 updates with no regressions and listed 116 rows under "Open dependencies", all "no audit yet" (7 rows wait on two systems); `hound scope apply` wrote 109, and a second plan was empty. In CISO Assistant the firewall audit shows 33 not applicable, 1 compliant and 126 not assessed, and the organisation's audit no longer shows the 82 rows as not applicable.
- As built: assessments take `provided_by: [<system>]` (model, loader, `schemas/scope.schema.json`); both writers (a system's audit and the environment capability map) write such rows open; `OpenDependencies.Of` gives the plan's section and JSON `dependencies`, with the result as the scope declares it, "from collectors" when that audit's mapping computes it, or "no audit yet". Tests: `OnboardOutputTests.Decisions_become_scope_assessments_and_another_systems_row_stays_open`, `CapabilityScopeTests.Requirement_another_system_meets_stays_open_naming_that_system` (both renamed from the names earlier tasks list), `ScopeValidationTests.Not_applicable_naming_another_system_is_reported`, `ScopeValidationTests.Open_row_met_by_another_system_is_valid`, `OpenDependencyTests` (3), `OpenDependencyPlanTests` (3), `ScopeSchemaTests` now also over the onboarding tutorial's files. Skipping rows the mapping computes was already built in s09-28 (`OnboardEnvironmentTests.Requirements_the_mapping_computes_are_left_to_it_so_the_scope_validates_with_the_deployment`). The onboarding tutorial's two audit files were regenerated from its recorded answers.

### s09-37 The agent interface (added 2026-11-23)
- [x] Done

Design decisions 2 and 3.

**Story:** As a person driving `hound` from Claude Code or Copilot, I want commands an agent can run without prompts, so that it can draft a whole onboarding and `hound` checks every step.

**Acceptance criteria**
- `hound onboard <system> pending --json`: the undecided requirements with their text, the API summary and the skills that apply.
- `hound onboard <system> decide <requirement> --kind <api_check|manual|not_applicable|other_system> --reasoning <text> [--source <name>]... [--other-system <name>]`: records one decision as "drafted by agent", validated as the interactive flow is.
- `hound supplier draft` takes the supplier answers and levels as options; `--json` on `scope validate`, `scope plan` and `explain`.
- Approvals happen in VS Code, never in CISO Assistant, and refuse an agent: everything a person must approve is a draft in config until `hound approve` (in a terminal) walks the person through each pending item in turn: not-applicable decisions, environment capabilities, supplier approval, and a supplier review's conclusion (`ok`, `warning` or `blocker`, who and when, written to the third-party file and applied by `hound scope apply`). Without a terminal, `approve` refuses. Approved items record who approved them and when.

**Tests first**
- `OnboardAgentCommandTests.Pending_lists_undecided_requirements_as_json`
- `OnboardAgentCommandTests.Decide_records_a_draft_decision`
- `OnboardAgentCommandTests.Not_applicable_stays_a_draft_until_approved_on_a_terminal`
- `OnboardAgentCommandTests.Approve_without_a_terminal_is_refused`
- `ApproveCommandTests.Walks_through_every_pending_approval_in_turn`
- `SupplierReviewTests.Conclusion_in_config_is_applied_to_ciso_assistant`

**Functional check**
- The example API onboarded with `pending` and `decide` only, then `hound approve` on a terminal; the example supplier's review concluded in config and applied; `hound scope validate` is valid and nothing was done in CISO Assistant's UI.
- Result (2026-11-23), in a copy of `examples/compliance-repo` with the built CLI and `HOUND_TODAY=2026-11-23`: `hound supplier draft ... --json` wrote `scope/third-parties/sup-example.yaml` (`"approved": false`); `hound onboard EXAMPLE-ASSETS pending --json --group 1 --spec examples/apis/example-asset-api.openapi.yaml --owner it@example.com --product "Example Asset API" --solution SOL-EXAMPLE-ASSETS` listed 48 requirements with the API summary and three skills (`applicability`, `essential-eight-ml2-mapping`, `reading-an-api-specification`); 48 `decide` calls drafted one not applicable, one API check and 46 manual. `printf '...' | hound approve` exited 2 with "hound approve needs a person at a terminal" and changed nothing. On a terminal, `hound approve` asked for the supplier, its review `EA-SUP-EXAMPLE-2026-12` (concluded `ok`), the not-applicable decision and the system, printed "Approved 4 of 4." and wrote the audit, `scope/setup.yaml` and the onboarding record; the supplier's file then held `status: active`, `approved_by`, `approved_on` and the conclusion with `by` and `on`. `hound scope validate scope --config hound.yaml` printed "scope valid: 1 domain(s), 3 control(s), 4 audit(s), 1 third party(ies)".
- Against CISO Assistant: `AgentOnboardingFunctionalTests` runs the same steps, then one `hound scope apply` writes the supplier, the review with its conclusion and the system, reads the conclusion back from CISO Assistant as `ok`, and plans nothing more. It runs in CI (the `functional` job); outputs land in `test-results/agent-onboarding/`.
- As built: `hound onboard <system> pending|decide` (`OnboardAgentCommand`) work on the same `session.json` as the interactive command; a decision carries `drafted_by`, `sources` and `approved_on`, and is a draft while no person has accepted it. The interactive `hound onboard` writes nothing from a session with drafts and points to `hound approve`. `hound supplier draft` takes each level as `--level <name>=<value>:<reason>`. `hound approve` (`ApproveCommand`) refuses unless the process's input and output are both a terminal, and walks capabilities, suppliers (draft contract to `active`, with `approved_by` and `approved_on`), open reviews, not-applicable drafts, then each drafted system. A review's conclusion is `conclusions:` under the assessment (`review`, `result`, `by`, `on`; in the loader, validator and `schemas/scope.schema.json`); the plan compares a review's `conclusion` only when the scope declares it, and the writer sends it only then. The supplier gate counts an active contract and a conclusion recorded in the scope before they are applied, and a conclusion in the scope wins over CISO Assistant's for the same review. `--json` on `scope validate` and `scope plan` is `--format json`; `explain --json` gives what code reads (counts, objects, flags, topics) and asks no model.
- Deviations: `decide` takes `--justification` for not applicable (the interactive flow asks for it; the approver is still only a person, in `hound approve`). `pending` also starts the session, from `--owner`, `--product` and `--solution`, since nothing else could without prompts. Who concluded a review and when stay in the supplier's file; only the conclusion is sent to CISO Assistant.
- Found by the first CI run of the functional test (2026-11-23): the plan after the apply still showed 2 updates, because the drafted supplier leaves `penetration`, `trust` and the solution's `criticality` to decide and CISO Assistant fills them with defaults. The planner now compares a supplier's levels and a solution's criticality only when the file declares them (`ScopePlanTests.Supplier_levels_the_scope_leaves_to_decide_are_left_as_ciso_assistant_holds_them`). The two functional tests that read a review by `ref_id` now pick it from the list themselves, since CISO Assistant does not filter entity assessments by `ref_id`.

### s09-38 hound agent setup: skills and agent files for Claude Code and Copilot (added 2026-11-23)
- [ ] Done

Design decision 9. The person chose Claude Code and Copilot custom agents in VS Code (`.github/agents/`).

**Story:** As a person using Claude Code or GitHub Copilot in VS Code, I want `hound` to set up my assistant in the compliance repo, so that it can onboard systems and suppliers, import my audit workbook and review a plan before apply.

**Acceptance criteria**
- `hound agent setup [--assistant claude|copilot|both]` asks when not given, and writes from one source of skills: Claude Code `CLAUDE.md`, `.claude/skills/<skill>/SKILL.md`, `.claude/agents/<agent>.md`; Copilot `.github/agents/<agent>.agent.md` (frontmatter `name`, `description`, `tools`, `skills`) and `.github/copilot-instructions.md`.
- Skills: onboard a system with hound, assess a supplier, read an API specification, import an audit workbook, write a profile, review before apply. Agents: onboarding (`read`, `search`, `edit`, `execute`) and review (`read`, `search`, `execute`, no `edit`).
- Every file starts with the rules: organisation data stays in this private repo; never open `secrets/`; never approve on the person's behalf; nothing reaches CISO Assistant except through `hound scope plan` and `apply`.
- Files `hound` wrote carry a marker and its version; re-running updates them and leaves any other file alone. `hound init` offers setup at the end.

**Tests first**
- `AgentSetupTests.Claude_files_are_written_from_the_skill_source`
- `AgentSetupTests.Copilot_agents_have_frontmatter_and_least_tools`
- `AgentSetupTests.Rerun_updates_hounds_files_and_keeps_the_persons`
- `AgentSetupTests.Every_file_starts_with_the_rules`

**Functional check**
- `hound agent setup --assistant both` in the trial repo; VS Code lists the hound agents in the Copilot agent picker and Claude Code lists the skills.
- Result (2026-11-23), command part only, in an empty folder with the built CLI: `hound agent setup --repo <folder> --assistant both` wrote 12 files (`CLAUDE.md`, six `.claude/skills/<skill>/SKILL.md`, two `.claude/agents/*.md`, `.github/copilot-instructions.md`, two `.github/agents/*.agent.md`); a second run reported each `Unchanged`. **Still to do, by the maintainer in the trial repo:** confirm VS Code lists `hound-onboarding` and `hound-review` in the Copilot agent picker and Claude Code lists the six skills. The task stays unticked until then.
- As built: the source is the repo's `agent/` folder (`rules.md`, `instructions.md`, `skills/`, `agents/`), embedded in hound; an agent skill names the default skills it carries with `includes:` (`AgentKit`, pure). The marker is an HTML comment after any frontmatter, with hound's version. Agents are named `hound-onboarding` and `hound-review`. Claude agents get `tools: Read, Grep, Glob, Edit, Write, Bash` (review: no `Edit`, `Write`). `hound init` asks at its end, or takes `--assistant`; `--yes` sets nothing up.
- Deviations: Copilot alone also gets the skills, under `.github/skills/`, because its agents name them; with both, they are written once under `.claude/skills/` and hound removes its own copies from `.github/skills/`. "Every file starts with the rules" means straight after the frontmatter and the marker, since both assistants need the frontmatter first. An existing `CLAUDE.md` without the marker is kept, not merged. Extra test: `AgentSetupTests.Init_offers_the_setup_at_the_end`.

### s09-39 Onboard the Sophos firewall again, driven by the agent (added 2026-11-23)
- [ ] Done

**Story:** As the maintainer, I want the whole Sophos journey done again by Claude Code with the hound skills (third parties, the system, its audit and its manual evidence), so that the agent path is proven against the s09-25 run before the built-in model goes.

Needs s09-36, s09-37, s09-38, s09-41 and s09-42.

**Acceptance criteria**
- In a fresh copy of the trial repo: the agent drafts Sophos and the managed service provider as third parties (reading the vendor documents, PDFs included, and the compliance card), the firewall and its audit through `pending` and `decide`, iterates a profile until `hound profile test` passes, proposes results for the manual evidence the person drops in, and runs the review agent before proposing apply. The person approves everything with `hound approve` and `hound manual approve` in the VS Code terminal, and clicks nothing in CISO Assistant.
- Compared with s09-25 in `docs/evidence/s09/s09-39.md`: decisions per kind, API checks found, profile rounds, flags the review raised and cleared, time taken. No organisation data.

**Functional check**
- The run itself, recorded as above.

### s09-40 Remove the built-in model from hound (added 2026-11-23)
- [ ] Done

Design decision 9. Only after s09-39.

**Story:** As the maintainer, I want the language model removed from `hound`, so that there are no API keys, provider SDKs or token budgets to manage, and the agent is the one way to get model help.

**Acceptance criteria**
- The Anthropic and OpenAI SDKs, the model port and adapters, `hound onboard config`, `onboarding.yaml`, the outbound guard, deny terms, token budget and request record are removed, with their tests and docs.
- `hound onboard`, `onboard environment`, `onboard profile`, `explain` and `skill draft` keep their deterministic parts; options that only served the model are removed, with a message pointing to `hound agent setup`.
- `docs/onboarding.md` describes the agent path; `docs/design/onboarding-assistant.md` is marked superseded by the agent-driven design.

**Tests first**
- `HoundCliTests.Onboard_config_points_to_agent_setup`
- Existing onboarding tests pass with the model removed.

**Functional check**
- `make test`, `make lint` and the example tutorial pass; `dotnet list package` shows neither SDK.
- Result (2026-11-23): `dotnet test` on the unit and contract projects passed (1265 tests; the 27 functional tests need Docker and run in the functional job or the dev container); `dotnet format --verify-no-changes --no-restore` printed nothing; `dotnet list package --include-transitive` names neither SDK, and the built CLI's folder holds neither assembly; the example tutorial's answers run through `TutorialTests` in the unit suite and pass. With the built CLI: `hound onboard config --provider anthropic` answered "hound no longer has a built-in model. For model help, set up your coding assistant with 'hound agent setup' and ask it: it drafts through hound, and you approve." and exited 2; `hound onboard --help` and `hound explain --help` list no model options. **Still to do:** ShellCheck (`make lint`'s other half) and the functional tests run in CI; the task stays unticked until those pass and the maintainer has tried it in the trial repo.
- Result (2026-11-23), CI started by hand on the branch at commit 072ce07: every job passed, the functional tests and ShellCheck among them. Still unticked until the maintainer has tried it in the trial repo.
- As built: `Models/`, `Guard/`, `Configuration/`, the capability and profile drafting, `onboarding/models.yaml` and the recorded model fixtures are gone from `EvidenceHound.Onboarding`, with their tests. `Retired` (CLI) keeps each removed option as a hidden option that does nothing and prints where model help now comes from. `hound onboard` asks its questions and proposes nothing: applicability starts empty and supplier levels are left "to decide". `hound onboard profile` checks a profile that already exists (validate, sample, confirm, write its test) and no longer drafts one. `hound explain` prints the facts of a plan without a summary. `hound skill draft` writes the organisation skill templates and keeps any skill file already there.
- Deviations: started before s09-39 at the maintainer's request (2026-11-23), who will test s09-39 to s09-42 together. Options that only served the model (`--no-model`, `--show-requests`, `--config`, `--rounds`, `--deny-terms`, `--framework` and `--skill` on explain, `--repo` on skill draft) are hidden and accepted with a note, not rejected, so scripts and the tutorial's commands keep working; `hound onboard config` itself exits 2 with the message. `hound skill validate` no longer warns about sensitive content, since nothing leaves the machine. The runner still refuses an `llm` block in `hound.yaml`; its hint now points to `hound agent setup`.


### s09-41 Agent-driven onboarding of the organisation, environment, third parties and audits (added 2026-11-23)
- [ ] Done

Design decision 5. Uses the agent interface (s09-37) and ships with `hound agent setup` (s09-38).

**Story:** As a small business owner with Claude Code or Copilot, I want my agent to onboard everything (my organisation, the environment, suppliers, resellers and managed service providers, systems and audits, including my existing audit workbook), so that I review and approve in VS Code rather than build it by hand.

**Acceptance criteria**
- Agent commands, non-interactive with `--json`, for each: `hound init` with every answer as an option; `hound onboard environment` capabilities through `pending` and `decide`; `hound supplier draft` (vendor, reseller or managed service provider, solutions, contract, levels with reasons, kept documents); `hound audit draft` (framework, groups, domain, perimeter, systems); `hound scope import <workbook>` for an existing audit (requirement, result, observation, evidence reference, approver and date), keeping each row's provenance and leaving rows without an approver as drafts.
- A skill for each, in the one skill source: onboard the organisation, onboard the environment, onboard a third party, onboard an audit, import an audit workbook. Each tells the agent to read the person's documents itself (PDFs included), to ask the person what only they know, and never to approve.
- Everything drafted is approved through `hound approve` (s09-37).

**Tests first**
- `InitCommandTests.Every_answer_can_be_given_as_an_option`
- `SupplierDraftCommandTests.Reseller_and_msp_are_drafted_as_their_own_third_parties`
- `AuditDraftCommandTests.Audit_is_drafted_for_the_named_systems`
- `ScopeImportTests.Workbook_rows_keep_their_approver_and_row`
- `ScopeImportTests.Row_without_an_approver_is_a_draft`
- `AgentSkillTests.Every_onboarding_kind_has_a_skill`

**Functional check**
- A fictional audit workbook (in `examples/`) imported into a copy of the example repo by Claude Code with the skills; `hound approve` on the drafts; `hound scope validate` is valid and the plan shows each imported row with its provenance.
- Result (2026-11-23), with the built CLI in a copy of `examples/compliance-repo`: `hound scope import examples/workbooks/example-audit-workbook.csv --repo <copy> --audit EX-E8-2026` answered "2 row(s) imported with the workbook's approver, 1 drafted for 'hound approve' (1 waiting in all), 0 already in the audit, 1 not assessed and left out" and listed row 6 (`EX-0001`) as not a requirement of the framework; `hound scope validate` answered "scope valid: 1 domain(s), 3 control(s), 3 audit(s)"; the audit holds ISM-1807 and ISM-1808 ending "Imported from example-audit-workbook.csv, row 2, approved by Sam Example on 2026-05-01."; `hound approve` run from the agent's shell refused ("needs a person at a terminal"). **Still to do:** the approve step and the plan against CISO Assistant are in `WorkbookImportFunctionalTests` (functional job, started by hand or in the dev container); the task stays unticked until that passes, and until the import is driven once by Claude Code with the skills.
- Result (2026-11-23), functional job started by hand on the branch at commit 4e64bf0: all functional tests passed, `WorkbookImportFunctionalTests` among them. Still unticked until the import is driven once by Claude Code with the skills.
- As built: `hound onboard environment pending|decide` (`OnboardEnvironmentAgent`) write `scope/capabilities.yaml` with no approver; `hound supplier draft` takes `--kind vendor|reseller|msp`, `--for <vendor>` and `--document <path>` (recorded with its SHA-256); `hound audit draft` (`AuditCommands`) writes `onboarding/drafts/audits/<ref>.yaml`; `hound scope import` (`ScopeImportCommand`, with `Workbook` and `WorkbookImport` in `EvidenceHound.Onboarding.Workbooks`, pure) reads `.csv` and `.xlsx` without a spreadsheet library; `hound init` takes `--json`. `hound approve` now also walks drafted audits and imported rows. Skills added to `agent/skills/`: onboard-the-organisation, onboard-the-environment, onboard-a-third-party, onboard-an-audit; import-an-audit-workbook now drives `hound scope import`.
- Deviations: an imported row's provenance (workbook, row, approver, date) is carried in the row's observation, since an audit assessment has no other field CISO Assistant keeps; the workbook's evidence reference is quoted there too, and no evidence file is imported. Rows marked not assessed are left out. The reseller or provider relation is the third party's description plus the system's existing `supplied_through`. Extra tests: `OnboardEnvironmentTests.Agent_decides_capabilities_through_pending_and_decide`, `ScopeImportTests.Columns_can_be_named_and_an_excel_workbook_is_read`.

### s09-42 Manual evidence with the agent: propose, then attest in the terminal (added 2026-11-23)
- [ ] Done

Design decision 10, and the reworded rule: no model in collection, and no evidence without a person.

**Story:** As a GRC lead, I want my agent to read each manual evidence file as it arrives and propose the result, so that I attest a queue of ready proposals in the VS Code terminal instead of assessing each file from scratch.

**Acceptance criteria**
- `hound manual propose <source> <file> --result <pass|fail|partial> --observation <text> --supports <extract or location>... [--json]` records a pending proposal next to the kept file (hashed); it is not evidence and no run publishes it.
- `hound manual approve [--source <source>]` walks through every pending proposal on a terminal: the file, the agent's result, observation and supporting extracts; the person attests (`a`), changes the result or observation, or rejects with a reason. Without a terminal it refuses.
- `hound run` publishes attested results only, recorded as "proposed by agent, attested by <person> on <date>" with the file attached and its hash; a result the person entered without a proposal reads as today.
- A skill, process manual evidence, tells the agent to propose only from what the file shows, quote what supports it, say what is missing, and never attest.
- `docs/engineering.md` and the onboarding docs state the reworded rule.

**Tests first**
- `ManualProposeTests.Proposal_is_pending_and_not_published`
- `ManualApproveTests.Walks_through_pending_proposals_and_records_the_attester`
- `ManualApproveTests.Refuses_without_a_terminal`
- `ManualCollectorTests.Only_attested_results_are_published`

**Functional check**
- In the trial repo: a restore-test record dropped in, a proposal recorded by the agent, attested with `hound manual approve`, and `hound run --only example-backup-restore-test` publishing it with the attestation line.
- Result (2026-11-23), with the built CLI in a copy of `examples/`: `hound manual propose example-backup-restore-test restore-test-2026-11.txt --config examples/example-essential-eight.yaml --result pass --observation "..." --supports "Restored: 3 files to a scratch folder, hashes match" --supports "Result: success, 14 minutes" --json` answered `"pending": true` with the file's SHA-256 and left `restore-test-2026-11.txt` and `restore-test-2026-11.txt.proposal.yaml` in the drop folder; `hound manual approve` run from the agent's shell refused ("needs a person at a terminal", exit 2); `hound run --only example-backup-restore-test --no-publish` stored the file alone as evidence, not the proposal. **Still to do, by a person:** attest with `hound manual approve` in a terminal and publish with `hound run` in the trial repo (part of s09-39). The task stays unticked until then; the walk itself is covered by `ManualApproveTests` with a terminal stood in.
- As built: `Proposal` (`<file>.proposal.yaml`: `proposed_by`, `proposed`, `file_sha256`, `outcomes`, `observation`, `supports`, and `rejected_by`, `rejected`, `rejection_reason` once rejected) beside `Attestation`, which gains `proposed_by` and `file_sha256`; `ManualSource.Accepts` leaves proposals out, so a run neither stores nor publishes one; `ManualAssessment` writes "Proposed by agent, attested <outcome> by <person> on <date>: <file> (sha256 <12>)." and reports `partial` when the file's hash no longer matches its attestation. `hound manual propose` and `hound manual approve` are in `ManualProposalCommands`. Skill `process-manual-evidence` in `agent/skills/`, listed on the onboarding agent; the shared rules now name `hound manual approve` and `.attest.yaml`.
- Deviations: `--check <check id>=<result>` gives one check a different result from `--result` (a source can have several checks). `hound manual approve` takes `--config`, as `hound manual upload` does, and has a stop answer (`s`). A proposal is refused when the file is already attested, and is skipped in the walk when the file changed after it was proposed. Extra tests: `AgentSkillTests.Manual_evidence_skill_proposes_from_the_file_and_never_attests`.

## Discovered work

- s09-42: an agent finds files waiting for a proposal by listing the drop folder. Add `hound manual pending --json` (files with neither an attestation nor a proposal, per source, with what each check asks for).
- s09-42: `hound manual upload` writes an attestation without the file's hash, so a file changed after a person attested it unaided is not noticed. Record `file_sha256` there too.
- s09-42: a rejected proposal stays beside the file as `<file>.proposal.yaml`; nothing reports rejected proposals or clears them when the file is replaced.

- s09-41: after the environment's decisions are approved, the rows other systems' audits leave open are written only by running `hound onboard environment --no-model` again. Have `hound approve` write them when it approves the last capability.
- s09-41: `hound scope import` reports a row that already waits as a draft under `already_assessed`. Give it its own list.
- s09-41: an imported row whose requirement a mapping computes is caught by `hound scope validate` (`scope.result_conflict`), not by the import. Have the import leave such rows out and list them.
- s09-41: an Excel date cell is read as its stored day number and converted; a date typed as text in another format (such as 1/5/2026) is copied as written. Normalise or refuse it.

- s09-38: the agent skills carry hound's default skills only. The organisation's own skills (`skills/` in the config repo, with `overrides:`) reach an agent through `hound onboard <system> pending --json`, but not through the skill files. Consider having `hound agent setup` include them (fits s09-41).
- s09-38: `examples/compliance-repo/CLAUDE.md` is hand-written and older than the agent files. Regenerate the example repo's assistant files with `hound agent setup` once the VS Code check passes, and keep one of the two.

- **Before the sprint PR is marked ready:** turn the `functional` CI job back on for pull requests (`.github/workflows/ci.yml`). It was switched to run only on `main` and by hand on 2026-11-23 to keep pushes fast; until then functional tests run in the dev container with `make test-functional`, and each task's functional check records that run.

- s09-37: the interactive `hound onboard <system>` still takes a not-applicable approver's name from piped input, so an agent could answer its prompts. Make its not-applicable approval go through `hound approve` too, or refuse it without a terminal (fits s09-41).

- s09-37: `hound approve` asks about every open supplier review each time it runs, including reviews not yet due and ones already concluded in CISO Assistant before conclusions moved to config. Consider asking only for reviews that are due or that the person names, and importing existing conclusions with `hound scope export`.

- s09-37: the onboarding tutorial (`docs/examples/onboarding-tutorial.md`) and the files `hound onboard` drafts for a person still describe concluding the supplier review in CISO Assistant. That route still works; move the tutorial to `hound approve` with s09-39.

- s09-37: `hound onboard environment` has no `pending` and `decide` yet (planned in s09-41); `hound approve` already approves capabilities that have no `approved_by`.

- s09-28: the capability map's signals do not yet include devices onboarded to Defender for Endpoint (`security/machines` needs a Defender API permission, not Graph). Add when s09-02 reads Defender.

- Open items from s08's discovered work (trial token refresh, the seed's compliance owner, HTTP logging at `info`, wizard prompts, trial re-runs, evidence on applied controls, the "unlisted rule" wording, `HOUND_CA_URL` in the dev container) are listed in [s08](s08-sophos-veeam-collectors.md#discovered-work); pick them up here or in s12.

- The config check's long-run rule (32+ letters, digits, `/`, `_`) also matches a deep folder path that contains digits, for example a `profiles_path` under a generated temp folder. Short mounted paths such as `/config/profiles` are fine. Consider excluding `/` from the run for `_path` settings (found in s09-10).

- Scope export (`hound scope export`) does not yet cover third parties or the organisation's own entity; a tenant that already has suppliers in CISO Assistant has to write their files by hand (found in s09-19).

- The Anthropic adapter asks for replies of up to 16,000 tokens without streaming. Steps that need longer replies (a whole drafted profile) should stream through the SDK's streaming call (found in s09-21).

- The official Anthropic .NET SDK sends the API key as `Authorization: Bearer`, and reads `ANTHROPIC_BASE_URL` from the environment when no base URL is set. `hound onboard config --check` confirms the key works end to end; note both in the onboarding guide if a provider proxy is used (found in s09-21).

- Vendor documents kept during `hound onboard` are saved with their SHA-256, source and retrieval date, but are attached to the supplier assessment as evidence only once supplier assessments exist (s09-20). The model reads only text documents (HTML, text, Markdown); PDFs are kept but not read (found in s09-22).

- An Essential Eight audit that declares `groups: ["2"]` is stored by CISO Assistant as groups 1 and 2, so `hound scope plan` shows it as changed after every apply. `hound scope validate` should warn, or the planner compare the groups CISO Assistant keeps; until then declare ML2 as `["1", "2"]` (found in s09-24's functional test).

- `hound onboard` now declares the system in `scope/setup.yaml` with its `solution`, and a drafted supplier file includes a yearly review (fixed after s09-20, at the person's request). Still open: `supplied_through` for a reseller is not asked, and kept vendor documents are not yet attached to the supplier assessment as evidence (found in s09-20).

- `hound scope export` does not write supplier assessments or forced tiers (found in s09-20).

- `hound onboard` reads OpenAPI 3 files only. Other API specifications, documentation saved as text, and specifications fetched from allowed URLs need further readers; Sophos Firewall's XML API (s09-15) is the first that will need one (found in s09-22).

- `hound run --dry-run` reports only counts of passed, failed and other checks; it does not print each check's outcome and observation. An operator trying a new profile needs them; `hound profile test` (s09-12) or a `--show-checks` option would help (found in s09-11).

- Fixed 2026-11-23 (s09-21 defect, commits 842f9d0 and 7714c09): `hound onboard config --check` failed against the real Anthropic API with HTTP 400, because a request with no inputs sent an empty user message (`messages.0: user messages must have non-empty content`); the recorded-reply tests accepted it. A request with no inputs now sends a fixed line, covered by a contract test for both adapters. Functional check: `make run ARGS="onboard config --check"` with a real key: `anthropic claude-opus-5-5: the key works.`

- A 4xx other than 401 or 403 from the model provider still says "Check the key with 'hound onboard config --check'", which sent the person looking at the key when the request itself was wrong. Name the provider's error type (such as `invalid_request_error`, which never echoes the request) and suggest the key only for 401 and 403 (found in s09-21).

- Supplier documents in PDF (such as a vendor's ISO/IEC 27001 certificate) are kept with their hash but not given to the model: hound reads only HTML, text and Markdown, so the supplier step cannot use the strongest assurance a vendor publishes. Since 2026-11-23 the person is told; next, extract PDF text (a maintained PDF library, checked as any new dependency) or send the PDF to the provider as a document block, within the same outbound guard and token budget (found in s09-22, Sophos trial).

- `hound onboard` summarises an OpenAPI spec without its response fields when they are behind `$ref` components (the Sophos Firewall API v1.5.0 spec: `GET /firewalls: List of firewalls` and nothing more), so the model proposed ISM-1501 as manual, saying no endpoint shows a firmware version, although `firmwareVersion` is in the response and the sophos collector checks it. Resolve `$ref` response schemas and list each read endpoint's top-level fields in the summary (found in s09-22, Sophos trial).

- `hound onboard environment` writes not-applicable rows into an audit for requirements the audit's own mapping computes (`scope.result_conflict` on validate), records "provided by another system" as not applicable even when that system has no audit (so the gap shows as closed), and overwrote collector evidence (ISM-1672). Record such rows as open, covered by the named system, until its audit exists; skip rows the mapping computes and say which (found in s09-28, Sophos trial).

- `hound explain` reads a printed plan but not the saved plan file `scope plan --out` writes, and with no file and no piped input it waits silently on the terminal. Read saved plans, and refuse with a hint when standard input is a terminal (found in s09-30, Sophos trial).

- `hound onboard profile` drafted a Sophos firewall profile that did not validate after 3 rounds: it used its own template syntax (`{{ parameters.x }}` rather than `${x}`) and put `field` and `missing` on the check rather than in `each`; the validation problems fed back did not get it there. Give the drafting step the profile format reference (or the closest shipped profile as an example) and the exact problem lines (found in s09-25).

- A requirement a collector marks compliant keeps status `to_do` in CISO Assistant (ISM-1501 on the Sophos firewall audit). Set the status to done with the result, as hand-written rows are (found in s09-25).

- `hound onboard` does not keep `--group` between runs: the second run (after the supplier stop) without the same `--group` options decides every requirement of the framework (152 for the Essential Eight, not 107). Save the groups with the session at the start, or refuse a run whose groups differ (found in s09-32).

- `hound onboard` writes an apostrophe in an observation as `\u0027` (JSON escaping inside a YAML string), and names the audit by the system ID and framework slug ("GRC-PLATFORM essential-eight-2023"). Write plain YAML strings and a readable name (found in s09-32).

- `hound scope validate` reports older not-applicable rows that name another system, but nothing rewrites them: in the trial 109 rows were fixed with a one-off script. A `hound scope fix` (or a `--fix` on validate) that rewrites them as open rows with `provided_by` would save hand edits (found in s09-36).

- An open dependency's "result there" comes from the scope files, not from CISO Assistant, so a requirement a collector has marked compliant in the providing system's audit reads "from collectors", not "compliant". Read the held result when the plan has it (found in s09-36).

- The plan's "Open dependencies" lists one line per row, which was 116 lines in the trial. Group by providing system, with counts, and list rows only with `--detailed` (found in s09-36).

## Sprint review
