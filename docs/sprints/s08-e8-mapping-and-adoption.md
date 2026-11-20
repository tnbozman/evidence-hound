# s08 Essential Eight mapping and adoption guides

Status: Planned

**Goal:** a complete Essential Eight ML2 mapping for the reference checks, and the guides an organisation needs to adopt the kit: moving registers out of spreadsheets, running manual sources, training and governance reporting.

### s08-01 Essential Eight mapping file
- [ ] Done

**Story:** As a compliance owner, I want the reference checks mapped to Essential Eight ML2 requirements, so that a new deployment produces E8 results on its first run.

**Acceptance criteria**
- `mappings/essential-eight-2023.yaml` targets CISO Assistant's built-in Essential Eight (Nov 2023) framework URN.
- Every check ID from the reference and manual collectors maps to at least one ML1 or ML2 requirement; every ML2 requirement either has checks or is listed under `manual_only` with a suggested manual source.
- A coverage report (`hound mapping coverage`) prints automated, manual-only and uncovered counts per mitigation strategy.

**Tests first**
- `E8MappingTests.All_requirement_refs_exist_in_library_snapshot` (snapshot of the framework's requirement refs committed under `tests/fixtures`)
- `E8MappingTests.Every_reference_check_is_mapped`
- `CoverageTests.Counts_per_strategy`

**Functional check**
- Against the local CISO Assistant with the E8 library loaded: `hound run` with recordings fills the example audit; coverage report matches the audit.

### s08-02 Spreadsheet migration guide (no code)
- [ ] Done

**Story:** As an organisation moving from spreadsheets, I want a step-by-step guide using CISO Assistant's own import templates, so that I can migrate without custom code.

**Acceptance criteria**
- `docs/adopt/migrate-from-spreadsheets.md`: mapping common register columns to CISO Assistant Data Wizard templates (assets, applied controls, evidences, compliance assessments, third-party ecosystem: entities, solutions, contracts, entity assessments, representatives); import with `clica` on Community Edition; reconciliation checklist; parallel-run and sign-off.
- Fictional sample workbooks under `examples/migration/`.

**Tests first**
- Docs link and lint check. Sample workbooks open and contain the template's required sheets (small test using ClosedXML).

**Functional check**
- Import the sample workbooks into the local CISO Assistant with `clica` following the guide verbatim.

### s08-03 Operating guides and training
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

### s08-04 Committee and board report templates
- [ ] Done

**Story:** As an authorising official, I want monthly and quarterly report templates fed from CISO Assistant, so that reporting takes minutes, not days.

**Acceptance criteria**
- `hound report committee --month YYYY-MM` produces Markdown: compliance status per audit and change since last month, collector health and evidence freshness, overdue applied controls, exceptions expiring, third-party reviews due.
- `hound report board --quarter YYYY-Qn` produces a two-page summary: compliance statement placeholder, maturity per strategy with trend, top risks, supplier assurance.
- Reports read only from the CISO Assistant API; formatting is pure functions over fetched data.
- A guide shows how to refine the draft with Claude using CISO Assistant's MCP server.

**Tests first**
- `CommitteeReportTests.Renders_expected_markdown_from_fixture` (golden file)
- `BoardReportTests.Trend_uses_last_four_quarters`

**Functional check**
- Generate both reports from the local stack after a `hound run`.

## Discovered work

## Sprint review
