# s06 CISO Assistant as configuration, and multi-framework foundations

Status: Done

**Goal:** an organisation's assurance structure in CISO Assistant is declared in version-controlled YAML, then validated, previewed and applied by `hound` instead of by clicking. One collection run feeds several framework audits, CISO Assistant changes only when a check's state changes, and audits run in periods that close with a sign-off. The errors, schemas and plans are precise enough for a person or an AI assistant to get the configuration right through ordinary pull requests.

**Why now:** Essential Eight ML2 is the audit of record today and needs requirement rows addressed by URN to publish correctly. In a few months the ASD Essentials for enterprise IT replaces it (s11), and a few months after that NIST SP 800-171 runs alongside the Essentials (s13). Both need requirement rows addressed by URN and several audits per run, and both are easier to set up from declared scope than by hand. See [the migration plan](../migrations/essential-eight-to-asd-essentials.md).

Principles for every task:
- **Git is the source of truth.** `hound` never deletes what it did not create, and never deletes at all without `--prune`. Changes made in the CISO Assistant UI show up as drift in `scope plan`.
- **Match by stable keys.** Objects are matched by name, `ref_id` or URN, never by CISO Assistant's internal IDs, so the same files rebuild an empty instance.
- **Validate offline first.** Everything that can be checked without CISO Assistant is checked by `scope validate`, including requirement references against committed framework snapshots, so an assistant can iterate without access to the live instance.
- **Machine-readable output.** Every command that reports problems or changes has `--format json`, with stable problem codes, file, line, a short message and a hint on how to fix it.

All examples use the fictional Example Pty Ltd and `example.com`.

### s06-01 Requirement rows by URN, scoped to the audit's groups
- [x] Done

**Story:** As a compliance owner, I want a mapping to name each requirement exactly, so that frameworks that repeat a `ref_id` across domains or strategies publish to the right row.

**Acceptance criteria**
- A mapping requirement is named by `urn` (a CISO Assistant requirement node URN), or by `ref`. `ref` stays valid for existing files.
- A `ref` that matches several rows is an error (`mapping.ref_ambiguous`, listing the matching URNs) unless the entry says `rows: all`, which writes the same result to every matching row. `rows: all` suits the Essential Eight, where a repeated ISM control has the same text under each strategy. It does not suit the Essentials, where each domain's row is rated separately.
- The publisher selects requirement assessments by audit and requirement URN. `Selector.RequirementInAudit` by `ref_id` remains only for unique refs.
- A framework snapshot (`frameworks/<library>.json`: library and framework URN, version, and each assessable node's URN, `ref_id`, parent, implementation groups and a short title) can be generated from a library file with `hound framework snapshot`. `hound mapping validate --framework <snapshot>` reports unknown URNs and refs, and ambiguous refs.
- The publisher skips requirements outside the audit's selected implementation groups and reports them as `skipped: outside selected groups`, not as errors.
- The dev seed imports `essential-eight-2023`, `asd-essentials-enterprise-it` and the transition mapping instead of the legacy `essential-eight` library.

**Tests first**
- `MappingTests.Requirement_named_by_urn_is_accepted`
- `MappingValidationTests.Ambiguous_ref_lists_matching_urns`
- `PlanTests.Rows_all_writes_one_result_to_every_matching_row`
- `MappingValidationTests.Unknown_urn_is_reported_with_line`
- `FrameworkSnapshotTests.Snapshot_lists_assessable_nodes_with_groups` (from a small fictional library with a repeated `ref_id`)
- `PlanTests.Requirements_outside_selected_groups_are_skipped`

**Functional check**
- Against the local CISO Assistant: publish a fictional mapping that names an Essentials action in two domains by URN, and confirm each domain's row gets its own result.
- Result (2026-11-21): `RequirementRowsFunctionalTests` in CI run [44898045325](https://github.com/tnbozman/evidence-hound/actions/runs/44898045325) passed; the Boundary services row of `IT.PRO-01.1` is `compliant`, the Identity services row `non_compliant`. Offline: `hound mapping validate mappings/essential-eight-2023.yaml --framework frameworks/essential-eight-2023.json` prints `valid: 16 requirements`. Evidence: [docs/evidence/s06/s06-01.md](../evidence/s06/s06-01.md).

### s06-02 Essential Eight 2023 ML2 mapping for the checks we have
- [x] Done

**Story:** As a compliance owner whose audit of record is Essential Eight ML2, I want the checks that exist today mapped to the Essential Eight (November 2023) framework, so that collection runs update my real audit now rather than after s11.

**Acceptance criteria**
- `mappings/essential-eight-2023.yaml` targets `urn:intuitem:risk:framework:essential-eight-2023`, never the legacy `essential-eight`. ISM controls repeated across strategies use `rows: all` (s06-01).
- It maps every check from the M365 collector (s05) and the manual collector to ML1 and ML2 requirements. Requirements no check covers yet are listed under `manual_only` with a suggested manual source. The file grows as each later collector lands (s07) and is completed in s11.
- The example config's Essential Eight audit selects maturity levels 1 and 2; ML3-only requirements are skipped, not reported as errors.
- A framework snapshot for `essential-eight-2023` is committed under `frameworks/`.

**Tests first**
- `E8MappingTests.All_requirement_urns_exist_in_framework_snapshot`
- `E8MappingTests.Every_m365_and_manual_check_is_mapped`
- `E8MappingTests.Repeated_ism_refs_use_rows_all`

**Functional check**
- Against the local CISO Assistant with `essential-eight-2023` loaded: `hound run` with the M365 recordings fills the example ML2 audit, and a control repeated across strategies, such as ISM-0123, gets its result in every strategy's row.
- Result (2026-11-21): `E8RunFunctionalTests` in CI run [44899216065](https://github.com/tnbozman/evidence-hound/actions/runs/44899216065) passed; all 4 rows of ISM-0123 are `compliant` and ISM-1504 is assessed. Evidence: [docs/evidence/s06/s06-02.md](../evidence/s06/s06-02.md).

### s06-03 Several audits from one run
- [x] Done

Moved from s13-02, because the Essentials parallel run needs it before 800-171 does.

**Story:** As a compliance owner, I want one collection run to update every audit that uses its checks, so that one evidence stream serves the Essential Eight and the Essentials during the migration, then the Essentials and NIST SP 800-171.

**Acceptance criteria**
- The config's `target` becomes `targets`, a list. Each target names a mapping, an audit and its controls. A single `target` is still accepted.
- Plan and publish upload each piece of evidence once and link it from every audit that uses it.
- A failure publishing to one audit does not stop the others; the report lists each audit's items.

**Tests first**
- `PlanTests.Two_targets_share_one_evidence_upload`
- `ConfigTests.Single_target_is_read_as_one_item_list`
- `PublishTests.Failure_in_one_audit_does_not_stop_the_next`

**Functional check**
- `hound run` against the local CISO Assistant updates the example Essential Eight 2023 and Essentials audits. The evidence appears once and is linked from both.
- Result (2026-11-21): `TwoAuditsFunctionalTests` in CI run [44899216065](https://github.com/tnbozman/evidence-hound/actions/runs/44899216065) passed; both audits are updated with no failures, and the conditional access evidence exists once and is linked from both. Evidence: [docs/evidence/s06/s06-03.md](../evidence/s06/s06-03.md).

### s06-04 Change-only publishing, traced to the run
- [x] Done

**Story:** As a compliance owner with daily runs, I want CISO Assistant updated only when a row's state actually changes, so that its history shows real changes rather than daily noise, and every change traces back to the run that made it.

**Acceptance criteria**
- `hound plan` reads the current requirement assessments, evidence and applied control statuses of each target audit, and `planUpdates(desired, current)` (pure) sends only what differs.
- A row's state is its result, the outcome of each check behind it, and the hashes of its linked evidence. Timestamps are not part of it, so an unchanged row is never rewritten.
- Evidence is one CISO Assistant evidence item per collector output, such as `m365-users`. A changed file is uploaded as a new revision of that item, so CISO Assistant keeps its own version history and SHA-256 hash; an unchanged hash uploads nothing.
- A computed row edited in the CISO Assistant UI is restored and reported as drift (`publish.drift_corrected`), naming the row and the old and new values.
- Every run has an ID. The observation of each row it changes names the run ID, and each audit gets an append-only history file in the evidence store (`_history/<audit>.jsonl`): date, run ID, rows changed, rows verified unchanged.
- The run report and `hound status` show changed, unchanged and drift counts per audit.

**Tests first**
- `PlanTests.Unchanged_row_is_not_sent`
- `PlanTests.Changed_check_outcome_with_same_result_is_sent` (the observation must stay true)
- `PlanTests.Timestamp_only_change_is_not_sent`
- `PlanTests.Known_evidence_hash_is_linked_not_uploaded`
- `PlanTests.Row_edited_in_ui_is_restored_and_reported_as_drift`
- `HistoryTests.Run_appends_one_line_per_audit`
- Property: planning against the state that publishing the plan produces is empty.

**Functional check**
- Run `hound run` twice against the local CISO Assistant with the same recordings: the second run sends nothing and the history file shows every row verified unchanged. Edit one row in the UI and run again: only that row is written, and drift is reported.
- Result (2026-11-21): `ChangeOnlyFunctionalTests` in CI run [44905017778](https://github.com/tnbozman/evidence-hound/actions/runs/44905017778) passed; the second run sends nothing and its history line counts every row unchanged, and after an edit of EX-1.1 through the API the third run writes that row only, reports `publish.drift_corrected` and records drift 1. Evidence: [docs/evidence/s06/s06-04.md](../evidence/s06/s06-04.md).

### s06-05 CISO Assistant configuration inventory
- [x] Done

**Story:** As the capability owner, I want a verified list of what in CISO Assistant is configuration and how each piece can be set through the API, so that the scope format covers what matters and never promises what the API cannot do.

**Acceptance criteria**
- `docs/ciso-assistant-configuration.md` lists each object type with: configuration or runtime data; whether `hound` manages it in this sprint, later, or never; its REST endpoint; its stable key; and how create, update, lock and delete behave. It is checked against the local CISO Assistant v4.3.4 and its OpenAPI schema (`/api/schema/`), not only the user guides.
- In this sprint: libraries (frameworks, mappings, reference controls, risk matrices), domains, perimeters, assets, applied controls, audits (including `ref_id`, ETA, due date, authors, reviewers, field visibility, lock and baseline), evidence and evidence revisions, requirement assessments.
- Recorded for s09: risk matrices and threats, risk studies and scenarios, risk acceptances, security exceptions, BIAs, and assets' security and recovery objectives.
- Recorded for s10: entities (including the main entity), solutions, contracts, representatives, entity assessments and revisions, tiers, metric definitions and instances, samples, dashboards.
- Recorded for later sprints: users, teams and role assignments; feature flags and general settings (such as the default score scale); task definitions; document templates (CISO Assistant imports these as a zip of Markdown files with frontmatter) and managed documents; webhooks.
- Recorded as out of scope: workflows, quick forms, campaigns, EBIOS RM and quantitative risk studies, which are authored in CISO Assistant or imported through libraries and the Data Wizard.
- It answers, with evidence, the questions later tasks depend on:
  - Does locking an audit also freeze the applied controls linked to it? (s06-10)
  - Does replacing an evidence file create a revision with its SHA-256 hash, and can the API read that hash? (s06-04)
  - Which audit fields hold status, start and end dates, and how is an audit created from a baseline? (s06-10)
  - Are writes to a field the service account's role can only read ignored silently? (s06-06)
  - Can the API upload a custom library, update the main entity, and set feature flags? (s06-09, s10-01)
  - Do BIAs, entity assessments and metric instances have a `ref_id`, and can entity assessments be revised through the API? (s09-04, s10-02)
- A schema snapshot (`tests/fixtures/ciso-assistant/openapi-v4.3.4.json`) is committed, and a test fails when an endpoint `hound` uses disappears from it, so upgrades are checked rather than assumed.

**Tests first**
- `OpenApiSnapshotTests.Every_endpoint_hound_calls_is_in_the_snapshot`

**Functional check**
- Each answer above reproduced against the local CISO Assistant with a short script under `scripts/`, its output recorded under this task.
- Result (2026-11-21): `scripts/ca-inventory.sh` ran against v4.3.4 in the functional CI job (runs [44906131552](https://github.com/tnbozman/evidence-hound/actions/runs/44906131552) and [44907514706](https://github.com/tnbozman/evidence-hound/actions/runs/44907514706)). Locking an audit does not freeze its controls; evidence revisions carry the file's SHA-256; audits have `status`, `eta` and `due_date` and can be created from a baseline; read-only field writes are dropped silently; a custom library uploads as a raw file body (201), and the main entity and feature flags can be PATCHed; BIAs, entity assessments and metric instances have `ref_id`, and entity assessments have no revisions. Evidence: [docs/evidence/s06/s06-05.md](../evidence/s06/s06-05.md).

### s06-06 Scope file and offline validation
- [x] Done

**Story:** As a capability owner, I want the organisation's structure declared in files with a schema, so that I can change it through reviewed pull requests, with an assistant's help, instead of clicking in CISO Assistant.

**Acceptance criteria**

The layout follows one that has already been tried against CISO Assistant: one setup file for the structure, and one file per audit.

- `scope/setup.yaml` declares, with a JSON Schema (`schemas/scope.schema.json`):
  - `organisation.root_folder`: the existing root folder the domains sit under (default `Global`).
  - `frameworks`: libraries to load, by library and framework URN, or custom library files.
  - `domains`: folders, each with its `perimeters` (a single `perimeter` is accepted) and its `systems`. A system has a name, a type (`primary` or `supporting`), a description, an optional owner and parents, and optional per-framework tags such as an Essentials domain or `cui: true`.
- `scope/controls.yaml` declares each applied control once: `ref_id`, name, description, domain (or the global domain), owner, status, priority, category, ETA, systems, the reference control it derives from (optional), and the documents that evidence it.
- `scope/audits/<audit>.yaml` declares one compliance assessment:
  - `ref_id` (the stable key), `name`, `framework` (URN), `groups` (such as `["1", "2"]` for ML2), the domain and perimeter it sits in, and optionally ETA, due date, authors, reviewers and `field_visibility`;
  - `systems` in scope;
  - `documents`: static evidence such as policies and reports, with files by path or glob, the systems they cover and the requirements they support;
  - `controls`: the applied controls this audit relies on, by `ref_id`, each with the requirements it supports in this framework. The controls themselves are declared once in `scope/controls.yaml` (below), because CISO Assistant treats an applied control as a reusable object that serves many requirements across frameworks;
  - `assessments`: hand-written results for requirements no check covers, each with `ref` (or `urn`), `status`, `result` and `observation`;
  - optionally `cycle` and `sign_offs`, for an audit that runs in periods (s06-10);
  - `unlisted`: what happens to in-group requirements the file does not list, either `not_applicable` with a reason (for an audit scoped to one system) or `not_assessed`. It is required, so nothing is marked not applicable silently.
- `hound scope new-audit --framework <snapshot> --groups 1,2` writes a starting audit file listing every in-group requirement with its title as a comment, with status `to_do` and result `not_assessed`.
- `hound scope validate` reports every problem at once with file, line and code:
  - unknown or duplicate keys;
  - references to undeclared domains, perimeters, systems, frameworks, documents or controls;
  - groups not defined by the framework's snapshot, and requirement refs not in it;
  - a document or control that cites a requirement the audit does not assess;
  - a hand-written assessment for a requirement that a target's mapping also computes (`scope.result_conflict`), so a person and a collector never write the same row;
  - a `field_visibility` that leaves a field `hound` writes (result, observation, evidences) at less than Edit for the auditor role (`scope.field_not_writable`), because CISO Assistant ignores such writes without an error.
- Each deployment `target` must name an audit and controls declared in the scope (`config.target_not_in_scope`).
- `--format json` prints the problems as a list for tools.

**Tests first**
- `ScopeParseTests.Reads_setup_and_audit_files`
- `ScopeParseTests.Single_perimeter_is_read_as_one_item_list`
- `ScopeValidationTests.Undeclared_system_is_reported_with_file_and_line`
- `ScopeValidationTests.Unknown_group_for_framework_is_reported`
- `ScopeValidationTests.Control_citing_unassessed_requirement_is_reported`
- `ScopeValidationTests.Hand_written_result_for_mapped_requirement_conflicts`
- `ScopeValidationTests.Unlisted_is_required`
- `ScopeValidationTests.Target_audit_must_be_in_scope`
- `ScopeValidationTests.Json_output_has_code_file_line_and_hint`
- `NewAuditTests.Lists_every_in_group_requirement_with_title`

**Functional check**
- `hound scope validate examples/compliance-repo/scope/` passes, and a copy with one broken reference fails with exactly that problem.
- Result (2026-11-21): `hound scope validate examples/compliance-repo/scope --config examples/example-essential-eight.yaml` printed `scope valid: 1 domain(s), 2 control(s), 1 audit(s)` and exited 0. A copy naming the undeclared system `Example tablets` in the audit exited 3 with one problem, `scope.reference_unknown` at `audits/ex-e8-2026.yaml` line 17. Evidence: [docs/evidence/s06/s06-06.md](../evidence/s06/s06-06.md).

### s06-07 Scope plan
- [x] Done

**Story:** As a reviewer of a compliance pull request, I want to see exactly what the change will create or update in CISO Assistant, so that I can approve it with confidence.

**Acceptance criteria**
- `planScope(declared, actual)` is a pure function returning creates, updates (field by field, old and new), drift (objects in CISO Assistant that differ from or are missing in the scope) and, with `--prune`, deletes limited to objects `hound` manages.
- `hound scope plan` reads CISO Assistant read-only and prints the plan as Markdown (for a pull request comment) or JSON.
- Changes are ordered by dependency: frameworks, domains, perimeters, systems, audits, documents, controls, then assessments.
- Objects `hound` creates are marked as managed. Unmanaged objects are never in a delete.

**Tests first**
- `ScopePlanTests.New_domain_is_a_create`
- `ScopePlanTests.Changed_owner_is_a_field_update`
- `ScopePlanTests.Object_edited_in_ui_is_reported_as_drift`
- `ScopePlanTests.Prune_never_deletes_unmanaged_objects`
- Property: planning a scope against the result of applying its own plan is empty.

**Functional check**
- Against a freshly seeded local CISO Assistant, `hound scope plan` on the example scope lists the expected creates, and the Markdown renders in a pull request comment.
- Result (2026-11-21): in CI run [44918724134](https://github.com/tnbozman/evidence-hound/actions/runs/44918724134), `hound scope plan` on the example scope against the seeded CISO Assistant listed 6 creates (perimeter Corporate IT, three systems, two documents), 5 updates (the seeded domain, audit EX-E8-2026, AC-MFA, AC-BACKUP and the hand-written ISM-1486 row) and the seed's other audits, perimeter and control as drift, with GET requests only. The Markdown is a GitHub table, recorded in the evidence file. Evidence: [docs/evidence/s06/s06-07.md](../evidence/s06/s06-07.md).

### s06-08 Scope apply: domains, perimeters and systems
- [x] Done

**Story:** As a capability owner, I want `hound` to create and update the structure the scope declares, so that a new or rebuilt CISO Assistant matches the repo without any clicking.

**Acceptance criteria**
- `hound scope apply` carries out the plan for domains (under the root folder), perimeters and systems, in order. It is safe to run again, and refuses to write anything when the plan has validation problems.
- `--dry-run` prints the plan and writes nothing.
- Each write's result is reported. A failed write stops its dependants, such as a failed domain stopping its perimeters, but not unrelated objects.

**Tests first**
- Contract: `ScopeClientTests.Creates_folder_perimeter_and_asset` (recorded responses)
- `ScopeApplyTests.Failed_domain_skips_its_perimeters_only`
- `ScopeApplyTests.Second_apply_writes_nothing`

**Functional check**
- Apply the example scope to an empty local CISO Assistant, apply it again and confirm the second run writes nothing.
- Result (2026-11-21): in CI run [44923765514](https://github.com/tnbozman/evidence-hound/actions/runs/44923765514) the first apply wrote 5 changes (the domain, perimeter `Corporate IT` and three systems) with none failed, and the second apply wrote nothing. Owners are written as CISO Assistant actors. Evidence: [docs/evidence/s06/s06-08.md](../evidence/s06/s06-08.md).

### s06-09 Scope apply: frameworks, audits, documents, controls and assessments
- [x] Done

**Story:** As a capability owner, I want frameworks, audits and their documents, applied controls and hand-written assessments created from the scope, so that the whole assessment is reproducible and reviewable.

**Acceptance criteria**
- Before writing code, confirm that the REST API can upload a custom library file, and import a stored library and its mapping libraries by URN. Record the result under this task.
- Frameworks: built-in libraries are imported by URN; custom library files are uploaded. A library already loaded at an older version is reported, not upgraded silently.
- Audits: compliance assessments are created or updated by `ref_id` in their perimeter, with the framework and selected groups. Renaming an audit in the file renames it in CISO Assistant. A locked audit is never changed (`scope.audit_locked`).
- Documents: evidence items are created in the audit's domain and their files uploaded, matched by content hash so an unchanged file is never uploaded twice. They are linked to the requirement rows they cite, in every audit whose systems include one of theirs.
- Controls: applied controls are created or updated once each by `ref_id`, then linked to the requirement rows each audit cites, and to their documents. A control shared by an Essential Eight and an Essentials audit is one object in CISO Assistant.
- Assessments: hand-written results are written to their rows; `unlisted` is applied to the rest of the audit's in-group rows that no mapping computes.
- Links are added, never removed, unless `--prune` is given.
- The dev seed creates only the service account and token, then runs `hound scope apply` on the example scope. The functional tests therefore exercise apply on every pull request.

**Tests first**
- Contract: `ScopeClientTests.Uploads_custom_library` and `Imports_stored_library_by_urn`
- `ScopeApplyTests.Locked_audit_is_reported_not_changed`
- `ScopeApplyTests.Audit_created_with_selected_groups`
- `ScopeApplyTests.Document_links_to_rows_of_every_audit_sharing_a_system`
- `ScopeApplyTests.Unchanged_file_is_not_uploaded_again`
- `ScopeApplyTests.Unlisted_not_applicable_skips_rows_a_mapping_computes`
- `ScopePlanTests.Older_library_version_is_reported`

**Functional check**
- `make ca-up` seeds through `hound scope apply`, and the existing functional suite passes unchanged.

**Result (API check)**: a custom library uploads to `POST stored-libraries/upload/` (201) and a stored library, including a mapping library, loads by URN with `POST stored-libraries/{urn}/import/`. v4.3.4 also loads a library on upload, so a just-uploaded library is not imported again. Evidence: [docs/evidence/s06/s06-09.md](../evidence/s06/s06-09.md).

**Functional check result**: the functional suite seeds through `hound scope apply` and passes, 14 of 14 (CI run 44933838879). After the other tests' runs, `hound scope plan` shows 0 changes and no drift, and a second apply writes nothing.

### s06-10 Audit periods: open, close and sign off
- [x] Done

**Story:** As an authorising official, I want each audit to run in periods that close and open on a configured schedule, so that every period is a clean, signed-off record while daily runs carry on into the next one.

**Acceptance criteria**
- Builds on the answers in s06-05. If locking an audit also freezes its linked applied controls, a closed period is not locked while a control it shares with the open period exists; it stays in review and is marked closed by `hound` instead, and the guide says so.
- An audit file may declare a `cycle`: `period` (`monthly`, `quarterly`, `half_yearly` or `yearly`), `starts` (a date) and `close` (`sign_off`, the default, or `auto`). The file then describes a series; each period is its own audit with `ref_id` `<series>-<period>`, such as `EX-E8-BACKUP-2026Q4`.
- When a period starts, `hound` opens its audit from the previous period's audit as baseline, so results, evidence links and hand-written assessments carry over, and sets it in progress with its start and end dates.
- Targets name the series; each run publishes to the period that is open on the run's date.
- When a period ends, the last run publishes to it and it moves to in review. With `close: auto` it is locked at once. With `sign_off` it is locked when the audit file records the sign-off (`sign_offs: [{period: 2026Q4, by: ..., date: ...}]`) and `scope apply` runs, so the approval is itself a reviewed commit.
- `hound` never writes to an audit that is in review or locked. Roll-over is idempotent: a missed boundary is caught up by the next run or `scope apply`, opening the period that is due.
- A closed period's final run is kept by evidence retention for `evidence.period_retention_years` (default 7), however old.
- `scope plan` shows openings, closings and pending sign-offs.

**Tests first**
- `PeriodTests.Quarter_of_date_and_period_ref_id` (property: every date falls in exactly one period)
- `PeriodTests.Run_publishes_to_the_open_period`
- `RollTests.New_period_opens_from_previous_as_baseline`
- `RollTests.Ended_period_moves_to_in_review_and_is_not_written_again`
- `RollTests.Sign_off_in_file_locks_the_period`
- `RollTests.Missed_boundary_is_caught_up_once`
- `RetentionTests.Closed_period_final_run_is_kept`

**Functional check**
- With a fixed clock, run across a quarter boundary against the local CISO Assistant: the old period goes to in review with its final results, the new one opens with them carried over, the next run writes only to the new one, and adding a sign-off and running `scope apply` locks the old one.
- Result (2026-11-21): `AuditPeriodsFunctionalTests` ran the series `EX-PERIOD` (quarterly from 2026-07-01) against v4.3.4 with `HOUND_TODAY` in the functional CI job (run [44947813409](https://github.com/tnbozman/evidence-hound/actions/runs/44947813409)). On 2026-09-30 apply created `EX-PERIOD-2026Q3` and publish wrote 5 rows to it; on 2026-10-01 the plan listed "EX-PERIOD-2026Q3 closes: in review, awaiting sign-off" and "EX-PERIOD-2026Q4 opens, from EX-PERIOD-2026Q3", apply did both, Q4 carried Q3's results, and the next publish wrote only to Q4 while Q3's rows stayed unchanged; on 2026-10-15, with the sign-off added, apply set Q3 to done and locked it. Locking does not freeze controls (s06-05), so no fallback was needed. Evidence: [docs/evidence/s06/s06-10.md](../evidence/s06/s06-10.md).

### s06-11 Export an existing instance
- [x] Done

**Story:** As an organisation that set up CISO Assistant by hand, I want to export what is there into scope files, so that I can move to managing CISO Assistant as configuration without retyping everything.

**Acceptance criteria**
- `hound scope export --out <folder>` writes `setup.yaml` (frameworks, domains, perimeters, systems) and one audit file per compliance assessment, with its documents, applied controls and hand-written assessments. Output is sorted and formatted deterministically, so a second export of an unchanged instance gives no diff.
- Objects without a `ref_id` are given one in the export, written back to CISO Assistant only on the next `scope apply`.
- Running `hound scope plan` on a fresh export reports no changes.
- Results computed from checks are not exported, and nor is collector evidence; collectors and manual sources rebuild them. Document files are downloaded only with `--with-files`.

**Tests first**
- `ScopeExportTests.Export_then_plan_is_empty` (from recorded responses)
- `ScopeExportTests.Output_is_sorted_and_stable`

**Functional check**
- Export the seeded local CISO Assistant, then plan against it: no changes.
- Result (2026-11-21): `ScopeExportFunctionalTests` exported the seeded v4.3.4 instance with `--with-files` in the functional CI job (run [44953205117](https://github.com/tnbozman/evidence-hound/actions/runs/44953205117)): five audits, the controls and documents with their files, and the hand-written results. `scope validate` passed, `scope plan` on the export listed no changes, and a second export was identical. The first run found one period audit planned as "mark as managed", fixed by leaving an audit's status and lock out of its recorded state. Evidence: [docs/evidence/s06/s06-11.md](../evidence/s06/s06-11.md).

### s06-12 Compliance repo template and guide
- [x] Done

**Story:** As a capability owner working with an AI assistant, I want a ready-made private compliance repo layout, with instructions for the assistant, so that the assistant proposes valid changes and I review them as pull requests.

**Acceptance criteria**
- `examples/compliance-repo/` is a fictional Example Pty Ltd repo with:
  - the deployment config, scope files, mappings, framework snapshots and manual source folders, each schema-linked with `# yaml-language-server` comments;
  - a `CLAUDE.md` telling an assistant the layout, to run `hound scope validate --format json` and `hound mapping validate` after every change, never to write secrets or real evidence into the repo, and to treat `scope plan` output as the summary for the pull request;
  - a CI workflow that validates on every pull request, posts `scope plan` as a comment, and runs `scope apply` on merge behind a protected environment with required reviewers.
- `docs/scope.md` documents the scope file, plan and apply, drift, pruning, export, audit periods and sign-off, and the review workflow.
- `docs/configuration.md` links to it and documents `targets`.

**Tests first**
- `ExampleRepoTests.Example_compliance_repo_validates` (runs scope and mapping validation on the example)
- Docs lint and link check.

**Functional check**
- Following `docs/scope.md` verbatim: copy the example repo, change a system's owner, and confirm that validate passes, plan shows one field update, and apply makes it.
- Result (2026-11-21): `ReviewWorkflowFunctionalTests` in the functional CI job (run [44956715748](https://github.com/tnbozman/evidence-hound/actions/runs/44956715748)): validate passed, plan showed `0 to create, 1 to update, 0 to delete.` with the owner change on "Example laptops", apply wrote 1, and a second plan showed no changes. The first run planned 5 updates because other tests had published results the example scope does not declare; the check now starts from the applied example scope. Evidence: [docs/evidence/s06/s06-12.md](../evidence/s06/s06-12.md).

## Discovered work

- Over Kafka, `hound run` has no current state to compare with, so it sends every row (as before s06-04) and reports no drift. A requirement mapped with `rows: all` is not supported over Kafka either, because the dispatcher addresses one row. (Found in s06-04.)
- Tests that PATCH CISO Assistant must send a sized body: .NET's `PatchAsJsonAsync` streams a chunked body that reaches Django empty, so the call succeeds and changes nothing. The client already uses `StringContent`. (Found in s06-04.)
- A deployment target's problems in `hound scope validate --config` name the config file but no line, because the config loader keeps no line numbers for targets. (Found in s06-06.)
- The scope plan does not yet compare a system's `tags`, an audit's `field_visibility` or a document's files: tags need CISO Assistant's filtering labels, field visibility its stored shape, and files the evidence hashes (s06-09). They are not reported as changes until then. (Found in s06-07.)
- The run publisher replaces a row's `evidences` with what its mapping computes and reports other evidence as drift, so documents the scope links to those rows (s06-09) would be undone on the next run. s06-09 must make the publisher keep scope-linked documents. (Found in s06-08. Done in s06-09.)
- `hound scope apply` does not clear a field the scope removes (such as a control's ETA): it sends only the fields that have values. (Found in s06-09.)
- The first apply of an audit writes each row it touches with its own PATCH (about 200 for the example scope). Fine for now; a batch endpoint would make it faster. (Found in s06-09.)
- The scope plan still does not compare a system's `tags` or an audit's `field_visibility` (see s06-07); document files are now compared by hash. (Found in s06-09.)
- Cycle periods are named `month`, `quarter`, `half_year` and `year`, not `monthly` and so on as the s06-10 story says, to match the period labels. (Found in s06-10.)
- CISO Assistant v4.3.4 audits have no start or end dates, so a period's dates show only in its `ref_id`, name and ETA (the period's last day). (Found in s06-10.)
- Only the change-only REST run checks that a target audit is closed (`plan.audit_closed`). `hound publish` and runs over Kafka send to whatever the target names; with a series target that is always the open period, but an explicit period `ref_id` of a closed period would be refused only by CISO Assistant's lock, and an in-review one would be written. (Found in s06-10.)
- The scope plan labels an audit "awaiting sign-off" for every ended period still in review, however old; it does not yet warn when a sign-off is overdue. (Found in s06-10.)
- `hound publish` writes results without the run marker that `hound run` adds (s06-04), so `scope export` cannot tell rows it wrote from hand-written ones and exports them as `assessments`. Give `publish` the marker too. (Found in s06-11.)
- `scope export` does not export a control's status or reference control, a system's tags, an audit's field visibility or cycle, or libraries no audit uses; an exported period audit is a plain audit. (Found in s06-11.)
- `scope apply` resets results that `hound publish` wrote to rows the scope leaves `unlisted: not_assessed`, because those rows carry no run marker (see the s06-11 note). Rows written by `hound run` are kept. Giving `publish` the marker fixes both. (Found in s06-12.)
- The organisation's own record (CISO Assistant's main entity, used by third-party risk features) is not managed by the scope yet; the scope uses the root folder. Add it once the REST API is confirmed to update it. (Found planning s06.)

## Sprint review

**2026-11-21.** All twelve tasks shipped with red and green commits and per-task evidence in `docs/evidence/s06/`. Each functional check ran against CISO Assistant v4.3.4 in CI.

- **Publishing by URN to several audits.** Requirement rows are addressed by URN and scoped to an audit's groups. One run feeds several audits (the Essential Eight and the ASD Essentials). Only rows whose state changed are written, each traced to its run by a marker.
- **CISO Assistant as configuration.** `hound scope validate`, `plan`, `apply` and `export` manage domains, perimeters, systems, frameworks and libraries, audits, documents with files, applied controls and hand-written assessments from YAML. Validation is offline against committed framework snapshots. Plans show drift from UI changes and never delete without `--prune`. An export of an instance plans back to no changes.
- **Audit periods.** A `cycle` turns an audit file into a series of periods. Each opens from the previous one as baseline, moves to review when it ends and is locked on sign-off. Runs publish to the open period and never write a closed one. Each closed period's final run is kept for seven years.
- **A compliance repo template** with an assistant guide and a pull request workflow (validate, plan as a comment, apply behind a protected environment).

754 unit and contract tests, the script tests, 17 functional tests (CI) and lint pass.

What slipped: nothing in scope. Follow-ups are under Discovered work, notably the run marker for `hound publish`, a system's tags and an audit's field visibility in plans, and overdue sign-off warnings.

What the feedback taught us:
- Read the API before writing tests. The s06-05 inventory, run in CI against the real container, found that owners are actor IDs, that chunked bodies reach Django empty, that uploading a library also loads it, and that audits have no dates. Each would otherwise have been a wrong test.
- State that only some objects declare (links, an audit's status and lock) must stay out of the recorded marker state, or every object that does not declare it plans as an update. The functional checks found both cases. The unit tests had not.
- Functional tests share one instance, so each check must start from a state it sets itself rather than from what earlier tests left behind.
