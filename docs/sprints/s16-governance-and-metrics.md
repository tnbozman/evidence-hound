# s16 Governance collector and metrics

Status: In progress

Release: 2

Was s10-03 to s10-06 before the release 1 reorder. Third parties stayed in release 1 as [s10](s10-third-parties.md).

**Goal:** `hound` reads CISO Assistant's own risk and recovery records through the `ciso-assistant` profile (started in s11), and metric definitions and instances live in the private configuration repo, with samples written from runs, so supplier assurance, risk management and trends are evidenced rather than asserted.

Builds on s06 (scope files, plan, apply, inventory), s10 (suppliers), s14 (the Essentials mapping) and s15 (risk studies, exceptions and BIAs). Everything is matched by `ref_id`, or by the key CISO Assistant uses where it has no `ref_id`. All examples use the fictional Example Pty Ltd and `example.com`.

| Configuration in git, applied by `hound` | Stays in CISO Assistant |
| --- | --- |
| Metric definitions, metric instances, dashboards | Manually entered metric samples |

### s16-01 Risk and recovery checks in the `ciso-assistant` profile
- [ ] Done

Changed in the AI governance planning PR (Nov 2026): s11-05 builds the `ciso-assistant` REST collector profile and its supplier checks (`supplier.assessments_current`, `supplier.contracts_current`), so this task extends that profile instead of building a coded collector.

**Story:** As a compliance owner, I want risk and recovery records already in CISO Assistant turned into checks, so that requirements about risk management and recovery are evidenced automatically rather than by manual uploads.

**Acceptance criteria**
- The `ciso-assistant` profile (s11-05) gains, with the records it read as evidence:
  - `risk.studies_reviewed`: every risk study was updated within a configured interval.
  - `risk.exceptions_current`: no approved exception is past its expiry date.
  - `bia.recovery_tested`: every BIA asset assessment for a critical system has recovery tested.
- It passes the collector contract suite (s04) and the profile tests (s09-12) with recorded responses.
- These checks and the supplier checks from s11-05 are added to the Essentials mapping (s14) where they evidence actions with no Essential Eight source (supplier assurance GOV-11, supply chain PRO-16, risk management GOV-03), reducing the manual-only list from the migration plan.

**Tests first**
- `CisoAssistantProfileTests.Risk_study_past_interval_fails`
- `CisoAssistantProfileTests.Expired_approved_exception_fails`
- `CisoAssistantProfileTests.Critical_bia_without_recovery_test_fails`

**Functional check**
- Against the local CISO Assistant with the example suppliers, study and BIA, `hound run` produces the supplier, risk and BIA checks with the expected outcomes and publishes them.

**Notes**
- Tests are YAML cases in `profiles/ciso-assistant.tests/` (run by `LibraryProfileTests` with the unit suite): `risk-study-past-interval`, `expired-approved-exception` and `critical-bia-without-recovery-test`, with `all-pass` extended.
- A critical system is one with a recovery time objective (`recovery: {rto: ...}`), so the check needs nothing new in the scope. `risk.studies_reviewed` skips deprecated studies and fails when there is no study at all; the interval is the `risk_review_interval` setting (default a year).
- Mapped: GOV-03.1 gains both risk checks, PRO-16.1 (both domains) gains `supplier.assessments_current`, PRO-10.3 (both domains) gains `bia.recovery_tested`. GOV-11 already had the supplier checks from s14. The migration guide says which manual-only actions are now partly automated.
- Functional: `RiskRecoveryChecksFunctionalTests` runs the profile against the seeded CISO Assistant and works out each expected outcome from CISO Assistant's own lists.

### s16-02 Metric definitions and instances
- [ ] Done

**Story:** As the capability owner, I want the metrics we track declared in the repo, so that what we measure, its targets and owners are reviewed and the same everywhere.

**Acceptance criteria**
- `scope/metrics.yaml` declares metric definitions (`ref_id` or library URN, name, quantitative with unit or qualitative with ordered levels worst first, direction `higher is better`, `lower is better` or none, decimals, and for computed metrics a formula over named inputs) and metric instances (definition, domain, collection frequency, grace period, target value, assigned to, linked applied controls).
- Validation: a formula names only declared instances, each in the computed instance's domain or below it; levels are ordered; a target is within the definition's levels or units.
- Dashboards and widgets may be declared (kind, title, the instance or builtin data each reads); applying creates or updates them.

**Tests first**
- `MetricScopeTests.Formula_input_must_be_declared_in_domain`
- `MetricScopeTests.Levels_must_be_ordered`
- Contract: `MetricClientTests.Creates_definition_instance_and_dashboard`

**Functional check**
- Apply a fictional "MFA coverage" metric with a 95 % target and a dashboard widget to the local CISO Assistant.

**Notes**
- `scope/metrics.yaml` is read by `ScopeLoader.Metrics`, checked by `MetricValidator`, compared by `MetricObjects` and written by `ScopeWriter.Metrics` to CISO Assistant v4.3.4's metrology API. Four new plan kinds: `metric_definition`, `metric_instance`, `dashboard` (marked by state, as other `ref_id` kinds) and `dashboard_widget` (keyed `dashboard/title`, compared field by field).
- A qualitative target is written as its level's position counted from 1, as CISO Assistant stores it, and read back as the level name. A unit is CISO Assistant's terminology with `field_path` `metric_definition.unit`; another unit is a warning, not an error, because an organisation can add one in CISO Assistant.
- "In the computed instance's domain or below it" is read as: an instance in the root folder may read any domain; an instance in a domain reads only that domain, as CISO Assistant's formula engine does.
- An instance with no `frequency` or `target` takes its definition's, so the plan compares what CISO Assistant copies.
- Functional: `MetricsFunctionalTests` applies an MFA coverage formula with a 95 target, a qualitative policy metric and a dashboard with metric, builtin and text widgets, then checks a second plan is empty and a new target is one update.

### s16-03 Metric samples from runs
- [ ] Done

**Story:** As the security committee, I want metrics filled in from collection runs, so that trends are measured every period without anyone typing numbers.

**Acceptance criteria**
- A metric instance may declare `source: hound` with a measure: `check_pass_rate` (over check IDs or a pattern such as `identity.mfa.*`), `requirements_compliant_pct` (for an audit), `evidence_freshness_pct`, or `collector_success_rate`.
- Each run computes the measure and writes one sample for the instance's current period. CISO Assistant keeps one value per period, so a later run in the same period replaces it; an unchanged value is not sent (s06-04).
- An instance with `source: hound` cannot also be fed by hand (`scope.result_conflict`).

**Tests first**
- `MetricMeasureTests.Check_pass_rate_over_pattern`
- `MetricMeasureTests.Requirements_compliant_pct_ignores_not_applicable`
- `MetricPlanTests.Same_period_value_replaces_and_unchanged_is_not_sent`

**Functional check**
- Two runs on different days of one month against the local CISO Assistant leave one sample for that month, holding the second run's value.

**Notes**
- Samples go to CISO Assistant v4.3.4's `custom-metric-samples` with `period_start`: CISO Assistant keeps one sample per instance and period and a later write for the period replaces it, so hound reads the instance's period samples and sends only a changed value (to two decimals). Periods are counted as CISO Assistant counts them (`MetricSamples.PeriodStart`, UTC, ISO weeks).
- "Cannot also be fed by hand" is read as: an instance whose definition has a formula is filled in by CISO Assistant, which refuses typed values, so `source: hound` there is `scope.result_conflict`. A person can still type a value for a run-fed instance's period in CISO Assistant; the next run whose value differs replaces it.
- `collector_success_rate` counts the collectors the run ran (all of them unless `--only`). `evidence_freshness_pct` counts a result fresh for 7 days, the committee report's stale window.
- `HOUND_TODAY` moves the measuring time to that day (never later than now), so the functional test runs twice in last month.
- Functional: `MetricsFunctionalTests.Two_runs_in_one_month_leave_one_sample_holding_the_second_value` measures `backup.*` with the fake collector, passing then failing one check.

### s16-04 Documentation and example repo
- [ ] Done

**Acceptance criteria**
- `docs/metrics.md` and a governance collector section in `docs/collectors/`: file formats, the governance collector checks, metric measures, and what stays in CISO Assistant.
- The example compliance repo gains two metrics and a dashboard, all of which validate.

**Tests first**
- `ExampleRepoTests.Example_compliance_repo_validates` covers the new files.
- Docs lint and link check.

**Functional check**
- Following the guides verbatim, add a metric to the example repo and apply it.

### s16-07 `hound status` gives each programme's standing (carried from s15-D10)
- [ ] Done

**Story:** As a GRC member with a few hours a week, I want `hound status` to say how much of each programme is assessed and met, so
that I see the real position without opening a report or trusting CISO Assistant's percentage.

**Acceptance criteria**
- With `--scope`, `hound status` gives each programme's standing from the latest posture snapshot (s15-14's level met, compliant of
  applicable with not-assessed rows as not met, rounded down, and "not assessed: N"), and says when there is no snapshot yet and how
  to make one (`hound report snapshot`).
- `--json` carries the same figures.

**Tests first**
- `StatusCommandTests.Gives_each_programmes_standing_from_the_latest_snapshot`
- `StatusCommandTests.Says_how_to_make_a_snapshot_when_there_is_none`

**Functional check**
- After the trial's runs and a snapshot, `hound status --scope scope/` gives the Essential Eight standing with the open rows counted.

### s16-05 Rehearse trial, onboarding and sustaining GRC (standing, every sprint)
- [ ] Done

As [s12-09](s12-e8-mapping-and-adoption.md#s12-09-rehearse-trial-onboarding-and-sustaining-grc-standing-every-sprint), including this sprint's new commands and guides.
The rehearsal drives hound through the agent skills, and the sprint ends with the Claude Code and Copilot skills and agents
accurate for the CLI and its workflows (sprint lifecycle step 5 in [README.md](README.md)).

### s16-06 Improve: quality and flow (standing, about 15% of each sprint)
- [ ] Done

As [s12-10](s12-e8-mapping-and-adoption.md#s12-10-improve-quality-and-flow-standing-about-15-of-each-sprint).

## Discovered work

## Sprint review
