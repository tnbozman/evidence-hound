# s11 Essential Eight mapping and adoption guides

Status: Planned

**Goal:** the Essential Eight ML2 mapping completed, the guides an organisation needs to adopt the kit (moving registers out of spreadsheets, running manual sources, training), and two tiers of reporting: monthly operational reporting to the security committee and quarterly governance reporting to the board.

Reporting is per **programme** (amended by s10b-01, Nov 2026): a programme is a framework, the implementation groups reported against and the audit of record, declared in `scope/setup.yaml`. The Essential Eight at ML1 and ML2 is one programme; AI6 (s10b) is another, and NIST SP 800-171 (s16) will be a third. No report code assumes a framework, its groups or its strategy names: they come from the programme and its framework snapshot.

Risks, exceptions and risk acceptances in the reports are read from CISO Assistant, whether entered there by hand or, from release 2 (s14), applied from configuration. The ASD Essentials mapping, planned here before the release 1 reorder, is now [s13](s13-essentials-mapping.md).

### s11-01 Essential Eight mapping completed
- [ ] Done

**Story:** As a compliance owner whose audit of record is Essential Eight ML2, I want every ML1 and ML2 requirement either evidenced by a check or assigned to a named manual source, so that the audit has no row that nobody owns.

**Acceptance criteria**
- `mappings/essential-eight-2023.yaml` (started in s06-02, extended in s08 and s09) covers every ML1 and ML2 requirement with checks or a `manual_only` entry, and every `manual_only` entry names a manual source from the example config (s09-05).
- Every check ID from the reference and manual collectors maps to at least one Essential Eight requirement.
- `hound mapping coverage` prints automated, manual-only and uncovered counts per parent node and implementation group of any framework snapshot, for an audit's selected groups (for the Essential Eight: per strategy and maturity level).

**Tests first**
- `E8MappingTests.All_requirement_urns_exist_in_framework_snapshot`
- `E8MappingTests.Every_reference_check_is_mapped`
- `E8MappingTests.Every_ml1_and_ml2_requirement_is_covered_or_manual`
- `CoverageTests.Counts_per_strategy_and_maturity_level`

**Functional check**
- Against the local CISO Assistant with `essential-eight-2023` loaded: `hound run` with recordings fills the example ML2 audit; the coverage report matches the audit, with no `not_assessed` row that lacks a manual source.

### s11-02 Spreadsheet migration guide (no code)
- [ ] Done

**Story:** As an organisation moving from spreadsheets, I want a step-by-step guide using CISO Assistant's own import templates, so that I can migrate without custom code.

**Acceptance criteria**
- `docs/adopt/migrate-from-spreadsheets.md`: mapping common register columns to CISO Assistant Data Wizard templates (assets, applied controls, evidences, compliance assessments, third-party ecosystem: entities, solutions, contracts, entity assessments, representatives); import with `clica` on Community Edition; reconciliation checklist; parallel-run and sign-off.
- Fictional sample workbooks under `examples/migration/`.

**Tests first**
- Docs link and lint check. Sample workbooks open and contain the template's required sheets (small test using ClosedXML).

**Functional check**
- Import the sample workbooks into the local CISO Assistant with `clica` following the guide verbatim.

### s11-03 Operating guides and training
- [ ] Done

**Story:** As a small team without a CISO, I want role-based guides and short training, so that system owners and the committee can run the process.

**Acceptance criteria**
- `docs/operate/`: system owner guide (find your audit, upload manual evidence, update an action), collector operator guide (run, troubleshoot, rotate credentials), committee member guide (read the dashboard and pack).
- `docs/training/`: three short session outlines (system owners 60 min, committee 30 min, board 20 min) with exercises against the example deployment.
- `docs/govern/continuous-compliance-procedure.md`: generic continuous compliance procedure (authorising official, reassessment triggers, risk acceptance with expiry, evidence freshness windows).

**Tests first**
- Docs lint and link check.

**Functional check**
- Walk through the system owner guide on the local stack without help.

### s11-04 Report data model and snapshots
- [ ] Done

**Story:** As the capability owner, I want each reporting period's figures captured as a snapshot, so that reports show trends and can be reproduced later.

**Acceptance criteria**
- `ReportingPeriod` (month or quarter) and a `PostureSnapshot` record: per programme (s10b-01), its framework and version, audit of record, selected groups, requirement results and counts per group and per parent node, and its register summaries (s10b-06); applied control status counts, open exceptions and risk acceptances with expiry, open findings and remediation items with due dates, incidents in the period, collector run summaries, manual sources due and overdue, third-party reviews due.
- `hound report snapshot --period YYYY-MM` reads only from the CISO Assistant API and the local run store, and writes the snapshot as JSON to the evidence store, then uploads it to CISO Assistant as evidence on a configurable "Security reporting" applied control.
- Pure functions compute changes between two snapshots (`DiffSnapshots`), so every report is a function of snapshots, not live calls.
- When audits run in periods (s06-10), a period's snapshot is taken from its closed audit and the audit history files (s06-04), so a signed-off period's figures never change.

**Tests first**
- `SnapshotTests.Built_from_fixture_api_responses`
- `SnapshotTests.Two_programmes_are_counted_separately` (Essential Eight and AI6 fixtures)
- `SnapshotTests.Counts_use_programme_groups_not_maturity_levels`
- `DiffTests.Detects_requirement_result_changes_between_periods`
- `DiffTests.Detects_new_expired_and_closed_exceptions`
- Property: `DiffSnapshots(a, a)` is empty.

**Functional check**
- Run `hound run` twice with different recordings, take a snapshot after each, and confirm the diff lists the changed requirement.

### s11-05 Monthly security committee report (operational)
- [ ] Done

**Story:** As the security committee, I want a monthly operational report, so that we can see what changed, what is failing and what is overdue, and assign actions.

**Acceptance criteria**
- `hound report committee --month YYYY-MM` produces Markdown from the month's snapshot and the previous one, with these sections in order:
    1. Summary: three to five lines on what changed and what needs a decision.
    2. Collector health: each collector's last run, success rate for the month, evidence freshness against its window, failures and their cause.
    3. Control status changes: requirements and applied controls whose status changed, grouped by programme, then by parent node (the Essential Eight strategy, the AI6 practice), with each programme's register summary (s10b-06).
    4. Exceptions and risk acceptances: new, expiring within 60 days, and expired.
    5. Remediation (POA&M-style) progress: open items, completed this month, overdue, by owner and due date.
    6. Incidents: incidents opened and closed in CISO Assistant this month, with status.
    7. Manual evidence and third parties: manual evidence and supplier reviews due or overdue, by owner, from CISO Assistant's task analytics (s10-04); owners overdue for a second month are listed first. Each overdue occurrence is carried into section 8 as an open action with its owner until it is completed, because the committee is the only escalation after the reminder emails (s10-04).
    8. Actions: proposed actions with owner and due date, for the committee to confirm.
- Every figure links back to its CISO Assistant object.
- A committee pack guide (`docs/govern/security-committee.md`): terms of reference template, agenda, attendees by role, how decisions and actions are recorded back into CISO Assistant (applied controls, exceptions, tasks).

**Tests first**
- `CommitteeReportTests.Renders_expected_markdown_from_fixture` (golden file)
- `CommitteeReportTests.Expiring_exceptions_use_60_day_window`
- `CommitteeReportTests.Overdue_remediation_sorted_by_due_date`
- `CommitteeReportTests.Empty_sections_say_none_rather_than_disappear`
- `CommitteeReportTests.Groups_changes_by_programme_and_parent`

**Functional check**
- Generate the report from the local stack for the current month and walk through it against the example audit.

### s11-06 Quarterly board report (governance)
- [ ] Done

**Story:** As the board, I want a short quarterly governance report, so that we understand our compliance posture, its direction, the key risks and the risks management has accepted on our behalf.

**Acceptance criteria**
- `hound report board --quarter YYYY-Qn` produces a Markdown report of at most two pages plus an appendix, from the quarter-end snapshots, with these sections:
    1. Compliance position: one statement per programme for the authorising official to confirm (on track, on track with conditions, off track), with the reasons.
    2. Posture: per programme, each parent node at each of the programme's groups, with the trend over the last four quarters (for example the Essential Eight's eight strategies at ML1 and ML2, the Essentials domains by priority phase (s13), the six AI6 practices at Foundations), and the programme's register summary.
    3. Key risks: the top five risks against appetite, with owner, treatment and target date.
    4. Risk-acceptance decisions: risks and exceptions accepted this quarter, who accepted them, and when they expire; anything needing board endorsement highlighted.
    5. Assurance: collector coverage (automated versus manual share of requirements), evidence freshness, independent review status, tier 1 supplier assurance.
    6. Decisions and asks of the board.
- No operational detail in the body (no individual collector failures or task lists); that lives in the committee report and the appendix.
- Trend charts rendered as simple text tables or Mermaid charts so the report stays plain Markdown.
- A board reporting guide (`docs/govern/board-reporting.md`): risk appetite statement template, how the committee's monthly reports roll up into the quarter, and how the board's decisions are recorded in CISO Assistant.

**Tests first**
- `BoardReportTests.Trend_uses_last_four_quarters`
- `BoardReportTests.One_position_and_posture_table_per_programme`
- `BoardReportTests.Lists_risk_acceptances_made_in_quarter_with_expiry`
- `BoardReportTests.Excludes_operational_detail_from_body`
- `BoardReportTests.Renders_expected_markdown_from_fixture` (golden file)

**Functional check**
- Generate the board report from four fixture quarter snapshots and from the local stack; confirm it fits two pages when printed.

### s11-07 Drafting reports with Claude
- [ ] Done

**Story:** As the authorising official, I want to refine report drafts with Claude using live CISO Assistant data, so that the narrative is accurate and fast to produce.

**Acceptance criteria**
- `docs/govern/ai-assisted-reporting.md`: connect Claude to CISO Assistant's MCP server (read-only service account), prompts for the committee summary and the board compliance position, and the rule that every figure must come from the generated report, not the model.

**Tests first**
- Docs lint and link check.

**Functional check**
- Follow the guide against the local stack and produce a committee summary from the generated report.

## Discovered work

## Sprint review
