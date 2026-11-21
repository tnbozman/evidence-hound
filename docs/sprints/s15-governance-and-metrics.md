# s15 Governance collector and metrics

Status: Planned

Release: 2

Was s10-03 to s10-06 before the release 1 reorder. Third parties stayed in release 1 as [s10](s10-third-parties.md).

**Goal:** `hound` reads CISO Assistant's own governance records as a collector, and metric definitions and instances live in the private configuration repo, with samples written from runs, so supplier assurance, risk management and trends are evidenced rather than asserted.

Builds on s06 (scope files, plan, apply, inventory), s10 (suppliers), s13 (the Essentials mapping) and s14 (risk studies, exceptions and BIAs). Everything is matched by `ref_id`, or by the key CISO Assistant uses where it has no `ref_id`. All examples use the fictional Example Pty Ltd and `example.com`.

| Configuration in git, applied by `hound` | Stays in CISO Assistant |
| --- | --- |
| Metric definitions, metric instances, dashboards | Manually entered metric samples |

### s15-01 CISO Assistant governance collector
- [ ] Done

**Story:** As a compliance owner, I want governance records already in CISO Assistant turned into checks, so that requirements about supplier assurance, risk management and recovery are evidenced automatically rather than by manual uploads.

**Acceptance criteria**
- A built-in collector type `ciso-assistant` reads CISO Assistant with a read-only token and emits framework-neutral checks, with the records it read as evidence:
  - `supplier.assessments_current`: every supplier at or above a configured tier has an assessment completed within its review cycle.
  - `supplier.contracts_current`: no active contract is past its end date.
  - `risk.studies_reviewed`: every risk study was updated within a configured interval.
  - `risk.exceptions_current`: no approved exception is past its expiry date.
  - `bia.recovery_tested`: every BIA asset assessment for a critical system has recovery tested.
- It passes the collector contract suite (s04) with recorded responses.
- The checks are added to the Essentials mapping (s13) where they evidence actions with no Essential Eight source (supplier assurance GOV-11, supply chain PRO-16, risk management GOV-03), reducing the manual-only list from the migration plan.

**Tests first**
- `GovernanceCollectorContractTests` (the s04 contract suite)
- `GovernanceChecksTests.Supplier_past_review_cycle_fails`
- `GovernanceChecksTests.Tier_below_threshold_is_not_required`
- `GovernanceChecksTests.Expired_approved_exception_fails`

**Functional check**
- Against the local CISO Assistant with the example suppliers and study, `hound run` produces the five checks with the expected outcomes and publishes them.

### s15-02 Metric definitions and instances
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

### s15-03 Metric samples from runs
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

### s15-04 Documentation and example repo
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
