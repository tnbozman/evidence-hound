# s09 Essential Eight gap collectors

Status: Planned

**Goal:** close the largest Essential Eight ML2 gaps left after the Microsoft 365, Sophos and Veeam collectors: application control evidenced from Intune and Microsoft Defender for Endpoint, patching evidenced from imported vulnerability scans, and a ready-made manual-upload template for every requirement that stays manual. After this sprint every ML1 and ML2 row in the example Essential Eight audit gets a result from a check or an attested manual source.

New sprint, added in the release 1 reorder. Application control extends the Microsoft 365 collector (same app registration). Vulnerability scans are imported by a new `vuln-scan` collector in `src/Collectors/EvidenceHound.Collectors.VulnScan`, which reuses the manual collector's drop folder and attestation handling. Recorded, fictional responses and exports only (Example Pty Ltd, `example.com`).

The patch windows and scan intervals below are settings, not constants, so the checks stay framework-neutral. The example config sets them to the Essential Eight ML2 values, and the mapping file quotes the ISM control each one comes from.

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

## Discovered work

- Open items from s08's discovered work (trial token refresh, the seed's compliance owner, HTTP logging at `info`, wizard prompts, trial re-runs, evidence on applied controls, the "unlisted rule" wording, `HOUND_CA_URL` in the dev container) are listed in [s08](s08-sophos-veeam-collectors.md#discovered-work); pick them up here or in s12.

## Sprint review
