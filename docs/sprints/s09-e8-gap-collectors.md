# s09 Essential Eight gap collectors

Status: Planned

**Goal:** close the largest Essential Eight ML2 gaps left after the Microsoft 365, Sophos and Veeam collectors: application control evidenced from Intune and Microsoft Defender for Endpoint, patching evidenced from imported vulnerability scans, and a ready-made manual-upload template for every requirement that stays manual. After this sprint every ML1 and ML2 row in the example Essential Eight audit gets a result from a check or an attested manual source.

New sprint, added in the release 1 reorder. Application control extends the Microsoft 365 collector (same app registration). Vulnerability scans are imported by a new `vuln-scan` collector in `src/Collectors/EvidenceHound.Collectors.VulnScan`, which reuses the manual collector's drop folder and attestation handling. Recorded, fictional responses and exports only (Example Pty Ltd, `example.com`).

The patch windows and scan intervals below are settings, not constants, so the checks stay framework-neutral. The example config sets them to the Essential Eight ML2 values, and the mapping file quotes the ISM control each one comes from.

### REST collector (s09-09 to s09-14)

These tasks come first in this sprint, before s09-01, so the Microsoft 365 application control checks and the Veeam tasks can be written as profile checks instead of new code. They build the generic `rest` collector agreed in [`docs/design/rest-collector.md`](../design/rest-collector.md): every query is called once per run, evidence datasets are extracted from one or more responses, and checks test dataset fields, giving pass, fail or unable to assess. Profiles are YAML loaded only from the organisation's profiles folder; this repo keeps a tested public library in `profiles/` to copy from. There are no runtime code plugins. Where the Veeam tasks (s09-07, s09-08) and s09-01/02 say "collector", read "profile in the library" once these tasks are done.

### s09-09 Profile model and validation
- [ ] Done

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

### s09-10 Query engine
- [ ] Done

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

### s09-11 Datasets and declarative checks
- [ ] Done

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

### s09-12 Code checks, profiles folder and profile tests
- [ ] Done

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

### s09-13 Sophos as a profile
- [ ] Done

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

### s09-14 REST collector documentation
- [ ] Done

**Acceptance criteria**
- `docs/collectors/rest.md`: profile reference (connection, auth, paging, queries, datasets, checks, operators, results, limits), the profiles folder, the library and how to copy from it, and security notes (`allowed_hosts`, secrets, identities).
- `docs/tutorials/write-a-profile.md`: write a profile for the fictional example API test-first, from recording to green `hound profile test`.
- `docs/design/rest-collector.md` status set to Implemented, with any changes made during the build.

**Tests first**
- Docs check that every operator and result in the engine appears in `docs/collectors/rest.md`.
- Docs lint and link check.

**Functional check**
- Following the tutorial verbatim produces a passing profile.

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

## Discovered work

## Sprint review
