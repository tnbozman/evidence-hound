# s15 Risk, threats and business impact analysis as configuration

Status: In progress

Release: 2

Was s09 before the release 1 reorder.

**Goal:** the risk register, its threats and risk matrices, security exceptions and business impact analyses live in the private configuration repo as YAML, reviewed through pull requests and applied to CISO Assistant by `hound`. Approvals stay with people in CISO Assistant. Checks feed what they can prove, such as whether recovery was tested.

Builds on s06: scope files, `scope validate`, `scope plan` and `scope apply`, controls declared once, and the configuration inventory (s06-05). Every object here is matched by `ref_id` (or URN for library objects), validated offline, and planned before it is applied. All examples use the fictional Example Pty Ltd.

What is configuration here, and what is not:

| Configuration in git, applied by `hound` | Stays in CISO Assistant |
| --- | --- |
| Custom risk matrices and threats (as libraries); risk studies and their scenarios, ratings, treatment and justification; security exceptions; BIAs, their asset assessments and escalation thresholds; systems' security and recovery objectives | Risk acceptance and exception approval (the Approver role and validation flows); computed risk levels; EBIOS RM and quantitative risk studies |

### s15-01 Risk matrices and threats from libraries
- [x] Done

**Story:** As a risk owner, I want the risk matrix and threat catalogue our studies use declared in the repo, so that every study is rated on the same reviewed scales.

**Note (from the s08-08 trial, 2026-11-22).** Loading the `essential-eight-2023` framework also loaded intuitem's Common Catalog (`urn:intuitem:risk:library:intuitem-common-catalog`): 28 threats and its reference controls, created in the same instant as the framework. 152 of the framework's 160 requirement nodes link to those threats (for example ISM-1807 to *Exploitation of Known Vulnerabilities*). So whether threats need adding by hand likely depends on the framework's bundle: a framework built on the Common Catalog brings its threats with it, while one that is not (or a custom threat) needs its library declared. Before building this task, confirm how CISO Assistant decides what comes with a library (its declared dependencies, which the stored-library API showed as empty here, or the objects the framework references), and check what NIST 800-171 and ASD Essentials bring. Declare only what does not arrive with a framework, and have the scope plan say what a framework brings with it, which today it does not.

**Acceptance criteria**
- `scope/setup.yaml` `frameworks` becomes `libraries`, accepting frameworks, risk matrices, threat catalogues and reference control catalogues. Built-in libraries are imported by URN; custom ones are library YAML files under `libraries/`, uploaded as in s06-09.
- `hound library validate` checks a custom risk matrix offline: URN of the form `urn:<packager>:risk:matrix:<id>`; probability, impact and risk levels numbered from 0 with no gaps; one grid row per probability level with one cell per impact level, each naming an existing risk level.
- A changed custom library whose `version` was not raised is an error (`library.version_not_bumped`), because CISO Assistant only re-publishes a library on a version bump.
- A framework snapshot (s06-01) is extended to matrices and threats, so studies are validated offline against level names and threat refs.

**Tests first**
- `RiskMatrixValidationTests.Levels_must_start_at_zero_without_gaps`
- `RiskMatrixValidationTests.Grid_cell_must_name_a_risk_level`
- `LibraryValidationTests.Changed_content_without_version_bump_is_reported`
- `LibrarySnapshotTests.Snapshot_lists_matrix_levels_and_threats`

**Functional check**
- A fictional 4×4 matrix and two fictional threats are uploaded to the local CISO Assistant and appear in its catalog; re-applying with no change writes nothing.

**What CISO Assistant brings with a library (checked 2026-11-25 against v4.3.4).** A library's own `dependencies` list decides it: CISO
Assistant loads each dependency with the library. `essential-eight-2023` declares `doc-pol` and `intuitem-common-catalog`, which is why the
Common Catalog's 28 threats arrived with it in the s08-08 trial. `asd-essentials-enterprise-it` declares none. Framework snapshots now keep
`dependencies`, and `hound scope plan` shows them as a `brings` line on the framework. CISO Assistant keeps the SHA-256 of an uploaded file
as the stored library's `hash_checksum`, and at the same version it only updates that hash and refuses the content, which is what
`library.version_not_bumped` guards against. A loaded library is updated with `GET loaded-libraries/<urn>/update/`.

**Decision.** `frameworks` stays as it is (each framework with its snapshot), and `libraries` now also takes a map with `urn`, `file` and
`snapshot` for a custom library. Renaming `frameworks` to `libraries` would break every existing repo for no gain.

**Functional check result**
```
$ hound library validate tests/fixtures/frameworks/example-risk-library.yaml
valid: 1 risk matrix, 2 threats
$ hound scope validate <repo>/scope    # setup lists the library file; a grid cell changed to 9
error: .../setup.yaml line 4: ../libraries/example-risk.yaml: risk matrix 'urn:example:risk:matrix:example-4x4': the cell for probability 'Likely' and impact 'Severe' is 9, which is not a risk level (0 to 2). [library.matrix_grid_unknown_risk]
```
Against CISO Assistant: `RiskLibraryFunctionalTests` (CI) uploads the 4x4 matrix and two threats, plans again with no change, is refused
with `library.version_not_bumped` after a change at the same version, then applies version 2 with a third threat. Result recorded below
once CI has run.

### s15-02 Risk register files
- [x] Done

**Story:** As a risk owner working with an assistant, I want each risk study and its scenarios in a file, so that ratings and treatments are proposed, reviewed and recorded like any other change.

**Acceptance criteria**
- `scope/risks/<study>.yaml` declares a risk study: `ref_id`, name, domain, optional perimeter, matrix (URN), status, authors, reviewers, ETA, due date.
- Each scenario has `ref_id`, name, description, systems (impacted assets), threats (by URN or `ref_id`), `existing_controls` and `planned_controls` (applied control `ref_id`s from `scope/controls.yaml`), current and residual probability and impact by the matrix's level names (inherent too when the `inherent_risk` flag is on), treatment (`open`, `mitigate`, `accept`, `avoid` or `transfer`) and justification.
- Validation: level names exist in the study's matrix; referenced systems, threats and controls exist; a planned control that is already `active` is reported; residual above current is reported (`risk.residual_above_current`), as CISO Assistant's own consistency check does; `--` means not rated.
- Risk levels are never written; CISO Assistant computes them from probability and impact.
- `scope plan` shows rating changes as old and new level names, so a reviewer sees "current: High → Medium" rather than numbers.

**Tests first**
- `RiskScopeTests.Unknown_level_name_is_reported_with_line`
- `RiskScopeTests.Residual_above_current_is_reported`
- `RiskScopeTests.Planned_control_already_active_is_reported`
- `ScopePlanTests.Rating_change_is_shown_as_level_names`
- Contract: `RiskClientTests.Creates_study_and_scenario_with_links` (recorded responses)

**Functional check**
- Apply a fictional study with three scenarios to the local CISO Assistant; the matrix view places them as expected; changing one rating in the file produces a one-line plan and a single update.

**Notes.** The risk client tests are in `ScopeClientTests` (`Creates_risk_study_on_its_matrix_with_actors_and_marker`,
`Creates_risk_scenario_with_links_and_ratings_by_position`), next to the other recorded writes, and the rating plan test is
`RiskPlanTests.Rating_change_is_shown_as_level_names`, because the plan's fields come from the parsed scope. A scenario's ratings are
planned as level names and written as their positions on the matrix (`ScopeObject.Levels`); CISO Assistant's list view gives each rating
back with its name, so a plan never needs the matrix to read what is held. The seven risk endpoints were added by hand to the OpenAPI
snapshot, as s12-04 did for the posture lists.

**Functional check result.** `RiskRegisterFunctionalTests` (CI) applies a three-scenario study to a copy of the example repo, checks
CISO Assistant places R-01 at High and R-03 at Medium on the 4x4 grid, then changes R-03's probability and expects one planned change
(`current_probability: Likely → Possible`) and one write. Result recorded below once CI has run.

### s15-03 Security exceptions and risk acceptances
- [ ] Done

**Story:** As an authorising official, I want exceptions and acceptances requested through the repo but approved only by an approver in CISO Assistant, so that the request is reviewed and versioned and the decision keeps its separation of duties.

**Acceptance criteria**
- `scope/exceptions.yaml` declares security exceptions: `ref_id`, name, description, domain, severity (`undefined`, `info`, `low`, `medium`, `high`, `critical`), owners, expiration date, observation, and links to systems, controls, scenarios and requirements (by audit and requirement).
- A risk scenario with treatment `accept` creates a risk acceptance request in CISO Assistant.
- `hound` never sets an approval status. A file that sets one is an error (`scope.approval_not_configurable`). `scope plan` shows each exception and acceptance with its CISO Assistant status (`draft`, `in_review`, `approved`, `rejected`, `resolved`, `expired`, `deprecated`).
- `scope plan` and `hound status` list exceptions expiring within 60 days, and expired exceptions still marked approved.
- When a check fails on a requirement covered by an approved, unexpired exception, the row's observation names the exception. The result itself is unchanged.

**Tests first**
- `ExceptionScopeTests.Approval_status_in_file_is_rejected`
- `ExceptionScopeTests.Expiring_within_60_days_is_listed`
- `ExceptionScopeTests.Expired_but_approved_is_reported`
- `PlanTests.Failed_check_under_approved_exception_names_it_in_observation`

**Functional check**
- Apply a fictional exception, approve it in the local CISO Assistant as an approver, and confirm the next run's observation names it and `scope plan` shows it approved.

**Notes.** Exceptions are read from `scope/exceptions.yaml` (`ScopeExceptionRequest`; CA1711 forbids type names ending in
"Exception", so the row-side record is `RowWaiver` and the client's is `RemoteWaiver`). A scenario treated by `accept` declares a
`RiskAcceptance` keyed by the scenario, written under the ref_id `ACC-<study>-<scenario>`. Neither is ever written with a status or
state. The reader takes an exception's controls and rows from their own `security_exceptions` lists, and observes status and expiry
(`ExceptionReview` turns them into the plan's notes, with a 60-day window). On the publish side the row list gives each row's exception
IDs and `ClientStateSource` names them from `security-exceptions/`; `AuditPlanner` appends the observation only to a failing result,
and adds the exception to the row fingerprint, so an approval or expiry rewrites the row once. `hound status --scope` lists expiring
exceptions offline from their declared expiry, since status reads no CISO Assistant. The test `Approval_status_in_file_is_rejected`
first expected an `approved` key on a scenario to be ignored; it is an unknown key like any other, so the test now says so.

**Functional check result.** `SecurityExceptionFunctionalTests` (CI) applies an exception and an accepted scenario to a copy of the
example repo, checks both are held as requests (draft, not accepted), approves the exception through the API as an approver would, and
expects the next plan to show it approved with no change to apply. The observation on a run is covered by
`PlanTests.Failed_check_under_approved_exception_names_it_in_observation` and `State_source_names_the_security_exceptions_on_each_row_and_the_day`.
Result recorded below once CI has run.

### s15-04 Business impact analysis files
- [ ] Done

**Story:** As a continuity owner, I want each BIA declared in the repo, so that recovery objectives and impact over time are reviewed and kept with the systems they describe.

**Acceptance criteria**
- Systems in `scope/setup.yaml` may declare security objectives and recovery objectives (`rto`, `rpo`, `mtd` as durations such as `4h` or `2d`).
- `scope/bia/<bia>.yaml` declares a BIA: `ref_id` (or name, if s06-05 found the API has no `ref_id`), perimeter, matrix, version, status, authors, reviewers, due date; one asset assessment per system with dependencies, associated controls, recovery flags (`documented`, `tested`, `targets_met`) and observation; and escalation thresholds per system with point in time (a duration), impact (a matrix impact level name), qualifications and justification.
- Validation: one assessment per system; thresholds per system are unique and in ascending time; impact names exist in the matrix; a threshold whose impact is lower than the one before is reported, because CISO Assistant carries impact forward.

**Tests first**
- `BiaScopeTests.Duplicate_system_is_reported`
- `BiaScopeTests.Thresholds_must_ascend_and_be_unique`
- `BiaScopeTests.Falling_impact_is_reported`
- `DurationTests.Parses_hours_days_and_weeks`
- Contract: `BiaClientTests.Creates_bia_asset_assessment_and_thresholds`

**Functional check**
- Apply a fictional BIA for two systems to the local CISO Assistant; its impact-over-time view matches the file.

**Notes.** CISO Assistant v4.3.4's BIA has a `ref_id` (`AutoRefIdMixin`), so the file's `ref_id` is the key. An asset assessment has no
`ref_id` or description: it is keyed `bia/system` and found by its asset; a threshold is keyed `bia/system/after` and found by its point in
time, held in seconds (`Duration` prints the largest whole unit, so `86400` reads `1d`). Impacts are planned as level names and written as
positions, as risk ratings are. A system's `objectives` and `recovery` are written to `security_objectives` and
`disaster_recovery_objectives` only when the file declares them (as labels are), so values set in the UI are kept; the reader reads only
enabled objectives. Qualifications are CISO Assistant terminologies, found by name. The client tests sit in `ScopeClientTests`
(`Creates_bia_asset_assessment_and_thresholds`, `System_sends_its_security_and_recovery_objectives_only_when_declared`).

**Functional check result.** `BiaFunctionalTests` (CI) applies a two-system BIA to a copy of the example repo, checks one asset
assessment per system and the thresholds at 4h Minor and 1d Major, then expects a second plan to change nothing. Result recorded below
once CI has run.

### s15-05 Recovery flags from checks
- [ ] Done

**Story:** As a continuity owner, I want "recovery tested" to come from evidence rather than a hand-ticked box, so that the BIA shows what was actually proven.

**Acceptance criteria**
- A BIA asset assessment may name checks for a recovery flag, such as `tested_by: [backup.restore_tested]`. Each run sets that flag from the checks (true only if every named check passes), links their evidence to the asset assessment, and writes only when the value changes (s06-04).
- A flag with checks named cannot also be set by hand in the file (`scope.result_conflict`).

**Tests first**
- `BiaPlanTests.Flag_follows_named_checks`
- `BiaPlanTests.Unchanged_flag_is_not_sent`
- `BiaScopeTests.Hand_set_flag_with_checks_conflicts`

**Functional check**
- With the fake collector reporting `backup.restore_tested` as pass, then fail: the flag in the local CISO Assistant follows, with evidence linked.

**Notes.** `documented_by`, `tested_by` and `targets_met_by` name checks by id. `RecoveryFlags.Plan` (pure) compares each such flag with
the asset assessment CISO Assistant holds; `RecoveryPublisher` runs after the collectors in `hound run` (REST only, not in a dry run),
reads every collector's latest results (s14-05), sets each changed flag and links the evidence CISO Assistant already holds. The scope
plan treats recovery flags as optional fields, so it neither resets a flag set by checks nor reports it as edited, and apply leaves an
undeclared flag out of its update. The fake collector gained a `fail` setting (check ids to report as failed) for the functional check.

**Functional check result.** `BiaFunctionalTests.A_recovery_flag_named_by_a_check_follows_it_from_pass_to_fail_with_its_evidence` (CI)
applies a BIA with `tested_by: [backup.restore_tested]`, runs the fake collector (flag true, evidence linked), runs it with
`fail: backup.restore_tested` (flag false), runs again (nothing set) and plans the scope (no change to the assessment). Result: pending
the CI run.

### s15-06 Documentation and example repo
- [ ] Done

**Acceptance criteria**
- `docs/risk.md` and `docs/bia.md`: file formats, validation, plan and apply, what stays in CISO Assistant (approvals, computed levels) and why.
- The example compliance repo (s06-12) gains a fictional matrix, threat catalogue, risk study, exception and BIA, all of which validate.

**Tests first**
- `ExampleRepoTests.Example_compliance_repo_validates` covers the new files.
- Docs lint and link check.

**Functional check**
- Following the guides verbatim, add a scenario to the example study and apply it.

### s15-07 Incident response and reporting deadlines (added 2026-11-24)
- [ ] Done

Added at the maintainer's request (2026-11-24): both regimes require incidents to be reported on a clock, and incident response had no
home in the plan.

**Story:** As a small supplier, I want an incident recorded once with the reports it triggers and their deadlines, so that I meet each
regulator's and customer's clock without knowing them by heart.

**Acceptance criteria**
- An incident response plan and its annual test are manual sources with runbooks and an evidence plan row (s10-04).
- Incidents live in CISO Assistant (its incident records). `scope/reporting.yaml` declares the reporting obligations as configuration:
  who, within how long, triggered by which data kinds (s10-06) or contract (s15-08). Examples: DFARS 252.204-7012 (72 hours to the
  DoD for covered defence information), DISP (to Defence), the Notifiable Data Breaches scheme (assess within 30 days), ASD's ACSC
  reporting, and the customer's own contract clauses.
- `hound incident obligations <incident>` lists each report owed with its deadline from the incident's discovery time; overdue reports
  are open actions in the committee report (s12-05) and items in the weekly review (s10-07).

**Tests first**
- `ReportingObligationTests.Cui_incident_owes_a_72_hour_dfars_report`
- `ReportingObligationTests.Pii_breach_owes_an_ndb_assessment_within_30_days`
- `IncidentObligationsCommandTests.Lists_deadlines_from_discovery_time`

**Functional check**
- Record a fictional incident on a CUI system in the local CISO Assistant; `hound incident obligations` lists the DFARS report due
  72 hours after discovery.

**Notes.** `scope/reporting.yaml` declares the obligations (id, name, report_to, within, when {data, always}, reference), checked by
`hound scope validate` and the scope schema. `ReportingObligations.Owed` (pure) gives each report owed, due from discovery: the first
*detection* timeline entry, else when the incident was reported. A timeline entry `Reported: <id>, ...` marks a report as made.
`hound incident obligations <incident>` (text or JSON) reads the incident from CISO Assistant (`IIncidentSource`, read only). The posture
snapshot records each incident's reports; the committee report lists overdue ones in section 6 and proposes actions in section 8; the
weekly review lists each report an open incident still owes when CISO Assistant is configured. The example repo has `reporting.yaml`
(DFARS 252.204-7012, NDB assessment, ReportCyber), the control `AC-INCIDENT-RESPONSE`, and the plan's yearly test as the manual source
`example-incident-response-plan` with its runbook and evidence plan row. Guide: docs/incidents.md.

**Functional check result.** `IncidentFunctionalTests` (CI) records `INC-FUNC-01` on Example laptops (CUI) with a detection entry and
checks that `hound incident obligations` lists `dfars-7012` due 72 hours later. Result recorded once CI has run.

### s15-08 Supply-chain flow-down (added 2026-11-24)
- [x] Done

Added at the maintainer's request (2026-11-24): primes push requirements to their subcontractors, and a supplier must pass them on to its
own.

**Story:** As a subcontractor, I want each customer contract's security clauses recorded with the requirements they impose, and checked
against my own suppliers' contracts, so that I can show a prime what I flow down and see where I do not.

**Acceptance criteria**
- A customer contract (from a prime) is declared like a supplier contract (s09-19), with `obligations`: clause references (for example
  DFARS 252.204-7012, DFARS 252.204-7021, a DISP clause, an Essential Eight maturity level) and the framework and level each imposes.
- `flow_down: true` on an obligation means it must appear on every supplier contract whose solution handles the related data kind
  (s10-06). Validation reports a supplier contract that is missing it (`contract.flow_down_missing`).
- The obligations a customer imposes become the programme's targets (s11-01), so reports say which customer requires what.
- `hound supplier list --data` (s10-06) gains the flowed-down clauses on each supplier, for the prime's questionnaire.

**Tests first**
- `FlowDownTests.Supplier_contract_without_a_flowed_down_clause_is_reported`
- `FlowDownTests.Customer_obligation_names_a_framework_and_level`

**Functional check**
- The machine-shop rehearsal records its prime's DFARS clauses and finds the one supplier contract that does not carry them.

**Notes**
- A customer is a third party whose every contract has `customer: true`, with the `CUS-` prefix. `obligations` name a clause, a
  framework and level, the data kinds it covers and `flow_down`. `contract.flow_down_missing` is a warning, like the sensitive-data
  findings (s10-05): recording the truth must not lock the repo, and it joins the weekly review. `contract.framework_unknown` and
  `contract.flow_down_invalid` are errors (s15-D9 made the first a warning).
- `hound programme list` adds "Required by <customer> (<contract>): <clause>, level N." under each programme with that framework
  (`required_by` in JSON). `hound supplier list --data` adds a "Flowed-down clauses" column (`clauses` in JSON), carried or missing.

**Functional check (2026-11-25)**
```
$ hound scope validate scope     # example repo + CUS-EXAMPLE-PRIME (DFARS 252.204-7012, flow_down, cui) + SUP-EXCLOUD storing cui
warning: scope/third-parties/sup-excloud.yaml line 15: contract 'CTR-EXCLOUD-2026' with SUP-EXCLOUD lacks 'DFARS 252.204-7012', which customer contract 'CTR-PRIME-PARTS' flows down. [contract.flow_down_missing]
scope valid: 1 domain(s), 6 control(s), 5 audit(s), 2 third party(ies), 1 risk study(ies) with 2 scenario(s)
$ hound supplier list --data cui --repo .
| SUP-EXCLOUD | SOL-EXCLOUD-HOST | stores |  | FedRAMP Moderate equivalent |  | 2027-06-30 |  | DFARS 252.204-7012 (missing) |
$ hound programme list --scope scope
Required by Example Prime Pty Ltd (CTR-PRIME-PARTS): Essential Eight maturity level 2, level 2.
```

### s15-09 Incident response plan document (added 2026-11-24)
- [x] Done

Added at the maintainer's request (2026-11-24): both regimes require a written incident response plan (ISM-0043; NIST SP 800-171
family 3.6, with DFARS 252.204-7012's 72-hour report for CMMC Level 2), and a small team should not have to write one from a blank page.

**Story:** As a GRC team member with a few hours a week, I want `hound` to build the incident response plan from what CISO Assistant
and the configuration repo already hold, so that I write only the judgement parts and the plan stays current when contacts, systems or
suppliers change.

**Acceptance criteria**
- Same document approach as the SSP (s11-02, s11-04): `scope/irp.yaml` names the programme, the roles (incident manager, deputy,
  executive contact, legal or privacy contact) and the judgement section files; `hound irp build --programme <id>` writes the plan as
  Markdown and JSON, every derived value with its source.
- Derived, not typed: contacts from the roles' holders, the systems in scope with their data kinds (s10-06), suppliers and their incident
  contacts and contract clauses (s09-19, s15-08), and the reporting obligations with their deadlines from `scope/reporting.yaml`
  (s15-07), for example DFARS 7012 within 72 hours, Defence for DISP, and the Notifiable Data Breaches assessment within 30 days.
- Judgement sections the person writes: severity definitions, escalation decisions, containment priorities, communication approach and
  lessons learned. A missing section or role is a gap, reported like the SSP's gaps; `--strict` exits non-zero on gaps.
- Structure (maintainer's decision, 2026-11-24): NIST first. The plan follows NIST SP 800-61 Rev 3 (2025, aligned to CSF 2.0: govern,
  identify, protect, detect, respond, recover), as the SSP follows SP 800-18. The Australian specifics come from ASD's ACSC Cyber
  Incident Response Plan Guidance (https://www.cyber.gov.au/sites/default/files/2023-03/ACSC%20Cyber%20Incident%20Response%20Plan%20Guidance_A4.pdf)
  and sit inside that structure, mainly as reporting obligations and contacts: ReportCyber to ASD, the Notifiable Data Breaches scheme,
  DISP reporting to Defence, and DFARS 252.204-7012's 72 hours for covered defence information. One layout, not a separate ACSC one.
- The plan cites ISM-0043, NIST SP 800-61 Rev 3, the ACSC guidance and NIST SP 800-171 3.6.1 to 3.6.3, and its annual test (s15-07's
  manual source) records the plan version it tested.
- Covers ISM-0043's eight items (ISM September 2026, ISM-0043 revision 6), each as a section a test checks for: what constitutes a
  cyber security incident; the types likely to be encountered and the expected response to each; how to report internally and
  externally to relevant authorities (from `scope/reporting.yaml`); other parties to inform (suppliers and customers, s15-08); the
  authorities responsible for investigating and responding; the criteria for requesting an investigation from a law enforcement agency,
  ASD or another authority; the steps that keep evidence intact; and system contingency measures, or a reference to the business
  continuity summary (s19-06).
- It is system-specific documentation, so the system's authorising officer approves it (ISM-0047).

**Tests first**
- `IrpBuilderTests.Contacts_come_from_role_holders_with_sources`
- `IrpBuilderTests.Reporting_obligations_list_each_deadline`
- `IrpValidatorTests.Missing_judgement_section_is_a_gap`
- `IrpLayoutTests.Covers_every_ism_0043_item`
- `IrpLayoutTests.Sections_follow_sp_800_61r3_with_australian_reporting`
- `IrpCommandTests.Strict_build_with_gaps_exits_non_zero`

**Functional check**
- Build the example programme's plan against the local CISO Assistant; it lists the fictional CUI system's DFARS 72-hour report and the
  supplier contacts, and reports no gaps once the example judgement sections are present.

**Notes**
- `scope/irp.yaml` names the programme, version, five roles (incident manager, deputy, executive, legal and privacy, and the
  authorising officer for ISM-0047) and five judgement sections. Everything else is derived from the repo with its file and line:
  systems with their data and recovery objectives, suppliers and customers with contacts and clauses, and each report with its
  deadline in words ("72 hours") and what triggers it in this scope. It reads no CISO Assistant: every value it uses is in the repo
  that apply writes to CISO Assistant.
- The example repo's plan and sections are fictional; the section files are excluded from markdownlint like the SSP's.

**Functional check (2026-11-25)**
```
$ hound irp build --programme essential-eight --scope scope --strict   # example repo, Example laptops processing cui
exit 0
## 1. Govern ... ## 2. Identify ... ## 3. Protect ... ## 4. Detect ... ## 5. Respond ... ## 6. Recover ... ## References
### ... (ISM-0043 item 1) to (ISM-0043 item 8)
| DFARS 252.204-7012 cyber incident report | DoD, through the DIBNet portal (needs a medium assurance certificate) | 72 hours | an incident on a system handling cui (Example laptops) | DFARS 252.204-7012(c) |
```
The supplier contacts are covered by `IrpBuilderTests` (the example repo declares no third party).

### s15-10 Rehearse trial, onboarding and sustaining GRC (standing, every sprint)
- [ ] Done

As [s12-09](s12-e8-mapping-and-adoption.md#s12-09-rehearse-trial-onboarding-and-sustaining-grc-standing-every-sprint), including this sprint's new commands and guides.
From s15 the rehearsal drives hound through the agent skills, as the person's agent would, and records any skill that was wrong or
missing (the maintainer's standing rule, 2026-11-25: every sprint ends with the Claude Code and Copilot skills and agents accurate for
the CLI and its workflows; see the sprint lifecycle in [README.md](README.md)).

### s15-11 Improve: quality and flow (standing, about 15% of each sprint)
- [ ] Done

As [s12-10](s12-e8-mapping-and-adoption.md#s12-10-improve-quality-and-flow-standing-about-15-of-each-sprint).

### s15-12 Approve one decision at a time, with the person's agent (added 2026-11-25)
- [ ] Done

Added at the maintainer's request (2026-11-25), after a first real `hound approve`: the terminal loop asked about each item with no
help, so 240 not-applicable rows and a typo went through. The maintainer's direction: every decision waiting for a person is written
out first; their own agent (Claude Code or GitHub Copilot) works through them with them one at a time, researching and checking around
the edges; the person then approves or challenges each with a command against a stable id. A person still approves at a terminal: no
agent can.

**Story:** As a GRC member with a few hours a week, I want to see every pending decision grouped, with the agent's case, its sources
and a risk flag, and to approve or challenge one at a time by id, so that I decide with help and nothing slips through in a long loop.

**Acceptance criteria**
- Builds on the weekly review (s10-05), which already has the pieces: `hound review pending --json` lists every waiting item by a stable
  key, `hound review recommend <key>` records the agent's case and sources, and `hound review` is the person's session at a terminal.
- `hound review pending --json` adds a risk flag to each item (`high` for a not-applicable decision, a supplier approval, a finding, or
  an item with no recommendation or one without a source; else `normal`) and a summary grouped by kind and recommendation with counts
  and minutes, so the agent opens with the size of the job.
- `hound review --only <key>` (repeatable) runs the session for just those items: the person reads the item with the agent's case and
  answers accept, challenge or skip. Nothing else is asked or written. An unknown key is refused with the nearest keys. It needs a
  person at a terminal, as now: an agent or a pipe is refused.
- `hound approve` opens with the grouped summary and points to `hound review` with an agent for long lists.
- The onboarding and review guides describe the flow with an agent: list, discuss, then the person accepts or challenges by key.

**Tests first**
- `ReviewCommandTests.Pending_flags_risk_and_groups_a_summary`
- `ReviewCommandTests.Only_the_named_item_is_asked_and_the_rest_wait`
- `ReviewCommandTests.An_unknown_key_is_refused_with_the_nearest`
- `ReviewCommandTests.Refuses_without_a_terminal` (extended to `--only`)

**Functional check**
- On a copy of the example repo with a drafted onboarding: the agent lists the items, records a recommendation, and the person
  accepts one item and challenges another by key; the sign-off records only those.

### s15-13 Skills and agents for every hound workflow, for Claude Code and GitHub Copilot (added 2026-11-25)
- [ ] Done

Added at the maintainer's request (2026-11-25): "I need skills or agents written for both github copilot and claude that supports all
workflows and understand the hounds commands", now rather than in s18. A gap check of what `hound agent setup` wrote found no skill,
agent or instructions for collect, collectors, config, status, publish, serve, ssp, incident, irp, programme, framework, library or
skill. s18-08 then extends the kit only for what s18 adds.

**Story:** As a GRC member who leans on my coding assistant, whichever one I use, I want it to know every hound command and to have a
skill for every job hound does, so that it can run the job with me and never invents a command.

**Acceptance criteria**
- `hound agent setup` writes a command reference skill, `hound-commands`, built from the CLI's own command tree when it runs (every
  command and sub-command with its description and options), so it cannot fall behind. Every agent loads it, and the instructions
  point to it.
- New skills, in the one source (`agent/skills/`) and so for both assistants: `work-through-pending-decisions` (lists by key with risk
  first, researches each item, records its case with `hound review recommend`, and gives the person the one `hound review --only <key>`
  to run, never running it), `operate-collectors` (config validate, collectors list, add collector, collect, plan, publish, run,
  serve, status, and reading a collector's errors), `build-the-ssp` (programme list, ssp and mapping coverage and crosswalk),
  `respond-to-an-incident` (incident obligations and irp build) and `keep-frameworks-and-libraries` (framework and library
  snapshots, mapping derive and validate, profile validate, test and diff, and the organisation's own skills).
- Two more agents: `hound-decisions` (walks the person through pending decisions; changes no scope file) and `hound-operations`
  (collectors, runs, the SSP, incidents, frameworks and libraries). The onboarding and review agents hand pending decisions to
  `hound-decisions`.
- Every top-level command is named by at least one skill, agent or the instructions, and every `hound ...` command any of them names
  exists in the CLI. Both checks run on what is written for Claude Code and for Copilot.

**Tests first**
- `AgentCommandTests.The_reference_names_every_command_for_both_assistants`
- `AgentCommandTests.Every_top_level_command_has_a_skill`
- `AgentCommandTests.Every_command_a_skill_names_exists`
- `AgentCommandTests.Every_agent_loads_the_command_reference`
- `AgentSkillTests.Decisions_skill_recommends_and_never_accepts`

**Functional check**
- `hound agent setup --assistant both` on a copy of the example repo; count the skills and agents written and check the reference
  against `hound --help`.

## Discovered work

- **s15-D1 First apply on an empty CISO Assistant failed to create systems whose owner the same apply creates** (found
  2026-11-25 rehearsing `hound init`, `hound add system` and `hound scope apply`). The plan ordered systems before users, so a
  system owned by a user from `scope/access.yaml` was refused (owner not found) and only a second apply created it. Fixed: users
  and teams are planned before systems (test, then fix). `hound add system --parents` takes system names, not ref IDs; the guided
  setup page now says so.
- **s15-D2 An audit's systems were never written to CISO Assistant** (found 2026-11-25 in the rehearsal). The scope plan did not
  compare an audit's `systems`, and the audit's assets in CISO Assistant stayed empty. Fixed: the plan compares them, apply writes
  them as the audit's assets, and systems added in the UI are kept until the scope names some (test, then fix).
- **s15-D3 A collector whose every check errored reported success** (found 2026-11-25 in the rehearsal: refused credentials gave
  "succeeded" and `hound status` showed 0 passed, 0 failed with no warning). Fixed: such a run fails with
  `collector.all_checks_errored`, quoting the first error, and `hound status` lists each failed collector's problems below the table.
- **s15-D4 `config validate --check-secrets` accepted an empty secret file** (found 2026-11-25 in the rehearsal). Fixed: an empty or
  blank secret file is missing (`secret.missing`, "the file is empty").
- **s15-D5 Retry attempts flooded stderr** (found 2026-11-25 in the rehearsal: an information line per request on every scope plan
  and apply). Fixed: retry logging starts at warnings, like the HTTP client's own.
- **s15-D6 The trial's dev server is unreachable on a `make ca-up` network** (found 2026-11-25 in the rehearsal). The trial and
  local-trial.md point the REST collector at `http://host.docker.internal:5370/`, but credentials go over plain http only to
  loopback, so every request fails with `rest.host_not_allowed`. Fixed (the maintainer chose to keep the rule): dev servers are
  always on localhost, and while the config reads one the trial runs hound on the host network, reaching a `make ca-up` stack on its
  published port, as the Microsoft 365 step already did. local-trial.md and sophos-dev-server.md say so.
- **s15-D7 `hound approve` wrote an audit domain and perimeter it never checked** (found 2026-11-25 in a person's first approve).
  Asked for the audit's domain, the person typed "y", taking it for a yes/no question; approve wrote `domain: "y"` and an empty
  perimeter, and `scope validate` then failed with `scope.value_invalid`. The audit's systems were also not linked in CISO
  Assistant, which s15-D2 fixed after that release. Fixed: the prompt lists the domains and perimeters setup.yaml declares
  ("Domain for the system's audit, one of: Example Pty Ltd [Example Pty Ltd]"), Enter takes the first, and any other answer is asked
  again with the reason; with none declared, approve stops with `onboard.place_missing` and writes nothing (test, then fix).

- **s15-D8 The organisation's audit of record can be an empty shell, its name is opaque, and onboarding steers to blanket
  attestations** (found 2026-11-25 in a person's own setup). `hound init` always creates the organisation-wide audit (such as
  `EX-E8-Q2-2027FY`); with no organisation-wide collector feeding it and only per-system audits doing the work, the person could not
  tell what it was for and removed it. `Q2-2027FY` for October to December 2026 needs explaining. The manual sources onboarding drafted
  were one `evidence_attested` check per strategy, too coarse to show what was checked. Maintainer's direction (2026-11-25): audits are
  per system, so gaps and coverage stay visible; a shared platform gets its own audit that the systems on it rely on; duplicate work is
  cut by shared, specific evidence sources feeding many audits, not by merging audits. Triage: s15-11 puts this in the onboarding and
  scope guides, explains the period name where `hound init` and `hound audit` print it, and steers onboarding to the per-check
  manual-source templates; making per-system and platform audits the onboarding default, with an organisation audit only when an
  organisation-wide source feeds it, goes to s18 (fewer steps for the agent).
- **s15-D9 Copying the guide's customer contract locked the example repo** (found 2026-11-25 rehearsing docs/scope.md). The
  example names NIST SP 800-171, which the example repo does not load, so `contract.framework_unknown` failed validation: recording a
  prime's true requirement stopped every other command. Fixed: it is a warning that names the gap (no programme tracks the framework
  yet) and joins the weekly review (test, then fix).

## Sprint review
