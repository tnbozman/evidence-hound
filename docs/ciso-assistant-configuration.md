# CISO Assistant as configuration

What in CISO Assistant is configuration that `hound` can declare in files, what is runtime data, and how each piece behaves through the REST API. Checked against CISO Assistant **v4.3.4**: its OpenAPI schema (`/api/schema/`, 1,805 paths), its source, and a probe script run against a local instance in CI.

- Probe: [`scripts/ca-inventory.sh`](../scripts/ca-inventory.sh). `HOUND_CA_TOKEN=… scripts/ca-inventory.sh <api-url> <out-dir>` saves the full schema (`openapi.json`), a snapshot of the resources below (`openapi-paths.json`) and the answers (`answers.json`). It creates `EX-PROBE-*` objects and deletes them afterwards, except the probe library. Run it against a throwaway instance.
- Snapshot: [`tests/fixtures/ciso-assistant/openapi-v4.3.4.json`](../tests/fixtures/ciso-assistant/openapi-v4.3.4.json) holds every operation on the resources `hound` calls or s06 configures (347 paths). `OpenApiSnapshotTests` fails when an endpoint `hound` calls (`ApiEndpoints.Used`) is missing from it. To upgrade CISO Assistant, run the probe against the new release, replace the snapshot and fix what fails.

## Conventions that hold for every object

- **Stable key.** Most objects have a `ref_id`. CISO Assistant fills a blank `ref_id` automatically, so `hound` always sends its own and finds objects by it (list filter `?ref_id=` or search). Library content (frameworks, requirement nodes, reference controls, risk matrices, threats) is keyed by URN instead.
- **Endpoints.** Collections are `/api/<resource>/` (`GET` list, `POST` create) and items are `/api/<resource>/{id}/` (`GET`, `PUT`, `PATCH`, `DELETE`), with `{id}` the UUID. `/api/<resource>/{id}/object/` returns the read serializer with names rather than IDs.
- **Writes `hound` is not allowed to make are not always errors.** On a requirement assessment, a field that the audit's `field_visibility` makes read-only for the auditor is dropped from the write and the request still returns 200 (see Q4). `hound` must read back what it wrote rather than trust the status code.
- **Sized request bodies.** Send JSON with a Content-Length. A chunked body reaches CISO Assistant empty, so a PATCH returns 200 and changes nothing.

## Managed in this sprint (s06)

| Object | Kind | Endpoint | Stable key | Create | Update | Lock | Delete |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Library (framework, mapping set, reference controls, risk matrix) | Configuration | `/api/stored-libraries/upload/` (`POST`, raw file body), then `/api/stored-libraries/{id}/import/`; loaded ones under `/api/loaded-libraries/` | Library URN (and version) | Upload stores it; import loads it | A newer version is offered by `/api/loaded-libraries/{id}/update/` | None | `/api/stored-libraries/{id}/unload/`, `DELETE` on the loaded library; refused while audits use it |
| Framework | Configuration (from a library) | `/api/frameworks/` | URN | Only through a library | Only through a library update | None | With its library |
| Domain (folder) | Configuration | `/api/folders/` | Name under its parent folder (no `ref_id`) | `POST` with `parent_folder` and `content_type: DO` | `PATCH` | None | `DELETE` deletes everything inside it |
| Perimeter | Configuration | `/api/perimeters/` | `ref_id` | `POST` with `folder` | `PATCH` | None | `DELETE` |
| Asset (system) | Configuration | `/api/assets/` | `ref_id` | `POST` with `folder`, `type` `PR` or `SP` | `PATCH`, including `parent_assets` | None | `DELETE` |
| Applied control | Configuration (status is runtime, set by collectors) | `/api/applied-controls/` | `ref_id` | `POST` | `PATCH` | Not frozen by an audit lock (Q1) | `DELETE` |
| Audit (compliance assessment) | Configuration; its rows are runtime | `/api/compliance-assessments/` | `ref_id` | `POST` with `framework`, `perimeter`, `selected_implementation_groups`; `baseline` copies another audit's results (Q3) | `PATCH` | `is_locked: true` refuses every other change to the audit and its rows (Q1) | `DELETE` |
| Requirement assessment (row) | Runtime, created with its audit | `/api/requirement-assessments/` | Audit `ref_id` + requirement URN (or ref) | Never: created with the audit | `PATCH` `result`, `observation`, `evidences`, `applied_controls`; read-only fields dropped silently (Q4) | Refused while the audit is locked | With the audit |
| Evidence | Runtime (documents are configuration) | `/api/evidences/` | `ref_id` | `POST` | `PATCH`; a new file is a new revision | None | `DELETE` |
| Evidence revision | Runtime | `/api/evidence-revisions/` | Evidence + `version` | `POST` with the file; CISO Assistant stores its SHA-256 (Q2) | None | None | `DELETE` |

Audit fields `hound` writes (from the `ComplianceAssessmentWrite` schema): `ref_id`, `eta`, `due_date`, `authors`, `reviewers`, `field_visibility`, `is_locked`, `baseline`, `status`. Audit `status` takes `planned`, `in_progress`, `in_review`, `done` and `deprecated` (`Assessment.Status`; the schema names its enum by a number, so read it from the source). There are no separate start and end date fields: `eta` is the planned completion and `due_date` the deadline, so an audit period's dates belong in `hound`'s own files (s06-10).

## Recorded for later sprints

| Object | Sprint | Endpoint | Stable key | Notes |
| --- | --- | --- | --- | --- |
| Risk matrix | s09 | `/api/risk-matrices/` | URN | Comes from a library |
| Threat | s09 | `/api/threats/` | URN or `ref_id` | Library or custom |
| Risk study (risk assessment) | s09 | `/api/risk-assessments/` | `ref_id` | Uses a risk matrix and a perimeter |
| Risk scenario | s09 | `/api/risk-scenarios/` | `ref_id` within its study | |
| Risk acceptance | s09 | `/api/risk-acceptances/` | `ref_id` | |
| Security exception | s09 | `/api/security-exceptions/` | `ref_id` | Linked from requirement assessments |
| Business impact analysis | s09 | under `/api/resilience/` | `ref_id` (Q6) | Feature flag `bia` |
| Asset security and recovery objectives | s09 | `/api/assets/` | Asset `ref_id` | Fields on the asset |
| Entity, including the main entity | s10 | `/api/entities/` | `ref_id`; the main entity is `builtin: true` | The main entity can be read and updated through the API (Q5) |
| Solution, contract, representative | s10 | `/api/solutions/`, `/api/contracts/`, `/api/representatives/` | `ref_id` (representatives: email) | Feature flag `tprm` (and `contracts`) |
| Entity assessment | s10 | `/api/entity-assessments/` | `ref_id` (Q6) | No revision endpoint (Q6) |
| Tier | s10 | `/api/tiers/` | Name | |
| Metric definition, metric instance, sample, dashboard | s10 | under `/api/metrology/` | Definitions by URN; instances by `ref_id` (Q6) | Feature flag `metrology` |
| Users, teams, role assignments | Later | `/api/users/`, `/api/teams/`, `/api/user-groups/`, `/api/role-assignments/` | User email; team and group name | |
| Feature flags | Later | `/api/settings/feature-flags/` | Flag name | `GET` and `PATCH` work (Q5) |
| General settings (such as the default score scale) | Later | `/api/settings/general/` | Setting name | `GET`, `PUT`, `PATCH` |
| Task definitions | Later | `/api/task-templates/` | Name | |
| Document templates and managed documents | Later | Document management routes (feature flag `policy_documents`) | Name | Templates import as a zip of Markdown files with frontmatter |
| Webhooks | Later | Automation routes (feature flag `outgoing_webhooks`) | Name | |

## Out of scope

Workflows, quick forms, campaigns, EBIOS RM studies and quantitative risk studies. They are authored in CISO Assistant, or imported through libraries and the Data Wizard (`/api/data-wizard/`). `hound` neither writes nor exports them.

## Answers

Each answer comes from `answers.json`, produced by `scripts/ca-inventory.sh` against v4.3.4 in the functional CI job (run [44906131552](https://github.com/tnbozman/evidence-hound/actions/runs/44906131552), step "Show the CISO Assistant inventory").

### Q1. Does locking an audit also freeze the applied controls linked to it? (s06-10)
No. With a locked audit (`PATCH is_locked: true` → 200):
- `PATCH` of a linked applied control's status → **200**, and the status changes to `active`.
- `PATCH` of a row → **400** "Cannot modify the requirement when the audit is locked."
- `PATCH` of another audit field → **400** "Cannot modify the audit attributes when it is locked. Only the 'Locked' field can be modified."

So closing a period must record the controls' state itself; the lock alone does not.

### Q2. Does replacing an evidence file create a revision with its SHA-256 hash, and can the API read it? (s06-04)
Yes. Two uploads to one evidence item → **201, 201**, and `GET /api/evidence-revisions/?evidence=<id>` lists versions 2 and 1, each with `attachment_hash` equal to the file's SHA-256 (`97b413bc…`). `hound` compares that hash before uploading.

### Q3. Which audit fields hold status, start and end dates, and how is an audit created from a baseline? (s06-10)
The write fields are `status`, `eta` and `due_date` (plus `ref_id`, `authors`, `reviewers`, `field_visibility`, `is_locked`, `baseline`); there are no start or end date fields. `POST /api/compliance-assessments/` with `baseline: <audit id>` → **201**, and the compliant row of the baseline was copied to the new audit (1 of 1).

### Q4. Are writes to a field the service account's role can only read ignored silently? (s06-06)
Yes. On an audit whose `field_visibility` makes `observation` read-only for the auditor, `PATCH` of a row with `observation` and `result` → **200**; afterwards `result` is `compliant` (written) and `observation` is empty (dropped). The scope plan must warn about such fields rather than expect an error.

### Q5. Can the API upload a custom library, update the main entity, and set feature flags? (s06-09, s10-01)
- Feature flags: `GET /api/settings/feature-flags/` → 200 and `PATCH` → **200**. 62 flags, including `tprm`, `metrology`, `bia`, `policy_documents` and `outgoing_webhooks`.
- Main entity: `GET /api/entities/?builtin=true` finds it, and `PATCH` → **200**.
- Custom library: the only upload path is `POST /api/stored-libraries/upload/`, which uses DRF's `FileUploadParser`: the body is the raw YAML file, named by a `Content-Disposition: attachment; filename=…` header. Sent that way, the probe library uploads → **201** and is returned as a stored library with its URN (run [44907514706](https://github.com/tnbozman/evidence-hound/actions/runs/44907514706)). A multipart form is refused with 400 "Missing filename. Request should include a Content-Disposition header with a filename parameter."

### Q6. Do BIAs, entity assessments and metric instances have a `ref_id`, and can entity assessments be revised through the API? (s09-04, s10-02)
All three have `ref_id` in both their read and write schemas (`BusinessImpactAnalysis*`, `EntityAssessment*`, `MetricInstance*`). No path in the schema matches an entity assessment revision, and the tprm models have no revision model, so a new assessment period is a new entity assessment with its own `ref_id`.
