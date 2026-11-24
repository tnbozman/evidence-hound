# s12 Essential Eight mapping and adoption guides

Status: In progress

**Goal:** the Essential Eight ML2 mapping completed, the guides an organisation needs to adopt the kit (moving registers out of spreadsheets, running manual sources, training), and two tiers of reporting: monthly operational reporting to the security committee and quarterly governance reporting to the board.

Reporting is per **programme** (amended by s11-01, Nov 2026): a programme is a framework, the implementation groups reported against and the audit of record, declared in `scope/setup.yaml`. The Essential Eight at ML1 and ML2 is one programme; AI6 (s11) is another, and NIST SP 800-171 (s17) will be a third. No report code assumes a framework, its groups or its strategy names: they come from the programme and its framework snapshot.

Risks, exceptions and risk acceptances in the reports are read from CISO Assistant, whether entered there by hand or, from release 2 (s15), applied from configuration. The ASD Essentials mapping, planned here before the release 1 reorder, is now [s14](s14-essentials-mapping.md).

### s12-01 Essential Eight mapping completed
- [x] Done

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

**Progress (2026-11-24)**
- The mapping already covered every ML1 and ML2 requirement after s09 and s10 (`E8MappingTests`; the test planned as `Every_ml1_and_ml2_requirement_is_covered_or_manual` was renamed to match). Every requirement without a collector is mapped to a check a person attests through a manual source; `manual_only` stays empty.
- New: `hound mapping coverage <mapping> --framework <snapshot> [--groups 1,2] [--manual <config, template or folder>]... [--json]` (`MappingCoverage.Count` in the core, `ManualSourceChecks` reads `type: manual` collectors). Documented in `docs/mapping-files.md` and the cheat sheet.
- Functional check: `hound mapping coverage mappings/essential-eight-2023.yaml --framework frameworks/essential-eight-2023.json --groups 1,2 --manual examples/manual-sources/essential-eight --manual examples/example-essential-eight.yaml` printed 16 rows (8 strategies at ML1 and ML2) and `Total: 46 automated, 109 manual, 0 uncovered`. The ML2 audit filled by `hound run` against CISO Assistant is `E8RunFunctionalTests.Run_fills_the_ml2_audit_and_every_row_of_a_repeated_control`, run by CI's functional job: CISO Assistant's images cannot be pulled in this cloud session (the proxy blocks GitHub's container blob host).

### s12-02 Spreadsheet migration guide (no code)
- [ ] Done

**Story:** As an organisation moving from spreadsheets, I want a step-by-step guide using CISO Assistant's own import templates, so that I can migrate without custom code.

**Acceptance criteria**
- `docs/adopt/migrate-from-spreadsheets.md`: mapping common register columns to CISO Assistant Data Wizard templates (assets, applied controls, evidences, compliance assessments, third-party ecosystem: entities, solutions, contracts, entity assessments, representatives); import with `clica` on Community Edition; reconciliation checklist; parallel-run and sign-off.
- Fictional sample workbooks under `examples/migration/`.

**Tests first**
- Docs link and lint check. Sample workbooks open and contain the template's required sheets (small test using ClosedXML).

**Functional check**
- Import the sample workbooks into the local CISO Assistant with `clica` following the guide verbatim.

### s12-03 Operating guides and training
- [ ] Done

**Story:** As a small team without a CISO, I want role-based guides and short training, so that system owners and the committee can run the process.

**Acceptance criteria**
- `docs/operate/`: system owner guide (find your audit, upload manual evidence, update an action), collector operator guide (run, troubleshoot, rotate credentials), committee member guide (read the dashboard and pack).
- `docs/training/`: three short session outlines (system owners 60 min, committee 30 min, board 20 min) with exercises against the example deployment.
- The guides are built around a GRC member with 2 to 8 hours a week and the guided weekly review (s10-07, [design](../design/guided-review.md)): what to do each week, each month and each quarter within that budget, and what the agent does for them (amended 2026-11-24).
- `docs/govern/continuous-compliance-procedure.md`: generic continuous compliance procedure (authorising official, reassessment triggers, risk acceptance with expiry, evidence freshness windows).

**Tests first**
- Docs lint and link check.

**Functional check**
- Walk through the system owner guide on the local stack without help.

### s12-04 Report data model and snapshots
- [ ] Done

**Story:** As the capability owner, I want each reporting period's figures captured as a snapshot, so that reports show trends and can be reproduced later.

**Acceptance criteria**
- `ReportingPeriod` (month or quarter) and a `PostureSnapshot` record: per programme (s11-01), its framework and version, audit of record, selected groups, requirement results and counts per group and per parent node, and its register summaries (s11-06); applied control status counts, open exceptions and risk acceptances with expiry, open findings and remediation items with due dates, incidents in the period, collector run summaries, manual sources due and overdue, third-party reviews due.
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

### s12-05 Monthly security committee report (operational)
- [ ] Done

**Story:** As the security committee, I want a monthly operational report, so that we can see what changed, what is failing and what is overdue, and assign actions.

**Acceptance criteria**
- `hound report committee --month YYYY-MM` produces Markdown from the month's snapshot and the previous one, with these sections in order:
    1. Summary: three to five lines on what changed and what needs a decision.
    2. Collector health: each collector's last run, success rate for the month, evidence freshness against its window, failures and their cause.
    3. Control status changes: requirements and applied controls whose status changed, grouped by programme, then by parent node (the Essential Eight strategy, the AI6 practice), with each programme's register summary (s11-06).
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

### s12-06 Quarterly board report (governance)
- [ ] Done

**Story:** As the board, I want a short quarterly governance report, so that we understand our compliance posture, its direction, the key risks and the risks management has accepted on our behalf.

**Acceptance criteria**
- `hound report board --quarter YYYY-Qn` produces a Markdown report of at most two pages plus an appendix, from the quarter-end snapshots, with these sections:
    1. Compliance position: one statement per programme for the authorising official to confirm (on track, on track with conditions, off track), with the reasons.
    2. Posture: per programme, each parent node at each of the programme's groups, with the trend over the last four quarters (for example the Essential Eight's eight strategies at ML1 and ML2, the Essentials domains by priority phase (s14), the six AI6 practices at Foundations), and the programme's register summary.
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

### s12-07 Drafting reports with Claude
- [ ] Done

**Story:** As the authorising official, I want to refine report drafts with Claude using live CISO Assistant data, so that the narrative is accurate and fast to produce.

**Acceptance criteria**
- `docs/govern/ai-assisted-reporting.md`: connect Claude to CISO Assistant's MCP server (read-only service account), prompts for the committee summary and the board compliance position, and the rule that every figure must come from the generated report, not the model.

**Tests first**
- Docs lint and link check.

**Functional check**
- Follow the guide against the local stack and produce a committee summary from the generated report.

### s12-08 NIST SP 800-171 crosswalk (added 2026-11-24)
- [ ] Done

Moved earlier from s17 at the maintainer's request (2026-11-24), lightweight: US suppliers have a window while CMMC Phase 2 is paused.
Full CMMC Level 2 (mapping, SSP layout, POA&M) stays in s17.

**Story:** As a supplier holding CUI, I want to see which NIST SP 800-171 requirements my existing Essential Eight evidence already
answers, so that I know my gap before I pay for an assessment.

**Acceptance criteria**
- `hound mapping crosswalk --from essential-eight-2023 --to <framework urn>` uses a mapping library CISO Assistant ships between the two
  frameworks when one is loaded (for example through NIST CSF), and otherwise a reviewed crosswalk file in `mappings/crosswalks/`.
- It prints, as Markdown or JSON, each target requirement with the checks and manual sources that would answer it today, those only
  partly answered, and those with nothing, with the current result of each source from the audit of record.
- No new audit is created and nothing is written to CISO Assistant; the crosswalk is advisory until s17.
- The adoption guide (s12-03) explains how to use it with a prime's questionnaire.

**Tests first**
- `CrosswalkTests.Every_target_requirement_is_answered_partly_answered_or_open`
- `CrosswalkTests.Uses_a_loaded_mapping_library_before_the_file`

**Functional check**
- `hound mapping crosswalk` against the local CISO Assistant with the example Essential Eight audit prints the 800-171 gap list.

**Progress (2026-11-24)**
- `mappings/crosswalks/essential-eight-2023-to-nist-sp-800-171-rev2.yaml`: all 110 Rev 2 requirements, links only to Essential Eight
  refs the mapping answers (tested), marked advisory and unreviewed. Schema `schemas/crosswalk.schema.json`; loader `CrosswalkLoader`.
- `hound mapping crosswalk --from <programme> --config hound.yaml [--to] [--file] [--offline] [--json]`: `--file`, else the repo's
  `mappings/crosswalks/`, else hound's own copy. Today's results come from the programme's audit of record. Reads only.
- Deviation from the first criterion: CISO Assistant v4.3.4 has no API that lists a mapping library's requirement links
  (`/api/frameworks/{id}/mappings/` lists target frameworks only), so hound cannot read a loaded library yet. `Crosswalks.Choose` keeps
  the seam; the docs say so. Recorded under Discovered work.
- Functional check (offline, local): `hound mapping crosswalk --from essential-eight --offline --scope examples/compliance-repo/scope
  --config examples/compliance-repo/hound.yaml` printed `Total: 1 answered, 30 partly answered, 79 open, of 110 requirements.`
  The CA-backed check is `CrosswalkFunctionalTests` (CI; cloud sessions cannot pull the images), which keeps the Markdown in
  `test-results/reports/`.

### s12-09 Rehearse trial, onboarding and sustaining GRC (standing, every sprint)
- [ ] Done

Standing task from 2026-11-24 at the maintainer's request: Claude does this every sprint, not the maintainer. See [the sprint lifecycle](README.md#lifecycle).

**Story:** As a busy maintainer, I want the agent to follow our own trial, onboarding and upkeep guides end to end every sprint, so that errors in the docs and in hound are found and fixed before a real small team meets them.

**Acceptance criteria**
- A subagent with only what an organisation's agent would have (the repo's docs, skills and example files, the dev servers and the local CISO Assistant) follows the guides literally, as the small defence-industry Cyber GRC team persona (2 to 8 hours a week, leaning on AI):
    1. Trial: `docs/tutorials/local-trial.md` (`make trial`).
    2. Onboarding: `docs/tutorials/first-deployment.md`, the onboarding tutorial and agent-driven onboarding with the skills (`docs/onboarding.md`).
    3. Sustaining GRC: the weekly review, manual evidence and `hound run` upkeep (`docs/manual-evidence.md`, `docs/running.md`, the guided review).
    4. The AI governance walkthrough (`docs/tutorials/ai-governance.md`, the carried s11-08 trial).
    5. This sprint's new commands and guides.
- The rehearsal never runs `hound approve` or `hound manual approve` for a person; the trial's committed approved copies stand in.
- Every error in a doc or in hound is recorded under "Discovered work" with the step that hit it, then fixed in this sprint (red, green as usual), or carried with a reason.
- Docs are updated for anything this sprint changed.
- Steps that only a real tenant, firewall or machine can prove are listed, not blocked on.

**Functional check**
- The rehearsal log (steps followed, errors found, fixes and their commits) is recorded under this task.

### s12-10 Improve: quality and flow (standing, about 15% of each sprint)
- [ ] Done

Standing task from 2026-11-24, after the maintainer asked for 10 to 20% of effort on refactoring, cleanup, guides and quality (the DevOps Three Ways: flow, feedback, continual learning).

**Acceptance criteria**
- At sprint start, triage the previous sprint's "Discovered work": fix, schedule or drop each item, with a reason.
- One or two improvement items, chosen from: docs drift (every schema key and command mentioned in the docs exists, and every command is documented), duplicate files that must match (for example copies of mappings and frameworks under `examples/`), legacy files no longer used, slow or flaky tests, CI feedback time.
- Before release 1 (s13): remove legacy files and stale references.
- A short retro in the sprint review: what slowed the sprint, what to change next sprint.

**Functional check**
- The items done and the retro are recorded under this task.

## Carried from s09 to s12

Human trials and onboarding tests, moved from s11 on 2026-11-24 at the maintainer's request. Built and tested in CI. Details stay under
each task in its own sprint file. From 2026-11-24 Claude's rehearsal (s12-09) closes the ones a person's own environment is not needed
for (s11-08, s09-38, s09-40 to s09-42, s09-54, s09-57); the items that need a real tenant or firewall (s09-01, s09-15, s09-17,
s09-29, s09-39) stay open until one is available.
- [ ] s11-08 AI governance walkthrough: someone who has not read AI6 follows `docs/tutorials/ai-governance.md` on the local stack without help, and notes anything unclear under Discovered work.
- [ ] s09-01 Application control policies from Intune: a real Graph export for the App Control for Business setting IDs.
- [ ] s09-15 Firewall firmware update timeliness: whether the read-only role may call the firmware upgrade check, and a dry run on a real tenant.
- [ ] s09-17 The Sophos trial on a real tenant.
- [ ] s09-29 Microsoft 365 checks follow the capability map: a run against a real tenant.
- [ ] s09-38 to s09-42 Agent-driven onboarding and manual evidence: a full run with an agent, including s09-39 on a real XGS firewall.
- [ ] s09-54 and s09-57 `make trial` end to end.

## Discovered work

- s12-08: CISO Assistant v4.3.4 exposes no list of a mapping library's requirement links, so `hound mapping crosswalk` cannot use a
  loaded library before the file. Revisit when CISO Assistant adds one (s17, with CMMC). The output says the file is used.
- s12-10 Improve: the committee report's "collector success rate for the month" needs run history; the run store keeps only each
  collector's latest run.
- s12-10 Improve: `ReportCommands` used `.Result` inside `Bind` (sync over async) when reading snapshots. Fixed.
- Cloud sessions cannot pull CISO Assistant's images (ghcr.io blobs are blocked by the proxy), so functional checks against CISO Assistant run in CI's functional job; dev servers and `--dry-run` runs work locally.

## Sprint review
