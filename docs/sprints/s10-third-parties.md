# s10 Third parties as configuration

Status: Planned

**Goal:** suppliers, their solutions and contracts, and supplier assessments live in the private configuration repo and are applied to CISO Assistant by `hound`, replacing the third-party spreadsheet.

The governance collector and metrics, planned here before the release 1 reorder, moved to [s15](s15-governance-and-metrics.md) in release 2.

Builds on s06 (scope files, plan, apply, inventory, framework libraries for questionnaires). Everything is matched by `ref_id`, or by the key CISO Assistant uses where it has no `ref_id` (name and domain; email for representatives). All examples use the fictional Example Pty Ltd and `example.com`.

What is configuration here, and what is not:

| Configuration in git, applied by `hound` | Stays in CISO Assistant |
| --- | --- |
| Entities, solutions, contracts, representatives (optional), supplier assessment set-up and review cycle, forced tiers with reasons | Questionnaire answers, tiering form results and tier approvals, tier history, campaigns |

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

### s10-03 Documentation and example repo
- [ ] Done

**Acceptance criteria**
- `docs/third-parties.md`: file formats, validation, plan and apply, privacy guidance for representatives, and what stays in CISO Assistant (questionnaire answers, tiering results, campaigns).
- The example compliance repo (s06-12) gains two fictional suppliers and a supplier assessment, all of which validate.

**Tests first**
- `ExampleRepoTests.Example_compliance_repo_validates` covers the new files.
- Docs lint and link check.

**Functional check**
- Following the guide verbatim, add a supplier to the example repo and apply it.

## Discovered work

## Sprint review
