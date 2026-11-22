# s08 Sophos Central and Veeam reference collectors

Status: In progress

Was s07 before the release 1 reorder. It moved after the thin deployment so that Microsoft 365 evidence runs on a schedule first.

**Goal:** two more reference collectors, one SaaS (Sophos Central) and one on-premises (Veeam Backup & Replication), proving the SDK fits both shapes.

Also in this sprint, ready for the first real configuration: scope plans show their compliance impact (s08-06), and plans get Terraform-style exit codes and saved plan files (s08-07). The sprint ends with a hands-on trial on a real Sophos Central tenant (s08-08), done together with a person, to prove Evidence Hound works end to end outside recordings.

Projects: `src/Collectors/EvidenceHound.Collectors.Sophos`, `src/Collectors/EvidenceHound.Collectors.Veeam`. Recorded, fictional responses only.

### s08-01 Sophos Central access
- [x] Done

**Story:** As the collector, I want Sophos Central authentication and tenant discovery, so that endpoint and firewall data can be read.

**Acceptance criteria**
- OAuth client credentials against Sophos ID; `whoami` determines tenant, partner or organisation; tenant ID setting required for partner and organisation credentials.
- Region-specific data API base URL from `whoami`.
- Paged endpoint and firewall listing.

**Tests first**
- `SophosAuthTests.Tenant_credential_uses_whoami_data_region`
- `SophosAuthTests.Partner_credential_without_tenant_id_fails_clearly`
- `SophosPagingTests.Reads_all_pages`

**Functional check**
- Run against recordings: endpoints and firewalls listed into evidence.
- Result (2026-11-22): `dotnet test --project tests/EvidenceHound.Collectors.Sophos.Tests` passed 21 tests against the recorded fictional tenant: the data region came from whoami, 5 endpoints were read over 2 pages and 1 firewall on 1 page. The evidence files themselves come with the collector in s08-02. Evidence: [`docs/evidence/s08/s08-01.md`](../evidence/s08/s08-01.md).

### s08-02 Sophos checks
- [ ] Done

**Story:** As a compliance owner, I want endpoint protection and firewall health measured, so that those controls have live evidence.

**Acceptance criteria**
- `endpoint.protection.healthy_ratio`: share of endpoints with good health.
- `endpoint.protection.tamper_protection_enabled`: all endpoints.
- `network.firewall.firmware_supported`: firewalls on a supported firmware version (list in config).
- `endpoint.protection.stale_agents`: no endpoint not seen for more than N days.

**Tests first**
- Outcome tests per check from fixtures; contract kit.

**Functional check**
- `hound run --only sophos-example` against recordings updates the example audit.

### s08-03 Veeam access
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

### s08-04 Veeam checks
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

### s08-05 Documentation
- [ ] Done

**Acceptance criteria**
- `docs/collectors/sophos.md` and `docs/collectors/veeam.md` as for M365, with the doc check-ID test.
- Every Sophos and Veeam check is mapped in `mappings/essential-eight-2023.yaml` (started in s06-02), and requirements they now cover are removed from `manual_only`.

**Tests first**
- Docs tests for both collectors.
- `E8MappingTests.Every_reference_check_is_mapped` extended to the Sophos and Veeam checks.

**Functional check**
- Docs build without warnings.

### s08-06 Compliance impact in scope plans
- [ ] Done

**Story:** As the reviewer of a compliance pull request, I want `hound scope plan` to say which framework requirements change result and why, not only which objects and fields change, so that I approve the compliance effect of a change, the way a Terraform user reads "Changes to Outputs" below the resource diff.

**Acceptance criteria**
- The plan has a **Changes to compliance** section after the change table and before Drift, for each audit whose open period has rows that change result. It is built from the framework snapshot (each requirement's title, its parent as the strategy or domain, its implementation groups) and the rows of the open period, so `hound` reads every row of each open audit, not only rows the scope declares (GET only).
- Per audit, one line per selected implementation group (for the Essential Eight, each maturity level): requirements compliant before and after, out of the in-group total, such as `ML2: 41 → 42 compliant of 96`.
- Then each row whose result changes: requirement ref and title, strategy, old → new result, and why: a hand-written assessment in the audit file, the `unlisted` rule, or a control or document linked to the row.
- A result that goes backwards (from compliant or partially compliant to anything lower, or to not assessed) is listed first and marked `⚠`, and the summary line says how many there are. This makes visible, for example, `scope apply` resetting a result that `hound publish` wrote (s06 discovered work).
- JSON plans add `compliance[]`, each `{audit, groups[{group, before, after, total}], rows[{requirement, title, parent, old, new, why, regresses}]}`. `scope apply` puts the same `compliance` in its report, so the record of each apply carries the effect it had.
- Rows that collector runs own are not changed by the scope plan and are left out of this section; their impact stays with `hound plan`.
- A plan with no result changes prints no section, so an unchanged scope still plans to nothing.

**Tests first**
- `ComplianceImpactTests.Counts_compliant_per_group_before_and_after`
- `ComplianceImpactTests.Changed_row_names_title_strategy_and_reason`
- `ComplianceImpactTests.Regression_is_flagged_and_listed_first`
- `ComplianceImpactTests.Run_owned_rows_are_left_out`
- `ScopePlanCommandTests.Markdown_has_changes_to_compliance_section` and `..._json_has_compliance` (recorded responses)

**Functional check**
- Against the local CISO Assistant with the example compliance repo: change one hand-written assessment to `compliant` and set one `unlisted` row back, run `hound scope plan examples/compliance-repo/scope --config examples/compliance-repo/hound.yaml`, and record the section showing one gain and one `⚠` regression with the right ML counts.
- `docs/scope.md` Plan section shows an example of the new section.

### s08-07 Terraform-style exit codes and saved plans
- [ ] Done

**Story:** As an operator used to Terraform, I want `hound scope plan` to report "changes pending" in its exit code and to save a plan that `hound scope apply` then applies exactly, so that pipelines can gate on a plan and what is applied is what was reviewed.

**Acceptance criteria**
- `hound scope plan --detailed-exitcode` exits 0 when the plan is empty, 2 when it has changes or drift, and 1 on any error, as Terraform does. Without the flag the exit codes stay as they are today. With the flag a usage error also exits 1, so that 2 always means changes.
- `hound scope plan --out <file>` also writes the plan to a file: the changes, the drift, the compliance impact (s08-06), the CISO Assistant URL, the date the plan was made for (`HOUND_TODAY` or today), a SHA-256 of the scope files and deployment config, and, for each object the plan changes or deletes, a fingerprint of the state it read. The file never holds the token or any document's content (only hashes), and the docs say not to commit it.
- `hound scope apply <file>` applies only the changes in that file, in order, without planning again. It refuses, writes nothing and exits 3 with `scope.plan_stale` when the scope files or config hash differently, the URL differs, or any object it would change no longer has the state it read. This matches Terraform's "Saved plan is stale". `hound scope apply <dir>` is unchanged.
- The example compliance repo's workflow plans with `--out` in the apply job and applies that file once the protected environment is approved.

**Tests first**
- `ScopePlanCommandTests.Detailed_exitcode_is_0_when_empty_and_2_with_changes`
- `ScopePlanCommandTests.Saved_plan_holds_no_token`
- `SavedPlanTests.Round_trips_changes_drift_and_fingerprints`
- `ScopeApplyCommandTests.Applies_saved_plan_without_replanning` (GET calls only for the stale check)
- `ScopeApplyCommandTests.Stale_plan_is_refused_when_scope_or_instance_changed`

**Functional check**
- Against the local CISO Assistant: `hound scope plan examples/compliance-repo/scope --config examples/compliance-repo/hound.yaml --out plan.json --detailed-exitcode` exits 2, then `hound scope apply plan.json` writes the plan. Repeat it after editing one object in the UI between plan and apply, and record the `scope.plan_stale` refusal.

### s08-08 Hands-on trial: real Sophos Central tenant
- [ ] Done

Do this task last in the sprint: it needs s08-01, s08-02, s08-05, s08-06 and s08-07 merged into the sprint branch, and the thin deployment from s07.

**Story:** As the maintainer, I want to run Evidence Hound for real against a home Sophos Central tenant with an XGS firewall, from configuration to results in CISO Assistant, so that I know the product works end to end and not only against recordings.

**Who does what.** Steps marked **(person)** need the person's hands: their tenant, their credentials and their host. Claude prepares commands, reads output the person pastes back, fixes defects on the sprint branch, and writes the evidence. Credentials, tenant IDs, host names, IP addresses, serial numbers and screenshots with any of those never go in this repo, in chat or in commit text.

**Steps**
1. **(person)** In Sophos Central, create a read-only API credential for the collector (service principal with the least role that reads endpoints and firewalls), following `docs/collectors/sophos.md`. Note any place the guide was wrong or unclear.
2. **(person)** On the host from the s07 thin deployment, store the client ID and secret as Docker secrets files, never in git.
3. Claude drafts, and **(person)** adds to their private compliance repo: a `sophos` collector in `hound.yaml` with `file:` secret references, the supported firmware list and the stale-agent threshold, and its mapping to the Essential Eight audit.
4. **(person)** Run `hound config validate --check-secrets` and `hound run --only <collector> --dry-run` on the host, and paste the output (with identifiers removed).
5. **(person)** Run `hound scope validate`, then `hound scope plan --out plan.json --detailed-exitcode`. Together, read the plan's change table and its **Changes to compliance** section (s08-06), and check that it shows no unexpected `⚠` regression.
6. **(person)** Run `hound scope apply plan.json`, then a real `hound run --only <collector>` (or let the s07 schedule run it), then `hound status`.
7. **(person)** In CISO Assistant, check that the Sophos evidence is attached, the mapped requirements show the results the checks gave, and a second `hound scope plan` plans to nothing (so `scope apply` does not reset what the run published).
8. Claude records any defect as a failing test and a fix on the sprint branch, or as discovered work if it is out of scope, and the person re-runs the failing step.

**Acceptance criteria**
- One real collection run against the tenant succeeds: endpoints and the firewall are read, and all four Sophos checks give an outcome (`Pass`, `Fail` or `NotApplicable`, not an error).
- The results appear on the mapped Essential Eight requirements in CISO Assistant, with the evidence attached.
- The saved plan applied without `scope.plan_stale`, and its compliance section matched what CISO Assistant showed afterwards.
- A second scope plan after the run is empty.
- Every defect found has a regression test from a new recording made with fictional data, or a discovered-work entry.
- `docs/collectors/sophos.md` is corrected wherever the person found it wrong or unclear.

**Tests first**
- None up front: this task is a manual trial. Each defect it finds starts with a failing test before its fix.

**Functional check**
- The trial itself. Record in `docs/evidence/s08/s08-08.md` only: the date, the CISO Assistant and `hound` versions, the shape of the setup ("a home Sophos Central tenant with an XGS firewall", the number of endpoints and firewalls), each step's outcome, each check's outcome, the compliance counts before and after, and the defects found with links to their fixes. No names, tenant IDs, host names, IP addresses, serial numbers, credentials or screenshots.

## Discovered work

## Sprint review
