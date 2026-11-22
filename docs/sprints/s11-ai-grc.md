# s11 AI governance (AI6)

Status: Planned

**Goal:** AI governance runs on the same machinery as cyber compliance. The Australian *Guidance for AI Adoption* (AI6) is loaded into CISO Assistant as a versioned custom framework, the AI system register lives in CISO Assistant as assets, framework-neutral `ai.*` checks are read from CISO Assistant and mapped to AI6 requirements, and the committee and board reports (s12) work per programme, so AI6 is reported beside the Essential Eight rather than bolted on.

Comes after s10 and before s12, so that s12's reporting is built framework-neutral from the start. Builds on s06 (custom library upload, framework snapshots, scope files), s09-09 to s09-14 (REST collector and profiles), s09-19 and s09-20 (suppliers and supplier assessments) and s10-04 (evidence plan and manual evidence tasks). All examples use the fictional Example Pty Ltd and `example.com`.

Rules that apply throughout:

- **CISO Assistant is the system of record.** AI systems are assets, AI risks are risk scenarios, AI suppliers are entities and solutions, AI6 results are audit rows. Nothing here adds an AI database of its own.
- **No LLM inference in evidence collection.** Every `ai.*` check is computed by `hound` from records in CISO Assistant. Model testing and runtime monitoring are attested by people (dates and evidence recorded in CISO Assistant), not measured by `hound`.
- **Framework-neutral.** Checks are named `ai.*` and say nothing about AI6; `mappings/ai6.yaml` turns them into AI6 results. NIST AI RMF and ISO/IEC 42001 can be added later as more mapping files.

What is configuration here, and what is not:

| Configuration in git, applied by `hound` | Stays in CISO Assistant |
| --- | --- |
| AI6 library file and snapshot, AI6 audit, programmes, the AI system asset class and custom field definitions, each AI system's identity, owner, risk tier and oversight arrangement | Impact assessment status and date, last test date, risk scenarios and their treatment, supplier questionnaire answers, requirement results set by the Cyber GRC team |

**Out of scope for now:** automated model testing and runtime monitoring collectors (manual attestation first), an AI incident workflow (AI6 3.4 stays manual), and mappings to NIST AI RMF or ISO/IEC 42001.

## About AI6

*Guidance for AI Adoption*, released by the National AI Centre (Department of Industry, Science and Resources) on 21 October 2025, replacing the Voluntary AI Safety Standard. Two versions with the same six practices:

| Practice | Implementation sub-practices |
| --- | --- |
| 1 Decide who is accountable | 1.1 Accountable people; 1.2 Supply chain accountabilities; 1.3 AI literacy and training; 1.4 AI governance framework |
| 2 Understand impacts and plan accordingly | 2.1 Identify and engage stakeholders; 2.2 Establish feedback and redress processes; 2.3 Monitor for systemic issues |
| 3 Measure and manage risks | 3.1 Establish a fit-for-purpose risk management framework; 3.2 Assess AI system risks; 3.3 Implement controls for AI system risks; 3.4 Monitor and report incidents |
| 4 Share essential information | 4.1 Maintain an AI register; 4.2 AI system transparency and explainability; 4.3 Supply chain transparency; 4.4 AI-generated content transparency |
| 5 Test and monitor | 5.1 Pre-deployment testing; 5.2 Monitor system performance; 5.3 Conduct additional testing proportionate to risk; 5.4 Implement robust data and cybersecurity measures |
| 6 Maintain human control | 6.1 Maintain human oversight and control; 6.2 Decommission when appropriate |

- **Foundations** is for organisations starting out or using AI in low-risk ways. It has the six practices only, each with "Getting started" and "Next steps" actions (30 in all), and no numbered sub-practices.
- **Implementation** is for organisations that build or customise AI, or have higher-risk uses. It has the 21 sub-practices above and about 73 numbered items (1.1.1 to 6.2.4), some tagged (DEV) or (DEP).
- The NAIC also publishes AI screening questions, an AI policy template and an AI register template. Their pages were not reachable as links when this sprint was planned (Nov 2026), so the register fields below come from the AI6 text, not the template. Task s11-08 obtains the templates and aligns the register fields and screening runbook with them, before s11-04.

Sources read on 2026-11-22: ai.gov.au *Guidance for AI Adoption: Foundations* and *Implementation guidance*, and the SafeAI-Aus AI6 summary. Neither ai.gov.au page states a licence; see open decision 1.

### s11-01 Programmes, and reporting per programme
- [ ] Done

**Story:** As the authorising official, I want reports organised by compliance programme (Essential Eight, AI6, later NIST SP 800-171), so that adding a framework adds a section to the same reports instead of a new report or code that assumes Essential Eight maturity levels.

The reports themselves are built in s12. This task declares programmes in configuration and amends s12-04 to s12-06 so they are built per programme (the amendment is made to the s12 file in the planning PR that adds this sprint). The code is already framework-neutral where it matters: `FrameworkSnapshot` carries implementation groups and parent URNs for any framework, and the scope plan's compliance impact (`ComplianceImpact`) counts per implementation group, not per maturity level.

**Acceptance criteria**
- `scope/setup.yaml` gains an optional `programmes` list. Each programme has `id` (lowercase slug, unique), `name`, `framework` (a framework URN declared in the same file), `groups` (the implementation groups the organisation reports against, which must exist in the framework's snapshot), `audit` (the `ref_id` of the audit, or audit series with a cycle, of record) and optional `group_by` (`parent`, the default, groups requirements under their parent node: the Essential Eight strategy, the AI6 practice).
- Validation: the audit exists and uses the programme's framework; an audit belongs to at most one programme; a framework without a snapshot cannot be a programme (`scope.programme_needs_snapshot`).
- `hound programme list` prints each programme with its framework, version, groups, audit of record, and assessable requirement counts per group and per parent node, read from the snapshot only (no CISO Assistant call). `--json` gives the same.
- Programmes create nothing in CISO Assistant and never appear in `scope plan`.
- s12-04, s12-05 and s12-06 are amended as listed in "Changes to other sprints" below.

**Tests first**
- `ProgrammeScopeTests.Programme_framework_must_be_declared`
- `ProgrammeScopeTests.Programme_groups_must_exist_in_snapshot`
- `ProgrammeScopeTests.Audit_in_two_programmes_is_reported`
- `ProgrammeScopeTests.Programmes_are_not_in_the_plan`
- `ProgrammeOutlineTests.Counts_requirements_per_group_and_parent` (pure, from the Essential Eight snapshot fixture)
- `ProgrammeOutlineTests.Ai6_counts_from_snapshot_fixture` (after s11-02)
- Property: every assessable node of a snapshot is counted exactly once per group it belongs to.

**Functional check**
- `make run ARGS="programme list --scope examples/compliance-repo/scope"` lists the Essential Eight programme with 8 strategies at groups 1 and 2, and (after s11-07) the AI6 programme with 6 practices.

### s11-02 AI6 framework library and snapshot
- [ ] Done

**Story:** As the Cyber GRC team, I want AI6 as a framework in CISO Assistant, so that AI governance is assessed in audits like any other framework.

**Acceptance criteria**
- `frameworks/ai6.yaml`: a CISO Assistant custom library (same format as `examples/compliance-repo/frameworks/example-baseline.yaml`), `urn:evidence-hound:risk:library:ai6`, framework `urn:evidence-hound:risk:framework:ai6`, `version: 1`, `provider` the National AI Centre, `packager: evidence-hound`, publication date of the guidance, and a copyright line and attribution as decided in open decision 1.
- Requirement nodes: the six practices at depth 1 (not assessable) and the 21 implementation sub-practices at depth 2 (assessable), with `ref_id` `AI6-1.1` to `AI6-6.2`. Titles as in the guidance; descriptions as decided in open decision 1, with a link to the guidance.
- Two implementation groups: `F` (Foundations) and `I` (Implementation). Every sub-practice is in `I`. A sub-practice is also in `F` when at least one Foundations action falls under it; the trace from each of the 30 Foundations actions to one sub-practice is a table in `docs/ai-grc.md`, so the grouping can be reviewed.
- The library version goes up whenever its content changes; a change note at the top of the file says what changed. Re-uploading an older version is refused by the existing scope rule (a framework loaded from an older library is reported, never upgraded silently).
- `frameworks/ai6.json` is the snapshot from `hound framework snapshot frameworks/ai6.yaml`, committed and listed in `frameworks/README.md` with its source and attribution.
- `scope validate` accepts the library as a framework with `file:` (no loader changes are expected; s06 already uploads custom library files).

**Tests first**
- `Ai6LibraryTests.Has_six_practices_and_twenty_one_assessable_sub_practices`
- `Ai6LibraryTests.Every_sub_practice_is_in_implementation_group`
- `Ai6LibraryTests.Foundations_group_matches_trace_table` (reads the table in `docs/ai-grc.md`)
- `Ai6LibraryTests.Snapshot_matches_library` (regenerating the snapshot gives the committed file)
- `FrameworkLibraryReaderTests` gains a case for a library whose groups are letters, not numbers, if the existing tests do not cover it.

**Functional check**
- `make run ARGS="framework snapshot frameworks/ai6.yaml --out /tmp/ai6.json"` reports 21 assessable requirements and matches the committed snapshot. With the example scope pointing at the library, `scope apply` against the local CISO Assistant (`make ca-up`) uploads and loads it, and an AI6 audit with group `F` shows only the Foundations rows.

### s11-03 AI6 mapping
- [ ] Done

**Story:** As a compliance owner, I want a mapping from framework-neutral `ai.*` checks to AI6 requirements, so that AI6 audit rows are filled by runs where a check exists and owned by a named manual source where none does.

**Acceptance criteria**
- `mappings/ai6.yaml` uses the existing rules only (`all_pass`, `any_pass`, `threshold`), validates against `frameworks/ai6.json`, and covers all 21 sub-practices with checks or a `manual_only` entry naming a manual source. Proposed starting point (checks from s11-05; supplier checks per open decision 4):

| Requirement | Rule | Checks, or manual source |
| --- | --- | --- |
| AI6-1.1 Accountable people | all_pass | `ai.owner_assigned`; the senior AI governance owner is a hand assessment in the audit |
| AI6-1.2 Supply chain accountabilities | all_pass | `ai.supplier_contracts_current` |
| AI6-1.3 AI literacy and training | manual | training records |
| AI6-1.4 AI governance framework | manual | AI policy (document in the audit) |
| AI6-2.1, 2.2, 2.3 | manual | impact assessment records, feedback and redress procedure, systemic issue reviews |
| AI6-3.1 Risk management framework | manual | AI policy and risk method |
| AI6-3.2 Assess AI system risks | all_pass | `ai.screening_recorded`, `ai.impact_assessment_before_use` |
| AI6-3.3 Implement controls for AI system risks | all_pass | `ai.risks_in_register` |
| AI6-3.4 Monitor and report incidents | manual | incident register (AI incident workflow out of scope) |
| AI6-4.1 Maintain an AI register | all_pass | `ai.register_complete`, `ai.owner_assigned` |
| AI6-4.2, 4.4 | manual | disclosure statements |
| AI6-4.3 Supply chain transparency | all_pass | `ai.suppliers_assessed` |
| AI6-5.1 Pre-deployment testing | all_pass | `ai.tested_before_use` |
| AI6-5.2 Monitor system performance | manual | owner's monitoring attestation (monitoring collectors out of scope) |
| AI6-5.3 Additional testing proportionate to risk | all_pass | `ai.test_results_current` |
| AI6-5.4 Data and cybersecurity measures | manual | the organisation's cyber programme (see notes) |
| AI6-6.1 Human oversight and control | all_pass | `ai.human_oversight_documented` |
| AI6-6.2 Decommission when appropriate | manual | retirement records |

- Every `manual_only` row also appears in the example evidence plan (s10-04) with an owner, an interval and a runbook, so manual AI6 evidence gets CISO Assistant's reminders like any other.
- `docs/mapping-files.md` documents `manual_only` (the loader, schema and validator already support it; the doc does not mention it).

**Tests first**
- `Ai6MappingTests.All_requirement_refs_exist_in_framework_snapshot`
- `Ai6MappingTests.Every_sub_practice_is_mapped_or_manual`
- `Ai6MappingTests.Every_ai_check_is_mapped` (after s11-05; until then unknown check IDs are warnings from `MappingLoader`, not errors)
- `MappingEngineTests.Ai6_rows_from_fixture_check_results` (golden results for a small fixture)

**Functional check**
- `make run ARGS="mapping validate mappings/ai6.yaml --framework frameworks/ai6.json"` prints `valid: 21 requirements` (9 mapped and 12 manual-only as proposed, or as finally agreed).

**Notes**
- AI6-5.4 overlaps the cyber programme. A later mapping could reuse cyber checks (for example `identity.mfa.*`) with `any_pass` once the AI systems' platforms are in scope of those collectors; kept manual here to avoid claiming coverage the collectors do not have.

### s11-08 NAIC templates: register, screening questions and policy
- [ ] Done

Numbered last so the other task numbers stay stable, but done before s11-04, because the register fields and the screening runbook follow from it.

**Story:** As the Cyber GRC team, I want the AI system register, screening and policy to follow the National AI Centre's own templates, so that an assessor sees the government's terms rather than ours.

**Acceptance criteria**
- The NAIC AI register template, AI screening questions and AI policy template are obtained from ai.gov.au (or supplied by the person if the site cannot be reached from the build environment), with their publication date, version and licence recorded in `docs/ai-grc.md`.
- A traceability table in `docs/ai-grc.md` maps every register template column to a CISO Assistant asset field (native or custom, s11-04), a linked object (owner, risk scenario, solution) or "not kept, because ...". The custom field table in s11-04 is updated to match before s11-04 starts.
- Each screening question maps to a field it sets (for example `ai_risk_tier`) or to a step of the screening runbook (s11-07); the questions that trigger a full impact assessment are named.
- The policy template is linked from the example repo as the source for the AI policy document attached to the AI6 audit (AI6-1.4, AI6-3.1). The template text is copied into the repo only if its licence allows it (open decision 1).
- If a template conflicts with the AI6 text or with a choice made here (for example the tier names), the conflict is listed under "Discovered work" with a recommendation, rather than changed silently.

**Tests first**
- `AiRegisterTemplateTests.Every_template_column_is_traced` (reads the table in `docs/ai-grc.md` and the field definitions in the example `scope/fields.yaml`)
- Docs lint and link check.

**Functional check**
- Export the example AI systems from the local CISO Assistant with its asset Excel export and confirm every template column the trace table says is kept is present and filled for the example systems.

### s11-04 AI system register in CISO Assistant
- [ ] Done

**Story:** As the Cyber GRC team, I want every AI system recorded in CISO Assistant with its owner, risk tier, impact assessment status and human-oversight arrangement, so that the register is in the system of record and can be checked and reported on.

How it works, using CISO Assistant's own features (checked against the v4.3.4 models, not yet run live):
- An AI system is an **asset** with the asset class *AI system*. Assets already have owners (actors), a lifecycle stage, filtering labels, parent assets and **custom fields** (`CustomFieldDefinition`: key, label, type text, number, date, boolean, choice, multi-choice or URL, scoped to a folder).
- AI risks are **risk scenarios** linked to the asset; third-party AI is a **solution** of an **entity** (s09-19) linked to the asset.
- The register is CISO Assistant's asset list filtered by class, exported with its own Excel export. No separate register file or spreadsheet.

**Acceptance criteria**
- `scope/fields.yaml` (new, generic, not AI-specific) declares asset classes (`name`, optional `parent`) and custom field definitions (`model: asset`, `key`, `label`, `type`, `choices` for choice types, `required`, `help`, optional `domain` for scope). Applying creates or updates them by key; removing one needs `--prune`, as for other objects.
- Systems in `scope/setup.yaml` gain optional `class` (an asset class) and `fields` (custom field values, by key, checked against the definition's type and choices). Fields a system lists are configuration and kept in step; fields it does not list are left to CISO Assistant and never in the plan.
- The example AI field set, as aligned with the NAIC register template in s11-08 (this table is the starting point):

| Key | Type | Set in | Meaning |
| --- | --- | --- | --- |
| `ai_risk_tier` | choice: `low`, `medium`, `high`, `prohibited` | git | Result of screening |
| `ai_screened_on` | date | git | When the screening questions were answered |
| `ai_use` | choice: `built`, `customised`, `third_party` | git | Matches AI6's (DEV) and (DEP) split |
| `ai_oversight` | choice: `human_in_the_loop`, `human_on_the_loop`, `human_out_of_loop` | git | Human-oversight arrangement |
| `ai_oversight_note` | text | git | Override points and fallback path |
| `ai_impact_assessment` | choice: `not_started`, `in_progress`, `approved`, `not_required` | CISO Assistant | Set by the GRC team on evidence |
| `ai_impact_assessed_on` | date | CISO Assistant | Approval date |
| `ai_in_use_from` | date | CISO Assistant | Go-live date |
| `ai_last_tested_on` | date | CISO Assistant | Last test or evaluation, on evidence |

- Lifecycle: use the native asset lifecycle stage if v4.3.4's built-in stages include an in-use and a retired stage; otherwise add a choice field `ai_stage`. Decide when recording contract responses and note it here.
- `scope plan` shows asset class, field definition and declared field value changes like other objects; values left to CISO Assistant are never shown or written.

**Tests first**
- `FieldScopeTests.Choice_field_needs_choices`
- `FieldScopeTests.System_field_value_must_match_type_and_choices`
- `FieldScopeTests.Unknown_field_key_is_reported`
- `FieldPlanTests.Unlisted_field_values_are_never_in_the_plan`
- `FieldPlanTests.Removing_a_definition_needs_prune`
- Contract: `CustomFieldClientTests.Creates_definition_and_sets_asset_value` and `AssetClassClientTests.Creates_class` (recorded responses from the local CISO Assistant; record the custom field endpoints first, as they were not checked when planning)

**Functional check**
- Apply the example scope to the local CISO Assistant: the *AI system* class and the field definitions exist, the two example AI systems show their tier and oversight in the asset view, and re-applying writes nothing. Setting `ai_last_tested_on` by hand in CISO Assistant does not appear in the next plan.

### s11-05 AI checks from CISO Assistant
- [ ] Done

**Story:** As a compliance owner, I want the AI register checked on every run, so that missing owners, unassessed systems and stale tests show up as failing checks rather than at the next audit.

**Acceptance criteria**
- A REST collector profile `ciso-assistant` in the public profile library (`profiles/ciso-assistant/`, s09-12) reads CISO Assistant with a read-only token: assets of class *AI system* with owners and custom fields, risk scenarios with their assets, and (per open decision 4) entities, solutions, contracts and entity assessments. No coded collector.
- Checks, each over AI systems that are in use unless stated, each evidenced by the saved query results:

| Check | Passes when |
| --- | --- |
| `ai.register_complete` | every AI system has `ai_risk_tier`, `ai_use`, `ai_oversight` and a lifecycle stage |
| `ai.owner_assigned` | every AI system has at least one owner |
| `ai.screening_recorded` | every AI system has `ai_screened_on`, and none in use is `prohibited` |
| `ai.impact_assessment_before_use` | every AI system in use with tier `medium` or `high` has `ai_impact_assessment` `approved`, dated on or before `ai_in_use_from` |
| `ai.risks_in_register` | every AI system with tier `medium` or `high` is linked to at least one risk scenario |
| `ai.human_oversight_documented` | every AI system has `ai_oversight` and `ai_oversight_note`; a `high` tier system is not `human_out_of_loop` |
| `ai.tested_before_use` | every AI system in use has `ai_last_tested_on` |
| `ai.test_results_current` | `ai_last_tested_on` is within 6 months for `high` and 12 months otherwise |
| `ai.suppliers_assessed` | every `third_party` AI system's solution belongs to an entity with a current assessment (s09-20) |
| `ai.supplier_contracts_current` | every `third_party` AI system's solution has an active contract not past its end date |

- No AI systems in use gives `not_applicable` for the per-system checks, not a pass. A field the profile cannot read gives *unable to assess*, never a fail.
- Field-to-field date comparison (`ai.impact_assessment_before_use`) uses the declarative operators if s09-11 supports it; otherwise it is a named code check compiled into `hound`.
- The profile passes the collector contract suite (s04) and the profile tests (s09-12) with recorded responses.

**Tests first**
- `CisoAssistantProfileTests.Ai_system_without_owner_fails_owner_assigned`
- `CisoAssistantProfileTests.High_tier_without_risk_scenario_fails`
- `CisoAssistantProfileTests.Impact_assessment_after_go_live_fails`
- `CisoAssistantProfileTests.Stale_test_uses_tier_interval`
- `CisoAssistantProfileTests.No_ai_systems_is_not_applicable`
- `CisoAssistantProfileTests.Unreadable_custom_field_is_unable_to_assess`
- `CisoAssistantProfileContractTests` (the s04 contract suite)

**Functional check**
- Against the local CISO Assistant with the example AI systems, `make run ARGS="run --dry-run"` produces the ten checks with the expected outcomes (one example system is deliberately missing a test date), and `mapping apply mappings/ai6.yaml` on the output fills AI6-4.1 compliant and AI6-5.1 non-compliant.

### s11-06 Register summary for programme reports
- [ ] Done

**Story:** As the security committee, I want each programme's report section to summarise its register (for AI6: AI systems by tier, new this period, and gaps), so that the AI section answers "what AI do we use and is it governed" as well as "which AI6 rows pass".

**Acceptance criteria**
- A programme (s11-01) may declare `registers`: an asset class and the choice fields to count by (for AI6: class *AI system*, by `ai_risk_tier` and `ai_use`).
- A pure function `RegisterSummary.Of(assets, register, period)` returns counts by field value, assets added and retired in the period, and assets failing any check that names the class. s12-04's `PostureSnapshot` includes it per programme (s12 amendment).
- Generic: nothing in the function knows about AI; the Essential Eight programme can declare a register of systems the same way.

**Tests first**
- `RegisterSummaryTests.Counts_by_choice_field`
- `RegisterSummaryTests.Added_and_retired_in_period`
- `RegisterSummaryTests.Empty_register_gives_zero_counts_not_missing_section`
- `ProgrammeScopeTests.Register_fields_must_be_choice_fields`

**Functional check**
- A small console check in the test suite prints the example AI register summary; the figures match the two example AI systems. The report output itself is checked in s12.

### s11-07 Documentation and example repo
- [ ] Done

**Acceptance criteria**
- `docs/ai-grc.md`: what AI6 asks, how it maps to CISO Assistant (assets, custom fields, risk scenarios, suppliers, audits), the field set and who sets each field, the checks, the Foundations trace table (s11-02), adopting at Foundations and moving to Implementation, and the limits (no model testing or monitoring collectors, no AI incident workflow, manual practices).
- A screening runbook template (`examples/compliance-repo/runbooks/ai-screening.md`) the GRC team follows when a new AI system is proposed: answer the NAIC screening questions, set the tier, and when a full impact assessment is needed. Supplier approval (s09-20) comes before an AI system is registered, as for other systems.
- The example compliance repo gains: the AI6 library reference, an AI6 audit (`EX-AI6-2026`, group `F`), the AI6 programme, two fictional AI systems (*Example Assist*, a third-party chat assistant, tier `medium`; *Example ticket triage*, built in-house, tier `low`), the field definitions, AI rows in the evidence plan (s10-04) and an AI6 mapping copy; all of which validate. A note suggests registering `hound onboard` itself (s09-21 to s09-26) when an organisation uses it, since it is AI use.
- `docs/plan.md` and the README mention AI governance as a programme alongside cyber compliance.

**Tests first**
- `ExampleRepoTests.Example_compliance_repo_validates` covers the new files.
- Docs lint and link check.

**Functional check**
- Following `docs/ai-grc.md` verbatim on the local stack, register a third fictional AI system, apply, run, and see its checks and AI6 rows update.

## Changes to other sprints

Applied in the planning PR that adds this sprint:

- **s12-04** `PostureSnapshot` holds results per programme (framework, audit of record, selected groups, counts per group and per parent node) and each programme's register summaries (s11-06), instead of assuming Essential Eight ML1 and ML2.
- **s12-05** committee report sections 2 to 8 are grouped by programme where they hold requirement or register figures; section 3 groups changes by programme, then by parent node (strategy, practice).
- **s12-06** board report sections 1 and 2 are per programme: one compliance position line and one posture table per programme, with the four-quarter trend; Essential Eight ML1 and ML2, Essentials phases and AI6 Foundations are examples of the same rule.
- **s12-01** `hound mapping coverage` counts per group and parent node from any snapshot; Essential Eight is the example, not the only case.

Proposed, not applied (see open decisions):

- **s16-01** becomes "extend the `ciso-assistant` profile" with `risk.studies_reviewed`, `risk.exceptions_current` and `bia.recovery_tested`, since s11-05 creates the profile and (if decision 4 is accepted) its supplier checks.
- **README** s10 row still describes suppliers, which moved to s09.

## Open decisions

Decided (person, 2026-11-22): this sprint is numbered s11 and the later sprints moved up by one (old s11 to s17 are now s12 to s18), and obtaining the NAIC templates is a task in this sprint (s11-08), not a prerequisite.

Still open, with the recommendation in each:

1. **AI6 text in a public repo.** The ai.gov.au pages state no licence. Recommended: practice and sub-practice titles with a short description in our own words and a link, until the licence is confirmed; full text only if it is CC BY 4.0.
2. **Library grain.** 21 sub-practices assessable with groups `F` and `I` (recommended), 6 practices only, or the ~73 numbered items.
3. **Where programmes are declared.** A `programmes` list in `setup.yaml` (recommended) or derived from audits with no new configuration.
4. **Pull forward from s16.** Recommended: build the `ciso-assistant` profile here with the supplier checks (`ai.suppliers_assessed`, `ai.supplier_contracts_current`, and the generic `supplier.assessments_current`, `supplier.contracts_current` for the cyber programmes), and leave risk-study and BIA checks in s16, which depend on s15.
5. **Register fields in git or in CISO Assistant.** Recommended split as in the s11-04 table: identity, owner, tier and oversight reviewed in git; assessment status and dates set by the GRC team in CISO Assistant.

## Discovered work

## Sprint review
