# s16 Governance collector and metrics

Status: Planned

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

## Discovered work

## Sprint review
