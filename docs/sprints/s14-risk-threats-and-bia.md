# s14 Risk, threats and business impact analysis as configuration

Status: Planned

Release: 2

Was s09 before the release 1 reorder.

**Goal:** the risk register, its threats and risk matrices, security exceptions and business impact analyses live in the private configuration repo as YAML, reviewed through pull requests and applied to CISO Assistant by `hound`. Approvals stay with people in CISO Assistant. Checks feed what they can prove, such as whether recovery was tested.

Builds on s06: scope files, `scope validate`, `scope plan` and `scope apply`, controls declared once, and the configuration inventory (s06-05). Every object here is matched by `ref_id` (or URN for library objects), validated offline, and planned before it is applied. All examples use the fictional Example Pty Ltd.

What is configuration here, and what is not:

| Configuration in git, applied by `hound` | Stays in CISO Assistant |
| --- | --- |
| Custom risk matrices and threats (as libraries); risk studies and their scenarios, ratings, treatment and justification; security exceptions; BIAs, their asset assessments and escalation thresholds; systems' security and recovery objectives | Risk acceptance and exception approval (the Approver role and validation flows); computed risk levels; EBIOS RM and quantitative risk studies |

### s14-01 Risk matrices and threats from libraries
- [ ] Done

**Story:** As a risk owner, I want the risk matrix and threat catalogue our studies use declared in the repo, so that every study is rated on the same reviewed scales.

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

### s14-02 Risk register files
- [ ] Done

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

### s14-03 Security exceptions and risk acceptances
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

### s14-04 Business impact analysis files
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

### s14-05 Recovery flags from checks
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

### s14-06 Documentation and example repo
- [ ] Done

**Acceptance criteria**
- `docs/risk.md` and `docs/bia.md`: file formats, validation, plan and apply, what stays in CISO Assistant (approvals, computed levels) and why.
- The example compliance repo (s06-12) gains a fictional matrix, threat catalogue, risk study, exception and BIA, all of which validate.

**Tests first**
- `ExampleRepoTests.Example_compliance_repo_validates` covers the new files.
- Docs lint and link check.

**Functional check**
- Following the guides verbatim, add a scenario to the example study and apply it.

## Discovered work

## Sprint review
