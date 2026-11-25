# s19 part B: Security document generators (droppable)

Status: Planned, waiting on the maintainer: part B of s19, or a later sprint

Release: 3, unless moved

Added at the maintainer's request (2026-11-24) as the final sprint, after release 2. On 2026-11-25 the maintainer made s19 a principal
software engineer and CISO review ([s19-review-and-release-3.md](s19-review-and-release-3.md)). These tasks are its droppable part B:
they start only after part A is done, and only if the maintainer keeps them in s19. The CISO review (s19b-07) may cut or reorder them;
its result is recorded here before any of them starts.

**Goal:** `hound` drafts the security documents a CMMC Level 2 or Essential Eight Maturity Level 2 organisation must keep, from the data it already manages, the same way it drafts the system security plan (s11-04) and the incident response plan (s15-09). Only documents that Evidence Hound's data can largely fill are in scope. A person writes the judgement parts, and approves each version in CISO Assistant. Then the approved document is published as evidence for the controls that require it, because several of these documents are themselves the evidence for policy and documentation controls.

Source: ASD's ISM, Guidelines for Cyber Security Documentation, September 2026 (https://www.cyber.gov.au/ism/2026-09/guidelines-for-cyber-security-documentation), and for CMMC Level 2 the NIST SP 800-171 requirements named in each task. The ISM control IDs and contents below were checked against the maintainer's copy of the September 2026 PDF on 2026-11-24.

What the guideline requires:
- Organisation level: a cyber security strategy (ISM-0039). Organisation-level documents are approved by the CISO, and system-specific ones by the system's authorising officer (ISM-0047). A system's security architecture is approved before the system is developed (ISM-1739). All documentation is reviewed at least annually and carries a "current as at [date]" statement (ISM-0888). Approved documentation, and notice of later changes, is communicated to all stakeholders (ISM-1602).
- Per system: a system security plan (ISM-0041), a cyber security incident response plan (ISM-0043), a change and configuration management plan (ISM-0912), a continuous monitoring plan (ISM-1163), a security assessment report produced by the assessor (ISM-1563), and a plan of action and milestones produced by the system owner after the assessment (ISM-1564).
- It also points to registers, policies and procedures in other guidelines: system registers, incident registers, approved supplier lists, managed service and outsourced cloud service registers, software registers, AI agent registers, awareness training registers, and usage policies (system, email, web, mobile devices, general-purpose AI, and others). s19b-04 and s19b-11 cover the ones hound has data for.

The ASD SSP annex template the guideline points to is used for the ISM annex (s11-16). Small and medium businesses often have no CISO. hound therefore names the approver of organisation-level documents as a configurable role, `cyber_grc_lead`: the person responsible for cyber GRC. That is the CISO, or an appropriate manager the chief security officer (CSO) or equivalent has delegated, such as a CTO or IT manager. The register records who approved and under which delegation.

Builds on: the SSP model and layout (s11-02 to s11-05), the POA&M (s11-03), the incident response plan (s15-09), the evidence plan (s10-04), risk and BIA as configuration (s15), suppliers and flow-down (s10, s15-08), metrics (s16) and the CMMC mapping (s17).

## Which documents, and why

| Document | ISM (Sep 2026) | NIST SP 800-171 | Evidence Hound already holds | Person writes | Task |
| --- | --- | --- | --- | --- | --- |
| System security plan | ISM-0041 | 3.12.4 | Done in s11 and s17 | - | - |
| Incident response plan | ISM-0043 | 3.6.1 | Done in s15-09 | - | - |
| Plan of action and milestones | ISM-1564 | 3.12.2 | Done in s11-03 | - | - |
| Security assessment report | ISM-1563 | 3.12.1 | Audit results, evidence and its dates, check results, gaps | Assessor's conclusions, strengths and weaknesses, recommended remediation | s19b-02 |
| Change and configuration management plan | ISM-0912 | 3.4.1 to 3.4.5 | Baselines as checks, the config repo's pull requests, `scope plan` and `scope apply`, approvers in `access.yaml` | What is routine or urgent, who is consulted | s19b-10 |
| Continuous monitoring plan | ISM-1163 | 3.12.3 | Collectors, schedules, checks, the evidence plan and its intervals, metrics, the weekly review | Escalation thresholds | s19b-03 |
| Cyber security policy and policy statements | ISM-0047 (approval), ISM-0888 (review); usage policies in other guidelines | Each family's "establish policy" objectives in the CMMC assessment guide | Controls declared once with owners and descriptions, grouped by strategy or family | Purpose, scope, exceptions process | s19b-04 |
| Risk assessment report | Risk management in the system lifecycle | 3.11.1 | Risk register, threats, matrices, exceptions (s15) | Context and risk appetite | s19b-05 |
| Business continuity and recovery summary | Backups "in accordance with business continuity requirements" (Essential Eight regular backups) | 3.8.9 (backups) | BIA, recovery objectives, backup and restore-test evidence (s15-04, s15-05) | Recovery priorities | s19b-06 |
| Supplier and supply chain report, and registers | Approved supplier list, managed service and outsourced cloud service registers (procurement and outsourcing guidelines) | 3.12 and DFARS 252.204-7012 flow-down | Suppliers, contracts, data kinds, flow-down gaps (s10, s15-08) | - | s19b-07 |
| Document register, annual review and communication | ISM-0047, ISM-0888, ISM-1602 | Policy review in each family | Every generated document, its version, approval and review date | Review decision | s19b-08 |
| Authorisation package | ISM-0047, ISM-1739 (system documents approved by the authorising officer) | 3.12 | SSP, SAR, POA&M, continuous monitoring plan | The authorising official's decision | s19b-09 |

**Left out:** documents that are mostly judgement and that Evidence Hound has little data for. These are the cyber security strategy (ISM-0039), the awareness and training programme, physical and personnel security plans (DISP readiness, s14, covers what Defence asks), standard operating procedures, cabling, telephone and media documents, and floor plans. The change and configuration management plan (ISM-0912) was first left out, and is now in (s19b-10), because the config repo's pull requests and `scope plan` already are the change process. If a later need appears, the catalogue in s19b-01 can list one as a template with only judgement sections.

**Policy documents as evidence:** the policy statements (s19b-04), the continuous monitoring plan, the SSP, the IRP, the SAR and the document register are each the evidence for a control in their own right. When a version is approved, `hound` publishes it to the requirement rows it answers, using the same publisher as collectors. Approval is a person's action in CISO Assistant (Approver role), never `hound`'s.

### s19b-01 Document catalogue and one generator pattern
- [ ] Done

**Story:** As a GRC team member, I want every document `hound` can draft listed in one place, with what it is built from and which controls it evidences, so that I can see what is generated, what I must write and what is due for review.

**Acceptance criteria**
- `documents/catalogue.yaml` (shipped, not per organisation) lists each document: id, title, level (organisation or system, which decides the approver under ISM-0047), source guidance with its URL, the ISM controls and NIST SP 800-171 requirements it evidences, the content items the control lists, its derived parts, its judgement sections and its review interval (at most a year, ISM-0888).
- Each document's content items come from the control's text. For example ISM-0043's eight items and ISM-1563's five. A layout that does not cover every listed item fails a test.
- The SSP (s11-04) and IRP (s15-09) generators move onto one pattern: a pure builder (scope plus what CISO Assistant holds gives a document model, every value with its source), a layout (model plus section texts gives Markdown), gaps and `--strict`. Their output does not change (golden files).
- `hound doc list` lists the catalogue with each document's status for the scope: not declared, draft with gaps, draft, approved (version), review due.
- `hound doc build <id> [--json] [--strict]` builds any catalogue document; `hound ssp build` and `hound irp build` stay as aliases.

**Tests first**
- `DocumentCatalogueTests.Every_document_names_its_controls_and_review_interval`
- `DocumentCatalogueTests.Catalogue_ism_ids_exist_in_the_ism_snapshot`
- `DocumentCatalogueTests.Every_listed_content_item_has_a_section`
- `DocCommandTests.Ssp_alias_and_doc_build_give_the_same_output`

**Functional check**
- `hound doc list` against the example scope on the local CISO Assistant lists the SSP and IRP as drafts and every other document as not declared.

### s19b-02 Security assessment report
- [ ] Done

**Story:** As an authorising official, I want a security assessment report built from the audit, so that I can see what was assessed, how, and what was found before I decide.

**Acceptance criteria**
- Covers ISM-1563's five items: the scope of the assessment (boundary and controls assessed, and the method: automated checks with their collectors, manual attestations, sampling); the system's strengths and weaknesses; the security risks of operating the system (from s15, and the residual risk accepted under s15-03); the effectiveness of each control's implementation (each requirement's result with its evidence and the evidence's date, in the ASD status vocabulary from s11-16); and recommended remediation actions, which become the POA&M (ISM-1564).
- Evidence older than its evidence-plan interval (s10-04) is a finding in its own right.
- ISM-1563 says the assessor produces the report, so hound only drafts it. The assessor's conclusions, strengths and weaknesses, and recommendations are judgement sections, and the report names the assessor. A self-assessment says so.

**Tests first**
- `SarBuilderTests.Each_result_cites_its_evidence_and_date`
- `SarBuilderTests.Stale_evidence_is_a_finding`
- `SarLayoutTests.Renders_expected_markdown_from_fixture` (golden file)

**Functional check**
- Build the example Essential Eight SAR; it lists ISM-1486's evidence and the POA&M items from s11-03.

### s19b-03 Continuous monitoring plan
- [ ] Done

**Story:** As a GRC team member with a few hours a week, I want the monitoring plan to be what `hound` actually runs, so that the plan never drifts from practice.

**Acceptance criteria**
- Covers ISM-1163's three items. Security assessment activities: vulnerability scans (the scan import from s09), vulnerability assessments and penetration tests (manual sources with intervals), and each collector, check and schedule. Analysing identified vulnerabilities for their potential impact: the scan findings by severity and the systems they affect. Implementing mitigations based on risk, effectiveness and cost: the POA&M and risk treatment (s15).
- Also from the deployment and scope: the evidence plan's manual sources and intervals, the metrics (s16) and the weekly review (s10-07). Who is alerted, and when.
- Judgement section: thresholds that escalate to the committee or board (s12-05).
- A collector or manual source that has missed its interval shows in the plan as a deviation, with its date.

**Tests first**
- `MonitoringPlanBuilderTests.Lists_every_scheduled_collector_and_manual_source`
- `MonitoringPlanBuilderTests.Missed_interval_is_a_deviation`

**Functional check**
- Build the example plan; it lists the nightly Microsoft 365 and Sophos runs and the manual backup restore test.

### s19b-04 Cyber security policy and policy statements
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

### s19b-05 Risk assessment report
- [ ] Done

**Story:** As the person responsible for cyber GRC, I want the risk assessment written up from the risk register, so that the periodic risk assessment requirement is met without a separate spreadsheet.

**Acceptance criteria**
- From s15: the risk matrix used, the threats considered, each scenario with inherent and residual rating, treatment, owner and linked controls, and the exceptions accepted with their expiry.
- Judgement sections: context and risk appetite.
- Ratings are CISO Assistant's computed values, cited, never recomputed.

**Tests first**
- `RiskReportBuilderTests.Ratings_come_from_ciso_assistant_with_sources`
- `RiskReportBuilderTests.Expired_exception_is_a_gap`

**Functional check**
- Build the example risk report against the local CISO Assistant with the example risk study (s15-06).

### s19b-06 Business continuity and recovery summary
- [ ] Done

**Story:** As a system owner, I want my recovery objectives and the evidence that recovery works in one document, so that backups are shown to meet business continuity requirements.

**Acceptance criteria**
- From s15-04 and s15-05: each system's recovery objectives, its backup evidence and the last restore test with its result and date, and systems whose last test is older than the interval.
- Judgement section: recovery priorities and who declares a disaster.

**Tests first**
- `RecoverySummaryBuilderTests.Untested_restore_is_a_gap`

**Functional check**
- Build the example summary; it shows the example backup service's restore test.

### s19b-07 Supplier and supply chain report
- [ ] Done

**Story:** As a subcontractor, I want one report of my suppliers, what data they handle and which clauses I flow down, so that I can answer a prime's supply chain questionnaire.

**Acceptance criteria**
- From s10 and s15-08: each supplier with its solutions, criticality, data kinds (CUI, PII, export controlled), contract clauses, flowed-down obligations and gaps, and its last assessment.
- No judgement sections; gaps come from flow-down validation.

**Tests first**
- `SupplyChainReportTests.Missing_flow_down_is_listed_per_supplier`

**Functional check**
- Build the example report; it lists the machine-shop rehearsal's supplier contract missing its DFARS clause.

### s19b-08 Document register and annual review
- [ ] Done

**Story:** As a GRC team member, I want each document's version, approval and next review tracked for me, so that nothing lapses and the review is one item in my weekly session.

**Acceptance criteria**
- Every generated document's approved versions, approval date and approver as recorded in CISO Assistant, and the "current as at" date printed in each document (ISM-0888). The approver is checked against the document's level (ISM-0047): the `cyber_grc_lead` for organisation-level documents, and the system's authorising officer for system-specific ones.
- `scope/access.yaml` declares the `cyber_grc_lead` role with its holder and, when the holder is not a CISO, a `delegated_by` (the CSO or equivalent) and the delegation's date. A delegate with no `delegated_by` is a validation problem (`access.grc_lead_delegation_missing`). The role's title in documents is configurable (for example "Chief Information Security Officer" or "IT Manager (delegated by the CSO)"), and nothing in the repo names a real person or business.
- On approval, a change notice (what changed since the last version) is drafted for stakeholders and recorded as sent (ISM-1602). hound drafts it, and a person sends it.
- A document past its review interval is an item in the weekly review (s10-07), where the agent drafts what changed since the last version.
- The register is itself published as evidence for the documentation review controls the catalogue names.

**Tests first**
- `DocumentRegisterTests.Document_past_review_interval_joins_the_weekly_review`
- `DocumentRegisterTests.Every_document_prints_current_as_at`
- `DocumentRegisterTests.Wrong_level_approver_is_a_gap`
- `AccessScopeTests.Grc_lead_delegate_needs_delegated_by`
- `DocumentRegisterTests.Approval_drafts_a_change_notice`

**Functional check**
- Approve the example SSP in the local CISO Assistant, move the clock a year on, and see it in `hound review`.

### s19b-09 Authorisation package
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

### s19b-10 Change and configuration management plan
- [ ] Done

**Story:** As a small IT team, I want the change plan to describe the process we already follow in the config repo, so that ISM-0912 and CMMC's configuration management practices are met without a second process.

**Acceptance criteria**
- Covers ISM-0912's seven items. Authorised baseline configurations: the checks that enforce them, per system, with their last result. Routine and urgent changes: a judgement section. How changes are requested, tracked and documented: pull requests to the config repo, `scope plan` and `scope apply`, and the audit trail in CISO Assistant. Who is consulted, who approves and who is notified: from `access.yaml` and the repo's review rules, with routine and urgent shown separately. Processes before, during and after a change: a judgement section that names the plan and apply steps.
- A check that drifts from its baseline appears as a deviation with its date.

**Tests first**
- `ChangePlanBuilderTests.Covers_every_ism_0912_item`
- `ChangePlanBuilderTests.Baselines_come_from_checks_with_last_result`

**Functional check**
- Build the example plan; it lists the Microsoft 365 baseline checks and the approver from the example `access.yaml`.

### s19b-11 Registers
- [ ] Done

**Story:** As a GRC team member, I want the registers the ISM points to printed from what CISO Assistant holds, so that I never keep a second spreadsheet.

**Acceptance criteria**
- `hound doc build` gains registers: system register (assets), approved supplier list and outsourced cloud and managed service register (entities and solutions, s10), AI agent register (s11-12), and incident register (s15-07). Each row has its source, and each register has a "current as at" date.
- Each register is published as evidence to the requirement rows the catalogue names.

**Tests first**
- `RegisterBuilderTests.Approved_supplier_list_comes_from_entities_with_sources`
- `RegisterBuilderTests.Cloud_service_register_lists_solutions_and_data_kinds`

**Functional check**
- Build the example registers against the local CISO Assistant.

### s19b-12 Documentation and example
- [ ] Done

**Acceptance criteria**
- `docs/documents.md`: the catalogue, what each document is built from, what a person writes, and which controls each one evidences.
- The example compliance repo gains the judgement sections for every catalogue document, and all of them build without gaps.
- Cheat sheet updated.

**Tests first**
- `ExampleRepoTests.Every_catalogue_document_builds_without_gaps`

**Functional check**
- Following `docs/documents.md` verbatim, build and package the example documents.

The standing rehearsal and improvement tasks are part A's (s19-10 and s19-11).

## Discovered work

## Sprint review
