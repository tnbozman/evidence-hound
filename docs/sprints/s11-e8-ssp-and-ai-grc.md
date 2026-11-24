# s11 Essential Eight system security plan and AI governance (AI6)

Status: In progress

**Goal:** two parts that share one idea, reporting per programme. Part 1: `hound` drafts the Essential Eight system security plan (SSP) and POA&M from the records already in CISO Assistant, in the shape the ISM expects, for people to review and sign. Part 2: AI governance runs on the same machinery as cyber compliance. The Australian *Guidance for AI Adoption* (AI6) is loaded into CISO Assistant as a versioned custom framework, the AI system register lives in CISO Assistant as assets, framework-neutral `ai.*` checks are read from CISO Assistant and mapped to AI6 requirements, and the committee and board reports (s12) work per programme, so AI6 is reported beside the Essential Eight rather than bolted on.

Comes after s10 and before s12, so that s12's reporting is built framework-neutral from the start. The person asked (Nov 2026) for the Essential Eight SSP in release 1, before AI governance, so Part 1 comes first; the NIST SP 800-171 / CMMC Level 2 layout stays with the NIST work in s17. Builds on s06 (custom library upload, framework snapshots, scope files), s09-09 to s09-14 (REST collector and profiles), s09-19 and s09-20 (suppliers and supplier assessments) and s10-04 (evidence plan and manual evidence tasks). All examples use the fictional Example Pty Ltd and `example.com`.

Rules that apply throughout:

- **CISO Assistant is the system of record.** AI systems are assets, AI risks are risk scenarios, AI suppliers are entities and solutions, AI6 results are audit rows. Nothing here adds an AI database of its own.
- **Deterministic first.** The SSP and every check are computed by code from records; a model is added only afterwards, only to propose text for people to review (s11-06), and never in evidence collection.
- **No LLM inference in evidence collection.** Every `ai.*` check is computed by `hound` from records in CISO Assistant. Model testing and runtime monitoring are attested by people (dates and evidence recorded in CISO Assistant), not measured by `hound`.
- **Framework-neutral.** Checks are named `ai.*` and say nothing about AI6; `mappings/ai6.yaml` turns them into AI6 results. NIST AI RMF and ISO/IEC 42001 can be added later as more mapping files.

What is configuration here, and what is not:

| Configuration in git, applied by `hound` | Stays in CISO Assistant |
| --- | --- |
| AI6 library file and snapshot, AI6 audit, programmes, the AI system asset class and custom field definitions, each AI system's identity, owner, risk tier and oversight arrangement | Impact assessment status and date, last test date, risk scenarios and their treatment, supplier questionnaire answers, requirement results set by the Cyber GRC team |

**Out of scope for now:** automated model testing and runtime monitoring collectors (manual attestation first), an AI incident workflow (AI6 3.4 stays manual), and mappings to NIST AI RMF or ISO/IEC 42001.

## Part 1: programmes and the Essential Eight system security plan

### s11-01 Programmes, and reporting per programme
- [x] Done

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
- `ProgrammeOutlineTests.Ai6_counts_from_snapshot_fixture` (after s11-09)
- Property: every assessable node of a snapshot is counted exactly once per group it belongs to.

**Functional check**
- `make run ARGS="programme list --scope examples/compliance-repo/scope"` lists the Essential Eight programme with 8 strategies at groups 1 and 2, and (after s11-15) the AI6 programme with 6 practices.

Result (2026-11-24): `make run ARGS="programme list --scope examples/compliance-repo/scope"` prints the `essential-eight` programme (framework version 3, audit of record EX-E8-2026) with 8 parent nodes (pa, pos, mfa, rap, ac, rmom, uah, rb) at groups 1 and 2: 48 requirements at Maturity Level One, 107 at Maturity Level Two. The AI6 line waits for s11-15. Also built: `scope.programme_id` (slug), duplicate ids (`scope.duplicate`), `scope.programme_framework_unknown`, `scope.programme_group_unknown`, `scope.programme_audit_unknown`, `scope.programme_audit_framework`, `scope.programme_audit_shared`, `scope.programme_needs_snapshot`; the schema and both example repos gained the programme.

### s11-02 SSP model and declaration
- [x] Done

**Story:** As the authorising official, I want an SSP assembled from the scope, systems, suppliers, controls and results we already keep in CISO Assistant, so that the plan matches what is actually recorded and does not go stale in a separate document.

What CISO Assistant offers today (checked in the v4.3.4 source, not yet run live): each audit has a Word and PDF report (`word_report`, profiles `full` and `attestation`) listing requirements with result, observation, evidence names and applied controls, and an action plan export (PDF, Excel, CSV). Custom Word templates (`CustomWordTemplate`) exist but are a paid-edition feature, and there is no SSP or system-description document. So `hound` assembles the SSP from CISO Assistant's API, the same way the s12 reports will, and attaches CISO Assistant's own audit report as an appendix rather than re-creating it.

**Acceptance criteria**
- `scope/ssp.yaml` declares one SSP per programme (s11-01): `programme`, `profile` (the controls annex: `ism` or `nist-800-171`, s11-04 and s17-03; the layout is always SP 800-18), `system_name`, `perimeter` (the boundary), `version`, `roles` (system owner, authorising official, security contact: teams or users from `access.yaml`), `documents` (boundary and network diagrams, data flow diagrams, policies and procedures, by document id from the audit, s06), and optional `sections` (Markdown files for the parts that cannot be generated: environment description, physical and personnel arrangements).
- A pure function builds an `SspDocument` from a read of CISO Assistant (the programme's audit rows, built so that the s12-04 posture snapshot reuses the same reads), the perimeter's assets with their owners (and custom fields once s11-12 exists), the suppliers and solutions linked to those assets (s09-19), the applied controls with owners, status and descriptions, and the requirement assessments with result, observation and evidence names. No live calls inside the core; the shell reads CISO Assistant once per generation.
- Every value in the SSP traces to a CISO Assistant object or a file in the repo; the JSON form of the document records the source of each value.
- Validation: the programme, perimeter, roles and documents exist; a profile's required sections are present.

**Tests first**
- `SspScopeTests.Programme_and_perimeter_must_exist`
- `SspScopeTests.Required_section_missing_is_reported`
- `SspBuilderTests.Inventory_is_the_perimeter_assets_with_owners`
- `SspBuilderTests.External_services_come_from_linked_solutions`
- `SspBuilderTests.Every_value_records_its_source`

**Functional check**
- `make run ARGS="ssp build --programme essential-eight --json"` against the local CISO Assistant with the example repo prints the document model with the example systems, suppliers and controls.

Result (2026-11-24): no CISO Assistant can run in the cloud session, so the check is `SspFunctionalTests.Builds_the_essential_eight_plan_from_the_seeded_records`, which runs `hound ssp build --programme essential-eight --json` against CISO Assistant v4.3.4 seeded from the example repo, with a `scope/ssp.yaml` for the Essential Eight programme. It passed in CI (functional job, run 45337444001): the inventory is Example 365, Example laptops and Example backup service with their owners from CISO Assistant, AC-MFA is among the controls, ISM-1486 reads compliant, and the document is the identity and access policy; the plan is kept in `test-results/ssp/`. The example has no suppliers yet, so external services are covered by `SspBuilderTests` until s11-07 adds them. Also built: `ssp.role_missing`, the `ssps` schema, and control status read from CISO Assistant as an observed value (never compared or written).

### s11-03 Requirement statements, gaps and POA&M
- [x] Done

**Story:** As an assessor, I want each requirement's implementation status, statement, responsible party and evidence stated the same way every time, so that I can trust the SSP was generated from the records and see what is missing.

**Acceptance criteria**
- Status is mapped deterministically from the requirement result: `compliant` to *Implemented*, `partially_compliant` to *Partially implemented*, `non_compliant` and `not_assessed` to *Not implemented* or *Planned* (with a POA&M item), `not_applicable` to *Not applicable* with the justification and approver recorded by onboarding or the audit file (s09-22, s06).
- The implementation statement is built from the applied controls linked to the requirement (their descriptions, which live in `scope/controls.yaml` and are reviewed in git) and the requirement observation. Responsible party is the control owner; evidence is the linked evidence names and the check outcomes for automated rows.
- A requirement with no statement, no owner, or a *Not applicable* without justification is a **gap**. Gaps are listed at the front of the draft and in `--json`; `--strict` exits non-zero while any gap remains.
- `hound ssp poam` produces the POA&M from the same model: every *Not implemented*, *Partially implemented* or *Planned* requirement, with the applied control, owner, ETA and status from CISO Assistant. Items the profile does not allow on a POA&M (s17-03) are flagged.

**Tests first**
- `SspStatementTests.Status_follows_requirement_result` (table test over all results)
- `SspStatementTests.Not_applicable_without_justification_is_a_gap`
- `SspStatementTests.Statement_joins_control_descriptions_in_ref_order`
- `PoamTests.Lists_unmet_requirements_with_owner_and_eta`
- Property: generating twice from the same inputs gives byte-identical output.

**Functional check**
- On the example repo, `make run ARGS="ssp build --programme essential-eight --strict"` exits non-zero and lists the example's gaps; after adding the missing control description it exits zero.

Result (2026-11-24): `SspFunctionalTests.Strict_lists_the_example_gaps_and_the_poam_lists_what_is_not_met` passed in CI against CISO Assistant v4.3.4 seeded from the example (head 6e28ba4): `hound ssp build --programme essential-eight --strict` exits 1 and lists `ISM-1486: no responsible party` (compliant by hand with no control linked), and `hound ssp poam` lists the rows not implemented; both outputs are kept in `test-results/ssp/`. The second half (fix and exit zero) is covered by the unit tests, since the fix for ISM-1486 is linking a control, which s11-07 does in the example. Deviation, decided while building: a row *not implemented* is not a gap, because the POA&M carries it; otherwise every unassessed row of a real audit would block `--strict`. Rows in place need a statement and an owner, Planned rows an owner, Not applicable rows a justification. Flagging requirements a profile does not allow on a POA&M waits for s17-03's list.

### s11-04 One SSP layout (NIST SP 800-18), with an ISM annex for the Essential Eight
- [x] Done

**Story:** As an Australian defence supplier, I want one system security plan layout that meets the ISM now and CMMC later, so that the same data and the same template serve DISP and CMMC.

**Decision (maintainer, 2026-11-24):** hound supports one SSP layout, the NIST SP 800-18 Rev 1 Appendix A structure (https://nvlpubs.nist.gov/nistpubs/legacy/sp/nistspecialpublication800-18r1.pdf), linked to the POA&M (s11-03). Only its controls annex switches by framework. This meets ISM-0041, which asks for an overview covering the system's purpose, its boundary and how it is managed, plus an annex of the applicable ISM controls and any others identified and implemented. The ASD Blueprint SSP is not a separate layout.

**Acceptance criteria**
- The plan follows SP 800-18 Rev 1 Appendix A in order: system name and identifier; categorisation (for the ISM, the classification); system owner; authorising official; other designated contacts; assignment of security responsibility; operational status; system type; general description and purpose; system environment (with the boundary, its diagram and the inventory); interconnections and external service providers (suppliers and solutions); applicable laws, regulations and policies; the controls annex; completion and approval dates (s11-05); ongoing plan maintenance.
- `profile` in `ssp.yaml` now names the annex: `ism` lists each ISM control in the programme's selected groups with applicability, implementation status, statement, responsible party and evidence, grouped by Essential Eight strategy (or Essentials domain for that programme); `nist-800-171` is built in s17-03. Each annex row links to its POA&M item when it has one.
- An ISM control the Essential Eight lists under several strategies appears once, with the strategies it serves (the mapping's `rows: all` rule).
- The sections a person writes follow the layout: purpose, environment, management (how the system is managed and maintained), laws and policies; the required list in `SspProfile` changes to match, for every profile.
- `docs/ssp.md` cites SP 800-18 Rev 1 and ISM-0041 and says which section of the plan meets each part of ISM-0041.

**Tests first**
- `SspLayoutTests.Sections_follow_sp_800_18_appendix_a`
- `IsmAnnexTests.Repeated_ism_control_appears_once_with_strategies`
- `IsmAnnexTests.Annex_row_links_its_poam_item`
- `SspLayoutTests.Renders_expected_markdown_from_fixture` (golden file)

**Functional check**
- Generate the SSP for the example Essential Eight ML2 audit on the local stack and read it against the Appendix A section list and ISM-0041.

Result (2026-11-24): `SspFunctionalTests.Renders_the_sp_800_18_plan_with_the_ism_annex_by_strategy` passed in CI against CISO Assistant v4.3.4 seeded from the example (head 3af8ae0). `hound ssp build --programme essential-eight` prints sections 1 to 15 of SP 800-18 Rev 1 Appendix A, with the gaps first and the POA&M as an appendix. The purpose section is read from its file, and the classification comes from `ssp.yaml`. ISM-1815 appears once in the annex, listed under `mfa, rap, ac, uah`. The output is kept in `test-results/ssp/ssp-essential-eight.md`. `docs/ssp.md` maps ISM-0041's purpose, boundary, management and annex to the plan's sections. Deviation: the listed `IsmAnnexTests` cases were written in `SspLayoutTests`, beside the layout they exercise. A missing section file prints as "not written yet", so a draft still builds; `ssp.section_missing` still catches an undeclared one.

### s11-16 Annex columns from the ASD Blueprint SSP annex template (added 2026-11-24, do after s11-04)
- [x] Done

Added at the maintainer's request (2026-11-24). The ASD Blueprint *System Security Plan Annex Template (June 2026)* (https://github.com/ASD-Blueprint/ASD-Blueprint-for-Secure-Cloud/blob/main/static/content/files/Blueprint%20System%20Security%20Plan%20Annex%20Template%20(June%202026).xlsx) is the annex assessors use for the ISM. The layout stays NIST SP 800-18 (s11-04). Only the ISM annex's columns follow the template.

Compared on 2026-11-24 (Essential Eight sheet against CISO Assistant's `essential-eight-2023` library):
- Both list 126 distinct ISM controls. They differ by one: the template has ISM-1899 (network devices outside administrative infrastructure cannot initiate connections to it), and the library has ISM-1900 (fortnightly vulnerability scanning under patch operating systems). Check this against the current ISM and the library's newer version, and record it under "Discovered work" for s12.
- The template's columns are Identifier, Description, Related Essential Eight controls and strategies, Org responsibility, Technology addressed, Responsible entity, Implementation status, Implementation comments, and applicability (NC, OS, P, S, TS, ML1 to ML3).
- Policy versus technical: CISO Assistant has this detail. Each requirement in the library links to reference controls from its `doc-pol` library, whose ids are prefixed `pol.` (policy), `proc.` (procedure), `tech.` (technical) or `doc.` (documentation). Every ISM control has one or two (pol 74, proc 73, tech 69, doc 4). The template's "Technology addressed" column means "covered by the Blueprint's Microsoft 365 design", not "is a technical control", so it is not the same split.
- hound's framework snapshot keeps only ref, parent, groups and title, so it drops the reference controls and full descriptions today.

**Acceptance criteria**
- The snapshot (`hound framework snapshot`) keeps each node's full description and its reference control ids. Older snapshots still load, with those fields empty.
- The `ism` annex columns become: Control, Description, Strategies, Maturity levels, Control type (Policy, Procedure, Technical, Documentation, from the reference controls), Responsible entity, Implementation status, Statement, Responsible, Evidence, POA&M.
- Responsible entity uses the template's values. It is Consumer by default, and Shared or Outsourced service provider when the control's linked systems are provided by a supplier's solution (s11-02 external services). It can be overridden per control in `controls.yaml`.
- Implementation status uses the template's vocabulary (Effective, Alternate control, Ineffective, No visibility, Not implemented, Not applicable, Not assessed), next to the SP 800-18 status already shown. The mapping from CISO Assistant results is written down in `docs/ssp.md`. A result of `partially_compliant` maps to Ineffective, and that choice is flagged for the maintainer at review.
- `hound ssp build --annex-xlsx <file>` is out of scope; Markdown and JSON only.

**Tests first**
- `FrameworkSnapshotTests.Keeps_reference_controls_and_description`
- `SspAnnexTests.Control_type_comes_from_reference_control_prefixes`
- `SspAnnexTests.Supplier_provided_system_makes_responsibility_shared`
- `SspAnnexTests.Asd_status_maps_from_result`

**Functional check**
- Regenerate the snapshot from the local CISO Assistant, build the example SSP, and check that ISM-1504's row reads Technical (its reference control is `tech.mfa`) with the expected responsible entity.

**Result (2026-11-24):** the snapshot was regenerated from the CISO Assistant v4.3.4 library (`hound framework snapshot`, identical to main apart from the new descriptions and reference controls). `SspFunctionalTests.Renders_the_sp_800_18_plan_with_the_ism_annex_by_strategy` built the example plan against the seeded CISO Assistant in CI (functional job, run 45387466690, passed): ISM-1504's row reads `| Technical |` with a responsible entity and an ASD implementation status.

### s11-05 Review, approval and versions
- [x] Done

**Story:** As the authorising official, I want to review, approve and version the SSP like any other controlled document, so that what we give an assessor is the version we signed.

**Acceptance criteria**
- Output is Markdown. `--format docx` also writes a Word file from the same model (person's decision, Nov 2026). Every page of an unsigned plan says *Draft*.
- `scope/ssp.yaml` records sign-offs like audit periods (s06-10): version, who approved and when, committed by pull request. A signed version records the SHA-256 of the generated file and the snapshot it was built from; regenerating a signed version from the same snapshot must give the same hash.
- `hound ssp publish` uploads the signed SSP and POA&M as a new revision of a "System security plan" evidence item in CISO Assistant (the system of record), with CISO Assistant's own audit report (`word_report`) for the same audit attached as the appendix. Generated SSPs are never committed to the repo, because they hold the organisation's system details.
- `hound ssp diff <version> <version>` lists changed statuses, statements, inventory and suppliers between two versions, so a reviewer reads only what changed; a boundary or inventory change is shown first because it usually needs a new major version.

**Tests first**
- `SspVersionTests.Unsigned_plan_is_marked_draft`
- `SspVersionTests.Signed_version_reproduces_its_hash`
- `SspDiffTests.Boundary_change_is_listed_first`
- Contract: `EvidenceRevisionClientTests.Uploads_ssp_as_new_revision`

**Functional check**
- Sign version 1.0 of the example SSP, publish it to the local CISO Assistant, change one control description, regenerate as 1.1 and confirm the diff and both evidence revisions.


**Result (2026-11-24):** `SspFunctionalTests.Signs_publishes_and_diffs_two_versions_of_the_plan` ran in CI against the seeded CISO Assistant (functional job, run 45387466690, passed): version 1.0 of the example plan built with `--snapshot-out`, signed by the authorising official, rebuilt from its snapshot as the approved plan and published with CISO Assistant's own Word audit report attached; the purpose section changed, version 1.1 signed and published; `ssp diff` listed the section change; evidence `SSP-ESSENTIAL-EIGHT` has two revisions. Changing a section stands in for changing a control description, so the shared instance keeps its seeded controls.

### s11-06 Control descriptions drafted by your own agent (optional, last)
- [x] Done

Comes after the deterministic SSP tasks, following the person's rule: deterministic code first, a model only afterwards and only to propose text for people to review. **Decided Nov 2026 (person):** hound has no model, so the person's own coding agent drafts the descriptions; hound gives it the facts and writes its text where review happens.

**Story:** As the Cyber GRC team, I want help writing the control descriptions the SSP is built from, so that a plan with many gaps can be closed quickly, without a model ever writing the SSP itself.

**Acceptance criteria**
- `hound ssp suggest --programme <id>` lists, as JSON, the applied controls with no description that leave an implemented requirement with no implementation statement: each control's name, owner and systems, and the requirements it serves with their description, status and evidence names. It sends nothing anywhere and holds no evidence content. `--from-snapshot` reads a snapshot instead of CISO Assistant.
- `hound ssp describe --control <ref> --text <description>` writes the description into `scope/controls.yaml` with a `# drafted with AI` comment above it, for a pull request. It never writes to CISO Assistant or into a generated SSP. A person accepts, edits or rejects each one in review; `scope apply` carries the accepted text to CISO Assistant.
- No model, no key and nothing scheduled: the scheduled runner never calls either command.

**Tests first**
- `SspSuggestTests.Only_gap_controls_are_listed`
- `SspSuggestTests.The_brief_holds_names_not_evidence_content`
- `ControlDescriptionEditTests.Writes_the_description_marked_as_drafted_with_ai`
- CLI: `SspCommandTests.Describe_writes_to_controls_yaml_not_ciso_assistant`

**Functional check**
- Suggest for the example repo's gaps, describe one control, apply the scope, and see that gap close in `ssp build`.


**Result (2026-11-24):** `SspFunctionalTests.Suggest_briefs_your_agent_and_describe_feeds_the_next_scope_plan` passed in CI against the seeded CISO Assistant (run on 411d39e): the brief lists no control the example already describes; `ssp describe` wrote AC-BACKUP's description to a copy of the example's `controls.yaml` under the "drafted with AI" comment, and `scope plan` showed the change for AC-BACKUP with that text. It was not applied, so the shared instance keeps the example's state; applying it is the ordinary `scope apply`.

### s11-07 SSP documentation and example
- [x] Done

**Acceptance criteria**
- `docs/govern/system-security-plan.md`: what the SSP is built from, the ISM-style profile, what cannot be generated and must be written or drawn by people (boundary and network diagrams, data flows, policies and procedures, physical and personnel arrangements, signatures), the review and approval flow, versions, and the optional drafting step.
- The example compliance repo gains `scope/ssp.yaml` for the Essential Eight programme, a fictional boundary diagram and environment section, all of which validate; a generated fictional SSP is a golden test file only.

**Tests first**
- `ExampleRepoTests.Example_compliance_repo_validates` covers the new files.
- Docs lint and link check.

**Functional check**
- Following the guide verbatim, produce, sign and publish the example SSP on the local stack.


**Result (2026-11-24):** `SspFunctionalTests.Signs_publishes_and_diffs_two_versions_of_the_plan` now uses the example's own `scope/ssp.yaml` and follows `docs/govern/system-security-plan.md`: build with `--snapshot-out`, sign, rebuild from the snapshot, publish with the audit report, then a second version and `ssp diff`. It passed in CI (run on 411d39e). `ExampleRepoTests` checks the example plan, its written sections and boundary diagram, and that no generated plan is committed.

## Part 2: AI governance

### AI governance in plain words

Written for a GRC team that knows cyber compliance but is new to AI governance (the person who asked for this sprint said so, and implementation should keep that reader in mind: plain words in every doc, command message and report heading).

- **What "AI GRC" means here.** The same governance, risk and compliance work you already do for cyber, applied to the AI tools the organisation uses or builds: know what AI is in use, who is responsible for each, what could go wrong, what was checked before it went live, and who can step in when it gets something wrong.
- **What counts as an AI system.** Any tool that makes predictions, recommendations, content or decisions from data with some autonomy: a chat assistant staff use, AI features switched on inside a SaaS product, a model a developer built. For a small business most AI systems are third-party products, so most of the work is knowing they are there and who looks after them.
- **What AI6 is.** Voluntary Australian Government guidance, not a law or certification. It plays the same role for AI that the Essential Eight plays for cyber: a short list of practices to adopt and show you follow. There is no AI6 audit body; the AI6 audit in CISO Assistant is your own self-assessment for the committee and board.
- **The six practices, in one line each.**
  1. *Decide who is accountable:* name one senior person for AI overall and an owner for each AI system.
  2. *Understand impacts:* before using an AI system, think about who it affects and how they can complain or get a decision reviewed.
  3. *Measure and manage risks:* screen each AI system to decide how risky it is, and record the risks of the riskier ones.
  4. *Share essential information:* keep a register of AI systems, and tell people when they are dealing with AI.
  5. *Test and monitor:* check an AI system works before relying on it, and keep checking.
  6. *Maintain human control:* make sure a person can oversee, override or switch off the system.
- **Foundations or Implementation.** Start with Foundations (the short version for organisations new to AI or using it in low-risk ways). Implementation is the detailed version for organisations that build AI or use it for higher-risk decisions. The example audit uses Foundations.
- **Words used in this sprint.** *Screening*: a short questionnaire that sorts an AI system into a risk tier. *Risk tier*: low, medium, high or prohibited. *Impact assessment*: a fuller review for medium and high tier systems, done before go-live. *Human oversight*: who checks the AI's output, before it acts (*in the loop*), while it acts (*on the loop*), or only afterwards (*out of the loop*). *Register*: the list of AI systems, kept in CISO Assistant as assets.

### About AI6

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
- The NAIC also publishes AI screening questions, an AI policy template and an AI register template. Their pages were not reachable as links when this sprint was planned (Nov 2026), so the register fields below come from the AI6 text, not the template. Task s11-11 obtains the templates and aligns the register fields and screening runbook with them, before s11-12.

Sources read on 2026-11-22: ai.gov.au *Guidance for AI Adoption: Foundations* and *Implementation guidance*, and the SafeAI-Aus AI6 summary. Neither ai.gov.au page states a licence. Decision 1 below covers what this means for the library text.

### s11-08 AI governance primer and guided walkthrough
- [ ] Done

Done first in Part 2, so the person reviewing the AI tasks has the background.

**Story:** As a GRC team member new to AI governance, I want a short primer and a guided walkthrough on the example deployment, so that I can run the AI programme without first becoming an AI expert.

**Acceptance criteria**
- `docs/ai-grc-primer.md`, readable in 15 minutes: the "AI governance in plain words" section above, expanded with one worked example per practice using the fictional Example Pty Ltd (for example: staff start using a chat assistant; what the GRC team records, who signs off, what the committee sees), a glossary, and "what you do not need to do at Foundations".
- A guided walkthrough in `docs/tutorials/ai-governance.md` against the local stack: register a new AI system, answer the screening questions, set the tier, record an impact assessment and a test date, run `hound`, and read the AI6 rows and the AI section of the committee report. Each step says what it is for in one sentence before saying what to type.
- A 30-minute training outline for the GRC team in `docs/training/` (alongside the s12-03 outlines) built on the walkthrough.
- Every `hound` message the sprint adds that a GRC user can see (validation errors, check observations, plan lines) names the AI6 practice in words, not only its ref (for example "AI6-6.1 Maintain human oversight and control"), and says what to do next.

**Tests first**
- Docs lint and link check.
- `CisoAssistantProfileTests.Observations_name_the_practice_in_words` (with s11-13).

**Functional check**
- Someone who has not read AI6 follows the walkthrough on the local stack without help, and notes anything unclear under "Discovered work".

**Progress (2026-11-24)**
- The primer is written: `docs/ai-grc-primer.md` (about 1,800 words), checked by `ExampleRepoTests.Ai_grc_primer_gives_each_practice_a_worked_example_and_a_glossary`.
- The walkthrough, the training outline and the message wording need the register, screening fields and checks, so they are written after s11-14, and this task is ticked then.

### s11-09 AI6 framework library and snapshot
- [ ] Done

**Story:** As the Cyber GRC team, I want AI6 as a framework in CISO Assistant, so that AI governance is assessed in audits like any other framework.

**Acceptance criteria**
- `frameworks/ai6.yaml`: a CISO Assistant custom library (same format as `examples/compliance-repo/frameworks/example-baseline.yaml`), `urn:evidence-hound:risk:library:ai6`, framework `urn:evidence-hound:risk:framework:ai6`, `version: 1`, `provider` the National AI Centre, `packager: evidence-hound`, publication date of the guidance, and a copyright line and attribution as decided in decision 1.
- Requirement nodes: the six practices at depth 1 (not assessable) and the 21 implementation sub-practices at depth 2 (assessable), with `ref_id` `AI6-1.1` to `AI6-6.2`. Titles as in the guidance; descriptions as decided in decision 1, with a link to the guidance.
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

**Progress (2026-11-24)**
- Library, snapshot, trace table and tests are done. The Foundations actions were read from the guidance PDF (v1.0, October 2025); it prints no licence, so the trace table summarises them in our own words. 20 of the 21 sub-practices are in `F`; only AI6-4.4 is not.
- The functional check (apply to the local CISO Assistant, an AI6 audit for group `F`) runs once the example scope declares AI6 (s11-15), because the functional tests share one seeded instance.

### s11-10 AI6 mapping
- [ ] Done

**Story:** As a compliance owner, I want a mapping from framework-neutral `ai.*` checks to AI6 requirements, so that AI6 audit rows are filled by runs where a check exists and owned by a named manual source where none does.

**Acceptance criteria**
- `mappings/ai6.yaml` uses the existing rules only (`all_pass`, `any_pass`, `threshold`), validates against `frameworks/ai6.json`, and covers all 21 sub-practices with checks or a `manual_only` entry naming a manual source. Proposed starting point (checks from s11-13; supplier checks per decision 4):

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
- `Ai6MappingTests.Every_ai_check_is_mapped` (after s11-13; until then unknown check IDs are warnings from `MappingLoader`, not errors)
- `MappingEngineTests.Ai6_rows_from_fixture_check_results` (golden results for a small fixture)

**Functional check**
- `make run ARGS="mapping validate mappings/ai6.yaml --framework frameworks/ai6.json"` prints `valid: 21 requirements` (9 mapped and 12 manual-only as proposed, or as finally agreed).

**Notes**
- AI6-5.4 overlaps the cyber programme. A later mapping could reuse cyber checks (for example `identity.mfa.*`) with `any_pass` once the AI systems' platforms are in scope of those collectors; kept manual here to avoid claiming coverage the collectors do not have.

**Progress (2026-11-24)**
- Changed from the plan, to follow s10-04 (decided after this sprint was planned): the 12 rows with no register check are mapped to checks a person attests through two manual sources, `examples/manual-sources/ai6/ai-governance` (yearly) and `ai-operations` (quarterly), as the Essential Eight mapping does. `manual_only` stays empty, and is documented in `docs/mapping-files.md` for requirements no manual source covers. The example evidence plan rows for the two sources come with the example AI programme (s11-15).
- Functional check: `hound mapping validate mappings/ai6.yaml --framework frameworks/ai6.json` printed `valid: 21 requirements` (8 from register checks, 12 from manual sources, and AI6-1.1 from both).

### s11-11 NAIC templates: register, screening questions and policy
- [ ] Done

Done before s11-12, because the register fields and the screening runbook follow from it.

**Story:** As the Cyber GRC team, I want the AI system register, screening and policy to follow the National AI Centre's own templates, so that an assessor sees the government's terms rather than ours.

**Acceptance criteria**
- The NAIC AI register template, AI screening questions and AI policy template are obtained from ai.gov.au (or supplied by the person if the site cannot be reached from the build environment), with their publication date, version and licence recorded in `docs/ai-grc.md`.
- A traceability table in `docs/ai-grc.md` maps every register template column to a CISO Assistant asset field (native or custom, s11-12), a linked object (owner, risk scenario, solution) or "not kept, because ...". The custom field table in s11-12 is updated to match before s11-12 starts.
- Each screening question maps to a field it sets (for example `ai_risk_tier`) or to a step of the screening runbook (s11-15); the questions that trigger a full impact assessment are named.
- The policy template is linked from the example repo as the source for the AI policy document attached to the AI6 audit (AI6-1.4, AI6-3.1). The template text is copied into the repo only if its licence allows it (decision 1).
- If a template conflicts with the AI6 text or with a choice made here (for example the tier names), the conflict is listed under "Discovered work" with a recommendation, rather than changed silently.

**Tests first**
- `AiRegisterTemplateTests.Every_template_column_is_traced` (reads the table in `docs/ai-grc.md` and the field definitions in the example `scope/fields.yaml`)
- Docs lint and link check.

**Functional check**
- Export the example AI systems from the local CISO Assistant with its asset Excel export and confirm every template column the trace table says is kept is present and filled for the example systems.

### s11-12 AI system register in CISO Assistant
- [ ] Done

**Story:** As the Cyber GRC team, I want every AI system recorded with its owner, risk tier, impact assessment status and human-oversight arrangement, so that the register can be checked and reported on, and anyone in CISO Assistant can find the AI systems.

**Design change (2026-11-24).** The plan used CISO Assistant custom fields. Reading the v4.3.4 source showed custom fields are an Enterprise feature: the Community Edition has no `custom_fields` feature flag, refuses writes and returns no values. Asset classes, lifecycle stages and filtering labels are in the Community Edition. So, as recommended to the person (decision card open; switch if they choose otherwise):
- An AI system is a **system** in `scope/setup.yaml` (a CISO Assistant asset) with an `ai` block. The block is the register entry, reviewed by pull request like the rest of the scope, dates and statuses included.
- In CISO Assistant the asset carries **filtering labels** (`ai-system`, `ai-tier-<tier>`, `ai-use-<use>`, `ai-oversight-<arrangement>`, and `ai-retired` once retired), and the register entry is added to its description, as solutions carry their sensitive data (s10-06). The register is CISO Assistant's asset list filtered by the `ai-system` label, exported with its own Excel export.
- AI risks are risk scenarios linked to the asset; third-party AI is a solution of a supplier (s09-19) linked to the system.
- This changes decision 5: impact assessment status and dates are set in git, on evidence, not in CISO Assistant. hound checks read the scope files and CISO Assistant (s11-13).

**Acceptance criteria**
- A system in `scope/setup.yaml` may have an `ai` block (field names to be aligned with the NAIC register template in s11-11):

| Key | Required | Values | Meaning |
| --- | --- | --- | --- |
| `tier` | yes | `low`, `medium`, `high`, `prohibited` | Result of screening |
| `screened_on` | yes | date | When the screening questions were answered |
| `use` | yes | `built`, `customised`, `third_party` | AI6's develop and deploy split |
| `oversight` | yes | `human_in_the_loop`, `human_on_the_loop`, `human_out_of_loop` | Human-oversight arrangement |
| `oversight_note` | yes | text | Who can override or switch it off, and the fallback |
| `impact_assessment` | no | `not_started`, `in_progress`, `approved`, `not_required` | Impact assessment status |
| `impact_assessed_on` | no | date | When the impact assessment was approved; only with `approved` |
| `in_use_from` | no | date | Go-live date; not set while being considered |
| `last_tested_on` | no | date | Last test or evaluation, on evidence |
| `retired_on` | no | date | When it stopped being used; after `in_use_from` |

- Validation: required keys, values and dates as above (`scope.value_invalid`, `scope.key_missing`); `ai.impact_date_without_approval`; `ai.retired_before_use`; a `third_party` AI system with no `solution` is `ai.solution_missing`, because the supplier checks need it.
- `scope plan` shows the labels and description like any other system field; `scope apply` writes them. A system without an `ai` block gets no AI labels.
- `docs/scope.md` documents the block.

**Tests first**
- `AiRegisterScopeTests.Ai_block_parses`
- `AiRegisterScopeTests.Required_keys_and_values_are_checked`
- `AiRegisterScopeTests.Impact_date_needs_an_approved_assessment`
- `AiRegisterScopeTests.Retired_before_use_is_reported`
- `AiRegisterScopeTests.Third_party_ai_needs_a_solution`
- `AiRegisterPlanTests.Register_entry_becomes_labels_and_description`
- `AiRegisterPlanTests.Systems_without_ai_carry_no_ai_labels`
- `ScopeSchemaTests` covers the `ai` block through the example.

**Functional check**
- Apply the example scope to the local CISO Assistant (with s11-15, which adds the example AI systems): the two example AI systems carry their labels and register entry, filtering assets by `ai-system` lists both, and re-applying writes nothing.

**Progress (2026-11-24)**
- Built: the `ai` block, its validation, the labels and the description (`AiRegisterScopeTests`, `AiRegisterPlanTests`, `ScopeSchemaTests`), and the `scope.schema.json` and `docs/scope.md` entries. The writer needed no change: systems already send labels and descriptions.
- The functional check runs with s11-15, which adds the example AI systems, because the functional tests share one seeded CISO Assistant.
- For s11-13: the register fields now live in the scope files, so the `ai.*` checks read the register from the scope (as the supplier and sensitive-data checks do) and read only owners, risk scenarios, solutions, contracts and assessments from CISO Assistant. The check table there still uses the old custom field names and is rewritten when s11-13 starts.

### s11-13 AI checks from the register and CISO Assistant
- [ ] Done

**Story:** As a compliance owner, I want the AI register checked on every run, so that missing owners, unassessed systems and stale tests show up as failing checks rather than at the next audit.

**Design change (2026-11-24).** The register entry now lives in the scope files (s11-12), so the checks split by where the data is:
- **`ai-register`, a built-in collector that reads the compliance repo's scope files.** It checks the register entries, which are the GRC team's declared record, reviewed by pull request. Each run saves the register as evidence (every AI system with its entry and the SHA-256 of `scope/setup.yaml`). The checks are a pure function over the parsed scope and today's date.
- **The `ciso-assistant` REST profile** (decision 4) reads what only CISO Assistant holds: risk scenarios linked to the AI systems' assets, and the supplier assessments and contracts.

**Acceptance criteria**
- An AI system is *in use* when `in_use_from` is on or before today and it is not retired on or before today. Each check covers the AI systems in use, unless stated; with none, it is `not_applicable`, never a pass.

| Check | Source | Passes when |
| --- | --- | --- |
| `ai.register_complete` | `ai-register` | every AI system (in use or not) has an impact assessment status |
| `ai.owner_assigned` | `ai-register` | every AI system (in use or not) has an owner |
| `ai.screening_recorded` | `ai-register` | none in use is tier `prohibited`, and each was screened on or before going into use |
| `ai.impact_assessment_before_use` | `ai-register` | each `medium` or `high` tier system is `approved`, dated on or before `in_use_from` |
| `ai.human_oversight_documented` | `ai-register` | no `high` tier system is `human_out_of_loop` (the note is required by validation) |
| `ai.tested_before_use` | `ai-register` | each has `last_tested_on` |
| `ai.test_results_current` | `ai-register` | `last_tested_on` is within 6 months for `high` and 12 months otherwise |
| `ai.risks_in_register` | `ciso-assistant` | every asset labelled `ai-tier-medium` or `ai-tier-high` is linked to at least one risk scenario |
| `ai.suppliers_assessed` | `ciso-assistant` | every asset labelled `ai-use-third-party` is provided by a solution whose entity has a current assessment |
| `ai.supplier_contracts_current` | `ciso-assistant` | that solution has an active contract not past its end date |

- Every observation names the failing AI systems and the AI6 practice in words (s11-08), and says what to do next.
- `ai-register` settings: `scope` (the scope folder, relative to the config file). A scope that does not load is an error naming the first problems, never a fail.
- The `ciso-assistant` profile passes the profile tests (s09-12) with recorded, fictional responses; a field it cannot read is *unable to assess*, never a fail. It also carries the generic `supplier.assessments_current` and `supplier.contracts_current`.

**Tests first**
- `AiRegisterChecksTests.Ai_system_without_owner_fails_owner_assigned`
- `AiRegisterChecksTests.Impact_assessment_after_go_live_fails`
- `AiRegisterChecksTests.Stale_test_uses_tier_interval`
- `AiRegisterChecksTests.No_ai_systems_in_use_is_not_applicable`
- `AiRegisterChecksTests.Prohibited_system_in_use_fails_screening`
- `AiRegisterChecksTests.High_tier_out_of_loop_fails_oversight`
- `AiRegisterChecksTests.Observations_name_the_practice_in_words`
- `AiRegisterCollectorContractTests` (the s04 contract suite) and `AiRegisterCollectorTests.Unloadable_scope_is_an_error`
- `profiles/ciso-assistant.tests`: `High_tier_without_risk_scenario_fails`, `Third_party_without_current_assessment_fails`, `Unreadable_field_is_unable_to_assess`

**Functional check**
- Against the local CISO Assistant with the example AI systems, `make run ARGS="run --dry-run"` produces the ten checks with the expected outcomes (one example system is deliberately missing a test date), and `mapping apply mappings/ai6.yaml` on the output fills AI6-4.1 compliant and AI6-5.1 non-compliant.

**Progress (2026-11-24)**
- Built: `AiRegisterChecks` (pure, 18 tests), the built-in `ai-register` collector (contract suite and `AiRegisterCollectorTests`), and the `ciso-assistant` profile with four recorded test cases (`hound profile test ciso-assistant --profiles profiles`: all four pass). Guide: `docs/collectors/ciso-assistant.md`.
- Checked against the CISO Assistant v4.3.4 serializers: related objects read as `{"str", "id"}`, so labels are matched on `filtering_labels[*].str`; an entity assessment's `conclusion` is `blocker`, `warning`, `ok` or `not_applicable`, with `expiry_date` saying when it stops being valid.
- `Ai6MappingTests.Register_checks_name_a_sub_practice_they_are_mapped_to` keeps each observation's practice in step with the mapping.
- The functional check runs with s11-15, once the example has AI systems.

### s11-14 Register summary for programme reports
- [ ] Done

**Story:** As the security committee, I want each programme's report section to summarise its register (for AI6: AI systems by tier, new this period, and gaps), so that the AI section answers "what AI do we use and is it governed" as well as "which AI6 rows pass".

**Design change (2026-11-24).** The register is the scope's systems (s11-12), not an asset class with custom fields, so a programme names a register kept in the scope.

**Acceptance criteria**
- A programme (s11-01) may declare `register`: `of` (`ai_systems`, the systems with an `ai` block, or `systems`, every system) and `by` (the fields to count by: `tier`, `use` and `oversight` for AI systems, `type` for systems). An unknown register or field is `scope.programme_register_unknown`.
- `RegisterItems.Of(setup, of)` gives each item's name, its field values, and the dates it was added (an AI system's `in_use_from`) and retired (`retired_on`).
- A pure function `RegisterSummary.Of(items, by, from, to, failing)` returns the number of items, counts by field value (items retired before the period ends are left out), the items added and retired in the period, and the items a failing check names. `AiRegisterFinding` names the systems it fails. s12-04's `PostureSnapshot` includes the summary per programme (s12 amendment).
- Generic: nothing in the summary knows about AI; the Essential Eight programme can declare `register: { of: systems, by: [type] }`.
- `hound programme list` shows each programme's register counts (text and `--json`).

**Tests first**
- `RegisterSummaryTests.Counts_by_field`
- `RegisterSummaryTests.Added_and_retired_in_period`
- `RegisterSummaryTests.Empty_register_gives_zero_counts_not_missing_section`
- `RegisterSummaryTests.Failing_lists_the_items_failing_checks_name`
- `RegisterItemsTests.Ai_systems_give_their_register_fields_and_dates`
- `ProgrammeScopeTests.Register_must_be_known_with_known_fields`
- `AiRegisterChecksTests.Findings_name_the_failing_systems`
- `ProgrammeCommandTests.List_shows_the_register_counts`

**Functional check**
- `make run ARGS="programme list --scope examples/compliance-repo/scope"` shows the AI6 programme's register: the two example AI systems by tier and use (with s11-15, which adds them). The report output itself is checked in s12.

**Progress (2026-11-24)**
- Built: `ProgrammeRegister`, `RegisterItems`, `RegisterSummary`, the failing systems on each `AiRegisterFinding`, the `register` key with `scope.programme_register_unknown`, the schema, and the register line in `hound programme list` (text and `--json`). `ProgrammeCommandTests.List_shows_the_register_counts` runs it on the example repo with a register of systems: "Register: 3 systems. type: primary 1, supporting 2."
- The functional check with the AI6 programme's register runs with s11-15.

### s11-15 Documentation and example repo
- [ ] Done

**Acceptance criteria**
- `docs/ai-grc.md`: what AI6 asks, how it maps to CISO Assistant (assets, custom fields, risk scenarios, suppliers, audits), the field set and who sets each field, the checks, the Foundations trace table (s11-09), adopting at Foundations and moving to Implementation, and the limits (no model testing or monitoring collectors, no AI incident workflow, manual practices).
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

- **s12-04** `PostureSnapshot` holds results per programme (framework, audit of record, selected groups, counts per group and per parent node) and each programme's register summaries (s11-14), instead of assuming Essential Eight ML1 and ML2.
- **s12-05** committee report sections 2 to 8 are grouped by programme where they hold requirement or register figures; section 3 groups changes by programme, then by parent node (strategy, practice).
- **s12-06** board report sections 1 and 2 are per programme: one compliance position line and one posture table per programme, with the four-quarter trend; Essential Eight ML1 and ML2, Essentials phases and AI6 Foundations are examples of the same rule.
- **s12-01** `hound mapping coverage` counts per group and parent node from any snapshot; Essential Eight is the example, not the only case.

Also applied, following decision 4:

- **s16-01** now extends the `ciso-assistant` profile with `risk.studies_reviewed`, `risk.exceptions_current` and `bia.recovery_tested`, since s11-13 creates the profile and (decision 4) its supplier checks.
- **s12-04** reuses the CISO Assistant reads built for the SSP (s11-02) when it builds the posture snapshot.
- **s17** now targets CMMC Level 2 through CISO Assistant's built-in `cmmc-2.0` framework (Rev 3 optional), and keeps only the NIST SP 800-171 / CMMC Level 2 SSP layout (s17-03) and its documentation (s17-04); the SSP engine, ISM-style layout, review and versions are s11-02 to s11-07.

- **README** s10 row no longer lists suppliers (moved to s09) and names the s09 tasks carried into s10.

## Decisions

Decided by the person on 2026-11-22:

- This sprint is numbered s11, and the later sprints moved up by one (old s11 to s17 are now s12 to s18).
- Obtaining the NAIC templates is a task in this sprint (s11-11), not a prerequisite.
- The person is new to AI6 and AI governance, so the sprint adds a primer and walkthrough (s11-08) and asks for plain words throughout.
- Recommendations accepted:
  1. **AI6 text in a public repo.** The ai.gov.au pages state no licence, so the library carries practice and sub-practice titles with a short description in our own words and a link, until the licence is confirmed; full text only if it is CC BY 4.0.
  2. **Library grain.** The 21 sub-practices are assessable, with groups `F` (Foundations) and `I` (Implementation).
  3. **Programmes** are declared in a `programmes` list in `setup.yaml`.
  4. **Pulled forward from s16.** This sprint builds the `ciso-assistant` profile with the supplier checks (`ai.suppliers_assessed`, `ai.supplier_contracts_current`, and the generic `supplier.assessments_current`, `supplier.contracts_current`); risk-study and BIA checks stay in s16, which depends on s15.
  5. **Register fields.** Identity, owner, tier and oversight are reviewed in git; impact assessment status and dates are set by the GRC team in CISO Assistant (the split in the s11-12 table).
- System security plan (person, 2026-11-22):
  1. The Essential Eight SSP comes into release 1, before AI governance: Part 1 of this sprint. The NIST SP 800-171 / CMMC Level 2 layout stays in s17.
  2. CMMC Level 2 uses CISO Assistant's built-in `cmmc-2.0` framework; a NIST SP 800-171 Rev 3 mapping is optional.
  3. Output is Markdown, with an option to generate Word.
  4. Model-suggested control descriptions are kept, as proposals people review (s11-06), and come after the deterministic tasks: deterministic code first, a model only afterwards, and never in evidence collection.
     Updated by the person on 2026-11-24 (s11-06): the person's own agent drafts them from `hound ssp suggest`; hound has no model.

## Carried from s09 and s10

Moved from s10 on 2026-11-24, at the maintainer's request. Built and tested in CI; each waits on a run in the maintainer's own
environment, not on code. Details stay under the task in `s09-e8-gap-collectors.md`.
- [ ] s09-01 Application control policies from Intune: a real Graph export for the App Control for Business setting IDs.
- [ ] s09-15 Firewall firmware update timeliness: whether the read-only role may call the firmware upgrade check, and a dry run on the maintainer's tenant.
- [ ] s09-17 The Sophos trial on a real tenant.
- [ ] s09-29 Microsoft 365 checks follow the capability map: a run against the maintainer's tenant.
- [ ] s09-38 to s09-42 Agent-driven onboarding and manual evidence: the maintainer's own run with their agent, including s09-39, their XGS firewall.
- [ ] s09-54 and s09-57 `make trial` on the maintainer's machine.

## Discovered work

- (s11-16, for s12) The ASD Blueprint SSP annex template (June 2026) lists ISM-1899 under the Essential Eight where CISO Assistant's `essential-eight-2023` library (v3, CISO Assistant v4.3.4) lists ISM-1900. Check both against the current Essential Eight maturity model and ISM, and against a newer library version, while completing the mapping.

- s11-02: a programme whose audit of record is an audit series (with a cycle) has rows under each period's ref_id; `hound ssp build` reads rows under the series ref_id only. Pick the current period's audit before s11-05 versions the plan.

- s11-01: framework snapshots hold parent node URNs but not their titles, so `hound programme list` shows a strategy as its short code (`mfa`, `rap`). The s12 reports need the name ("Multi-factor authentication"): add parent titles to the snapshot (`hound framework snapshot`) before s12-05.

## Sprint review
