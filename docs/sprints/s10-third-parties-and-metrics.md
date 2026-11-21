# s10 Third parties and metrics as configuration

Status: Planned

**Goal:** suppliers, their solutions and contracts, supplier assessments, and metric definitions and instances live in the private configuration repo and are applied to CISO Assistant by `hound`. `hound` also reads CISO Assistant's own governance records as a collector, and writes metric samples from its runs, so supplier assurance and trends are evidenced rather than asserted.

Builds on s06 (scope files, plan, apply, inventory) and s09 (libraries beyond frameworks). Everything is matched by `ref_id`, or by the key CISO Assistant uses where it has no `ref_id` (name and domain; email for representatives). All examples use the fictional Example Pty Ltd and `example.com`.

What is configuration here, and what is not:

| Configuration in git, applied by `hound` | Stays in CISO Assistant |
| --- | --- |
| Entities, solutions, contracts, representatives (optional), supplier assessment set-up and review cycle, forced tiers with reasons; metric definitions, metric instances, dashboards | Questionnaire answers, tiering form results and tier approvals, tier history, campaigns; manually entered metric samples |

### s10-01 Suppliers, solutions and contracts
- [ ] Done

**Story:** As a supplier owner, I want our suppliers, what they provide and our contracts with them declared in the repo, so that the supplier register is reviewed and complete.

**Acceptance criteria**
- `scope/third-parties/<entity>.yaml` declares one entity: `ref_id`, name, description, mission, country (ISO 3166-1 alpha-2), currency (ISO 4217), parent entity, domain, and its dependency (0–4), penetration (0–4), maturity (1–4) and trust (1–4).
- It also declares the entity's solutions (`ref_id`, name, description, criticality 1–4) and contracts (`ref_id`, name, status `draft`, `active`, `expired` or `terminated`, start and end dates, annual expense, currency, the solutions covered, and identifiers such as LEI or VAT).
- Representatives are optional. They are personal data, so the guide recommends keeping them in CISO Assistant unless the repo is private and access-controlled; when declared, they are matched by email and never given a login by `hound`.
- If s06-05 confirmed the API can update the main entity, `organisation` in `scope/setup.yaml` manages it here (the s06 discovered-work item).
- Validation: country and currency codes; contract end after start; an `active` contract past its end date is reported (`supplier.contract_lapsed`); solutions named by a contract belong to the same entity.

**Tests first**
- `SupplierScopeTests.Invalid_country_or_currency_is_reported`
- `SupplierScopeTests.Active_contract_past_end_date_is_reported`
- `SupplierScopeTests.Contract_solution_must_belong_to_entity`
- Contract: `SupplierClientTests.Creates_entity_solution_and_contract`

**Functional check**
- Apply two fictional suppliers to the local CISO Assistant; re-applying writes nothing; a changed contract end date is a one-field update.

### s10-02 Supplier assessments and tiers
- [ ] Done

**Story:** As a supplier owner, I want each supplier's assessment set up from the repo on a review cycle, so that reviews start on time and nobody sets them up by hand.

**Acceptance criteria**
- An entity file may declare assessments: name, solutions, perimeter, due date, criticality, the questionnaire framework (a library URN), and `review_every` (such as `12 months`). Applying creates the entity assessment and its linked questionnaire audit.
- When a review is due, `hound` opens the next one: a new revision if the API supports it (s06-05), otherwise a new assessment named with its period, using the same periods logic as audits (s06-10).
- Answers, tiering form results and campaigns are never written by `hound`.
- `forced_tier` (one of the criticality scale's keys, such as `critical`) with a required `reason` may be declared; `scope plan` reports when CISO Assistant's current tier differs from it.

**Tests first**
- `SupplierAssessmentTests.Due_review_opens_the_next_one`
- `SupplierAssessmentTests.Answers_are_never_in_the_plan`
- `SupplierScopeTests.Forced_tier_requires_reason`
- `ScopePlanTests.Tier_differing_from_forced_tier_is_reported`

**Functional check**
- With a fixed clock past a review date, `scope apply` opens the next fictional supplier assessment in the local CISO Assistant with its questionnaire.

### s10-03 CISO Assistant governance collector
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
- The checks are added to the Essentials mapping draft where they evidence actions with no Essential Eight source (supplier assurance GOV-11, supply chain PRO-16, risk management GOV-03), reducing the manual-only list from the migration plan.

**Tests first**
- `GovernanceCollectorContractTests` (the s04 contract suite)
- `GovernanceChecksTests.Supplier_past_review_cycle_fails`
- `GovernanceChecksTests.Tier_below_threshold_is_not_required`
- `GovernanceChecksTests.Expired_approved_exception_fails`

**Functional check**
- Against the local CISO Assistant with the example suppliers and study, `hound run` produces the five checks with the expected outcomes and publishes them.

### s10-04 Metric definitions and instances
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

### s10-05 Metric samples from runs
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

### s10-06 Documentation and example repo
- [ ] Done

**Acceptance criteria**
- `docs/third-parties.md` and `docs/metrics.md`: file formats, the governance collector, metric measures, privacy guidance for representatives, and what stays in CISO Assistant.
- The example compliance repo gains two fictional suppliers, a supplier assessment, two metrics and a dashboard, all of which validate.

**Tests first**
- `ExampleRepoTests.Example_compliance_repo_validates` covers the new files.
- Docs lint and link check.

**Functional check**
- Following the guides verbatim, add a supplier and a metric to the example repo and apply them.

## Discovered work

## Sprint review
