# s19 Security document generators

Status: Planned

Release: 3

Added at the maintainer's request (2026-11-24) as the final sprint, after release 2.

**Goal:** `hound` drafts the security documents a CMMC Level 2 or Essential Eight Maturity Level 2 organisation must keep, from the data it already manages, the same way it drafts the system security plan (s11-04) and the incident response plan (s15-09). Only documents that Evidence Hound's data can largely fill are in scope. A person writes the judgement parts, and approves each version in CISO Assistant. Then the approved document is published as evidence for the controls that require it, because several of these documents are themselves the evidence for policy and documentation controls.

Source: ASD's ISM, Guidelines for Cyber Security Documentation (https://www.cyber.gov.au/ism/2026-09/guidelines-for-cyber-security-documentation), and for CMMC Level 2 the NIST SP 800-171 requirements named in each task. The page could not be fetched when this sprint was written, so the ISM control IDs below come from earlier ISM releases. s19-01 checks each one against the 2026-09 guideline before any code cites it.

Builds on: the SSP model and layout (s11-02 to s11-05), the POA&M (s11-03), the incident response plan (s15-09), the evidence plan (s10-04), risk and BIA as configuration (s15), suppliers and flow-down (s10, s15-08), metrics (s16) and the CMMC mapping (s17).

## Which documents, and why

| Document | ISM (check in s19-01) | NIST SP 800-171 | Evidence Hound already holds | Person writes | Task |
| --- | --- | --- | --- | --- | --- |
| System security plan | ISM-0041 | 3.12.4 | Done in s11 and s17 | - | - |
| Incident response plan | ISM-0043 | 3.6.1 | Done in s15-09 | - | - |
| Plan of action and milestones | ISM-1564 | 3.12.2 | Done in s11-03 | - | - |
| Security assessment report | ISM-1563 | 3.12.1 | Audit results, evidence and its dates, check results, gaps | Assessor's conclusions | s19-02 |
| Continuous monitoring plan | ISM-1163 | 3.12.3 | Collectors, schedules, checks, the evidence plan and its intervals, metrics, the weekly review | Escalation thresholds | s19-03 |
| Cyber security policy and policy statements | Organisational documentation controls | Each family's "establish policy" objectives in the CMMC assessment guide | Controls declared once with owners and descriptions, grouped by strategy or family | Purpose, scope, exceptions process | s19-04 |
| Risk assessment report | Risk management in the system lifecycle | 3.11.1 | Risk register, threats, matrices, exceptions (s15) | Context and risk appetite | s19-05 |
| Business continuity and recovery summary | Backups "in accordance with business continuity requirements" (Essential Eight regular backups) | 3.8.9 (backups) | BIA, recovery objectives, backup and restore-test evidence (s15-04, s15-05) | Recovery priorities | s19-06 |
| Supplier and supply chain report | Supply chain risk management | 3.12 and DFARS 252.204-7012 flow-down | Suppliers, contracts, data kinds, flow-down gaps (s10, s15-08) | - | s19-07 |
| Document register and annual review | Documentation is approved and reviewed at least annually | Policy review in each family | Every generated document, its version, approval and review date | Review decision | s19-08 |
| Authorisation package | System authorisation | 3.12 | SSP, SAR, POA&M, continuous monitoring plan | The authorising official's decision | s19-09 |

**Left out:** documents that are mostly judgement and that Evidence Hound has little data for. These are the cyber security strategy, the awareness and training programme, physical and personnel security plans (DISP readiness, s14, covers what Defence asks), change management and standard operating procedures, and media and emanation security documents. If a later need appears, the catalogue in s19-01 can list one as a template with only judgement sections.

**Policy documents as evidence:** the policy statements (s19-04), the continuous monitoring plan, the SSP, the IRP, the SAR and the document register are each the evidence for a control in their own right. When a version is approved, `hound` publishes it to the requirement rows it answers, using the same publisher as collectors. Approval is a person's action in CISO Assistant (Approver role), never `hound`'s.

### s19-01 Document catalogue and one generator pattern
- [ ] Done

**Story:** As a GRC team member, I want every document `hound` can draft listed in one place, with what it is built from and which controls it evidences, so that I can see what is generated, what I must write and what is due for review.

**Acceptance criteria**
- `documents/catalogue.yaml` (shipped, not per organisation) lists each document: id, title, source guidance with its URL, the ISM controls and NIST SP 800-171 requirements it evidences, its derived parts, its judgement sections and its review interval. Each ISM ID is checked against the 2026-09 Guidelines for Cyber Security Documentation, and the check is recorded in this task.
- The SSP (s11-04) and IRP (s15-09) generators move onto one pattern: a pure builder (scope plus what CISO Assistant holds gives a document model, every value with its source), a layout (model plus section texts gives Markdown), gaps and `--strict`. Their output does not change (golden files).
- `hound doc list` lists the catalogue with each document's status for the scope: not declared, draft with gaps, draft, approved (version), review due.
- `hound doc build <id> [--json] [--strict]` builds any catalogue document; `hound ssp build` and `hound irp build` stay as aliases.

**Tests first**
- `DocumentCatalogueTests.Every_document_names_its_controls_and_review_interval`
- `DocumentCatalogueTests.Catalogue_ism_ids_exist_in_the_ism_snapshot`
- `DocCommandTests.Ssp_alias_and_doc_build_give_the_same_output`

**Functional check**
- `hound doc list` against the example scope on the local CISO Assistant lists the SSP and IRP as drafts and every other document as not declared.

### s19-02 Security assessment report
- [ ] Done

**Story:** As an authorising official, I want a security assessment report built from the audit, so that I can see what was assessed, how, and what was found before I decide.

**Acceptance criteria**
- For a programme's audit: scope (boundary and controls assessed), method (automated checks with their collectors, manual attestations, sampling), each requirement's result with its evidence and the evidence's date, findings and their POA&M items, and the residual risk accepted (s15-03).
- Evidence older than its evidence-plan interval (s10-04) is a finding in its own right.
- The assessor's conclusions are a judgement section. A self-assessment says so, and so does an assessment by a third party.

**Tests first**
- `SarBuilderTests.Each_result_cites_its_evidence_and_date`
- `SarBuilderTests.Stale_evidence_is_a_finding`
- `SarLayoutTests.Renders_expected_markdown_from_fixture` (golden file)

**Functional check**
- Build the example Essential Eight SAR; it lists ISM-1486's evidence and the POA&M items from s11-03.

### s19-03 Continuous monitoring plan
- [ ] Done

**Story:** As a GRC team member with a few hours a week, I want the monitoring plan to be what `hound` actually runs, so that the plan never drifts from practice.

**Acceptance criteria**
- Derived from the deployment and scope: each collector and its schedule, each check and the requirements it maps to, the evidence plan's manual sources and intervals, the metrics (s16) and the weekly review (s10-07). Who is alerted, and when.
- Judgement section: thresholds that escalate to the committee or board (s12-05).
- A collector or manual source that has missed its interval shows in the plan as a deviation, with its date.

**Tests first**
- `MonitoringPlanBuilderTests.Lists_every_scheduled_collector_and_manual_source`
- `MonitoringPlanBuilderTests.Missed_interval_is_a_deviation`

**Functional check**
- Build the example plan; it lists the nightly Microsoft 365 and Sophos runs and the manual backup restore test.

### s19-04 Cyber security policy and policy statements
- [ ] Done

**Story:** As a small supplier without a policy writer, I want a policy set drafted from the controls I already declare, so that each control has a policy statement an assessor can find and I only write what is mine to decide.

**Acceptance criteria**
- One organisation-level cyber security policy (purpose, scope, roles from `access.yaml`, the exceptions process from s15-03, review), plus one policy statement section per Essential Eight strategy and per NIST SP 800-171 family. Each statement is built from the declared controls' descriptions and owners ("shall" wording comes from the control's description; nothing is invented).
- Families or strategies with no declared control are gaps, not empty sections.
- The approved policy is published as evidence to the policy requirement rows the catalogue names (CMMC "establish policy" assessment objectives, ISM documentation controls).

**Tests first**
- `PolicyBuilderTests.Each_strategy_lists_its_controls_owners`
- `PolicyBuilderTests.Family_without_controls_is_a_gap`
- `PolicyEvidenceTests.Approved_policy_is_evidence_for_named_rows`

**Functional check**
- Build the example policy; the multi-factor authentication statement names AC-MFA and its owner.

### s19-05 Risk assessment report
- [ ] Done

**Story:** As the CTO, I want the risk assessment written up from the risk register, so that the periodic risk assessment requirement is met without a separate spreadsheet.

**Acceptance criteria**
- From s15: the risk matrix used, the threats considered, each scenario with inherent and residual rating, treatment, owner and linked controls, and the exceptions accepted with their expiry.
- Judgement sections: context and risk appetite.
- Ratings are CISO Assistant's computed values, cited, never recomputed.

**Tests first**
- `RiskReportBuilderTests.Ratings_come_from_ciso_assistant_with_sources`
- `RiskReportBuilderTests.Expired_exception_is_a_gap`

**Functional check**
- Build the example risk report against the local CISO Assistant with the example risk study (s15-06).

### s19-06 Business continuity and recovery summary
- [ ] Done

**Story:** As a system owner, I want my recovery objectives and the evidence that recovery works in one document, so that backups are shown to meet business continuity requirements.

**Acceptance criteria**
- From s15-04 and s15-05: each system's recovery objectives, its backup evidence and the last restore test with its result and date, and systems whose last test is older than the interval.
- Judgement section: recovery priorities and who declares a disaster.

**Tests first**
- `RecoverySummaryBuilderTests.Untested_restore_is_a_gap`

**Functional check**
- Build the example summary; it shows the example backup service's restore test.

### s19-07 Supplier and supply chain report
- [ ] Done

**Story:** As a subcontractor, I want one report of my suppliers, what data they handle and which clauses I flow down, so that I can answer a prime's supply chain questionnaire.

**Acceptance criteria**
- From s10 and s15-08: each supplier with its solutions, criticality, data kinds (CUI, PII, export controlled), contract clauses, flowed-down obligations and gaps, and its last assessment.
- No judgement sections; gaps come from flow-down validation.

**Tests first**
- `SupplyChainReportTests.Missing_flow_down_is_listed_per_supplier`

**Functional check**
- Build the example report; it lists the machine-shop rehearsal's supplier contract missing its DFARS clause.

### s19-08 Document register and annual review
- [ ] Done

**Story:** As a GRC team member, I want each document's version, approval and next review tracked for me, so that nothing lapses and the review is one item in my weekly session.

**Acceptance criteria**
- Every generated document's approved versions, approval date and approver as recorded in CISO Assistant, and the "current as at" date printed in each document.
- A document past its review interval is an item in the weekly review (s10-07), where the agent drafts what changed since the last version.
- The register is itself published as evidence for the documentation review controls the catalogue names.

**Tests first**
- `DocumentRegisterTests.Document_past_review_interval_joins_the_weekly_review`
- `DocumentRegisterTests.Every_document_prints_current_as_at`

**Functional check**
- Approve the example SSP in the local CISO Assistant, move the clock a year on, and see it in `hound review`.

### s19-09 Authorisation package
- [ ] Done

**Story:** As an authorising official, I want the SSP, SAR, POA&M and monitoring plan bundled at one version, so that I authorise one consistent set and an assessor receives exactly that.

**Acceptance criteria**
- `hound doc package --programme <id>` writes one folder (Markdown, JSON, and a manifest with each file's hash and version) for the approved versions.
- A package whose documents disagree (for example a POA&M from a different audit read than the SSP) is refused.
- The authorising official's decision stays in CISO Assistant.

**Tests first**
- `AuthorisationPackageTests.Manifest_hashes_every_file`
- `AuthorisationPackageTests.Mismatched_versions_are_refused`

**Functional check**
- Package the example Essential Eight programme and verify the manifest.

### s19-10 Documentation and example
- [ ] Done

**Acceptance criteria**
- `docs/documents.md`: the catalogue, what each document is built from, what a person writes, and which controls each one evidences.
- The example compliance repo gains the judgement sections for every catalogue document, and all of them build without gaps.
- Cheat sheet updated.

**Tests first**
- `ExampleRepoTests.Every_catalogue_document_builds_without_gaps`

**Functional check**
- Following `docs/documents.md` verbatim, build and package the example documents.

## Discovered work

## Sprint review
