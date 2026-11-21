# Scope files

A compliance repo declares the organisation's structure in CISO Assistant as files, so it changes through reviewed pull requests rather than clicks. `hound scope validate` checks the files offline, `hound scope plan` shows what they would change in CISO Assistant, and `hound scope apply` writes them.

A complete fictional example is in [`examples/compliance-repo/scope/`](../examples/compliance-repo/scope/). The schema is [`schemas/scope.schema.json`](../schemas/scope.schema.json); add `# yaml-language-server: $schema=…` to the top of each file for editor help.

```
compliance-repo/
  scope/
    setup.yaml          organisation, frameworks, other libraries, domains with their perimeters and systems
    controls.yaml       applied controls, each declared once
    audits/
      ex-e8-2026.yaml   one file per audit (compliance assessment)
  documents/            the files that documents name, relative to the compliance repo (the scope folder's parent)
  frameworks/           custom library files, if any
```

The local CISO Assistant (`make ca-up`, the dev container and the functional tests) is seeded by applying this example scope, so every pull request exercises `hound scope apply`.

## setup.yaml

| Key | Meaning |
| --- | --- |
| `organisation.root_folder` | The existing folder the domains sit under. Default `Global`. |
| `frameworks` | Each framework to load: `urn` (the framework URN), and `library` (a library URN from CISO Assistant's stored libraries) or `file` (a custom library file). `snapshot` names the framework snapshot made by `hound framework snapshot`, so groups and requirements can be checked offline; Paths are relative to setup.yaml. |
| `libraries` | Other libraries to load by URN, such as the mapping set between the Essential Eight and the Essentials. |
| `domains` | Folders. Each has a `name`, a `description`, its `perimeters` (or a single `perimeter`) and its `systems`. |
| perimeter | `name`, optional `ref_id` and `description`. |
| system | `name`, `type` (`primary` or `supporting`), optional `ref_id`, `description`, `owner`, `parents` (other systems by name) and `tags` (per-framework labels such as `essentials_domain: identity` or `cui: "true"`). |

## controls.yaml

`controls` lists each applied control once, because CISO Assistant treats an applied control as a reusable object serving many requirements across frameworks: `ref_id` (the stable key), `name`, and optionally `description`, `domain` (left out for the global domain), `owner`, `status` (`to_do`, `in_progress`, `on_hold`, `active`, `degraded`, `deprecated`), `priority` (`"1"` to `"4"`), `category`, `eta`, `systems`, `reference_control` (a URN) and `documents` (ids of documents declared in audit files).

## audits/&lt;audit&gt;.yaml

| Key | Meaning |
| --- | --- |
| `ref_id`, `name` | The audit's stable key and title. |
| `framework`, `groups` | The framework URN and the implementation groups it assesses, such as `["1", "2"]` for Essential Eight ML2. |
| `domain`, `perimeter` | Where the audit sits. |
| `eta`, `due_date`, `authors`, `reviewers` | Optional audit fields (dates as `YYYY-MM-DD`). |
| `field_visibility` | Optional, per field: `{auditor: edit|read|hidden, respondent: …}`. `result`, `observation` and `evidences` must stay `edit` for the auditor, because CISO Assistant silently ignores writes to fields the writer can only read. |
| `systems` | The systems in scope. |
| `documents` | Static evidence such as policies and reports: `id`, `name`, `files` (paths or globs, relative to the compliance repo, the scope folder's parent), `systems` and `requirements` it supports. A document is linked to the rows it cites in its own audit, and in every other audit of the same framework that shares one of its systems. |
| `controls` | The applied controls this audit relies on, by `ref_id`, each with the `requirements` it supports in this framework. |
| `assessments` | Hand-written results for requirements no check covers: `ref` (or `urn`), `status` (`to_do`, `in_progress`, `in_review`, `done`), `result` and `observation`. |
| `cycle`, `sign_offs` | Optional, for an audit that runs in periods (s06-10): `cycle: {period: month|quarter|half_year|year, starts: YYYY-MM-DD}`, and `sign_offs: [{role, name}]`. |
| `unlisted` | Required. What happens to in-group requirements the file does not list: `{result: not_assessed}`, or `{result: not_applicable, reason: …}` for an audit scoped to one system. Nothing is marked not applicable silently. It is applied only with `--config`, because the deployment's mappings say which rows a run computes, and those are left alone. |

A requirement is named by its ref_id, or by its URN when the value starts with `urn:` (needed when a framework repeats a ref).

Start an audit file with every in-group requirement listed, titled in a comment, to do and not assessed:

```sh
hound scope new-audit --framework frameworks/essential-eight-2023.json --groups 1,2 --ref-id EX-E8-2026 --out scope/audits/ex-e8-2026.yaml
```

## Validate

```sh
hound scope validate scope/ [--config hound.yaml] [--format json]
```

Every problem is reported at once with its file, line, code and a hint, and the exit code is 3 when there are any. `--format json` prints them as a list of `{code, file, line, message, hint}` for tools. With `--config`, each deployment target must name an audit and controls the scope declares.

| Code | Problem |
| --- | --- |
| `scope.yaml_invalid`, `scope.document_not_a_map` | The file is not a YAML map. |
| `scope.key_unknown`, `scope.key_duplicate`, `scope.key_missing`, `scope.value_invalid` | A key is not known, appears twice, is missing, or has a value of the wrong shape. |
| `scope.file_missing` | The scope folder has no setup.yaml. |
| `scope.duplicate` | Two domains, systems, perimeters of one domain, controls, audits, or documents of one audit share a name. |
| `scope.reference_unknown` | A domain, perimeter, system, framework, document or control is used but not declared. |
| `scope.framework_snapshot_missing`, `scope.framework_file_missing`, `scope.framework_snapshot_mismatch` | A framework cannot be checked offline: no snapshot, a missing file, or a snapshot of another framework. |
| `scope.group_unknown` | An audit selects a group its framework does not define. |
| `scope.requirement_unknown` | A requirement is not in the framework. |
| `scope.requirement_not_assessed` | A document, control or assessment cites a requirement outside the audit's groups. |
| `scope.result_conflict` | A hand-written result for a requirement a target's mapping also computes, so a person and a collector would write the same row. |
| `scope.field_not_writable` | `field_visibility` leaves `result`, `observation` or `evidences` below `edit` for the auditor. |
| `scope.unlisted_missing`, `scope.unlisted_reason_missing` | `unlisted` is missing, or marks requirements not applicable without a reason. |
| `config.target_not_in_scope` | A deployment target names an audit or control the scope does not declare. |

## Plan

```sh
HOUND_CA_URL=http://localhost:8000/api/ HOUND_CA_TOKEN=… hound scope plan scope/ [--config hound.yaml] [--format markdown|json] [--prune]
```

The plan validates the scope first and stops, reading nothing, when there are problems. Otherwise it reads CISO Assistant (GET requests only) and prints what applying the scope would do, as Markdown for a pull request comment or as JSON for tools. It writes nothing.

- **Create**: objects the scope declares that CISO Assistant does not hold, with their fields.
- **Update**: objects whose fields differ, field by field (old → new). An object that exists but was not made by `hound` is taken over: "mark as managed".
- **Delete**, only with `--prune`: objects `hound` manages that the scope no longer declares. Objects made by hand are never deleted.
- **Drift**: objects changed in CISO Assistant since `hound` last wrote them (they are written back), objects under the root folder that the scope does not declare, an audit that is locked in CISO Assistant (it and its rows are never changed), and a framework whose loaded library is older than the snapshot's (it is reported, not upgraded).

Changes come in dependency order: frameworks, domains, perimeters, systems, audits, documents, controls, then assessments. Deletes come last, in reverse.

How objects are matched: a framework by URN, a domain and a system by name, a perimeter by domain and name, an audit and a control by `ref_id`, a document by its id (the evidence's `ref_id`), and a hand-written assessment by audit and requirement. Only objects in the root folder and the domains directly under it are read.

`hound` marks the objects it manages by ending their description with `[hound scope <state>]`, a fingerprint of the fields it wrote, other than links. Links are compared one by one instead, because runs add their own, such as a collector's health evidence on a control. That is how the plan tells an object edited in CISO Assistant (drift) from one the scope changed (an update). Frameworks and assessments carry no marker.

What the plan compares: a domain's description; a perimeter's name, `ref_id`, description and domain; a system's `ref_id`, type, description, owner, domain and parents; an audit's name, framework, groups, domain, perimeter, ETA, due date, authors and reviewers; a document's name, domain and file hashes; a control's name, description, domain, owner, priority, category, ETA, systems and documents; an assessment's status, result and observation; a row's applied controls and documents. A control's `status` and `reference_control` and a framework's library are written only when the object is created, because collectors and CISO Assistant own them afterwards.

Links are added, never removed: a control's systems and documents, an audit row's applied controls and documents, and a document's files keep what CISO Assistant already holds, unless `--prune` is given.

## Apply

```sh
HOUND_CA_URL=http://localhost:8000/api/ HOUND_CA_TOKEN=… hound scope apply scope/ [--config hound.yaml] [--dry-run] [--prune]
```

Apply validates the scope, plans it as above, and writes the changes in order. It is safe to run again: a second apply with nothing changed writes nothing. With `--dry-run` it prints the plan and writes nothing. When the scope has problems, it reports them and writes nothing.

It writes every kind:

- **Frameworks**: a library from CISO Assistant's stored libraries is imported by URN; a custom library `file` is uploaded first. `libraries` are imported the same way.
- **Domains** as folders under the root folder, **perimeters**, and **systems** as assets.
- **Audits** as compliance assessments, by `ref_id`, with their framework, perimeter, selected groups, dates, authors and reviewers. Renaming an audit in the file renames it.
- **Documents** as evidence in the audit's domain. Each file is uploaded as a revision only when no revision holds its SHA-256, so an unchanged file is never uploaded twice.
- **Controls** as applied controls, once each, with their systems and documents.
- **Rows**: each audit row the scope cites gets its controls and documents linked, hand-written assessments get their status, result and observation, and with `--config` the `unlisted` result goes to the in-group rows that neither the file lists nor a mapping computes.

`hound run` keeps the documents the scope linked to a row when it writes that row's evidence.

The report is JSON: how many changes were `written`, `failed` and `skipped`, and each change with its outcome. A change that depends on one that failed is skipped, such as a domain's perimeters and systems when the domain could not be written, or a system whose parent could not be; other changes go on. The exit code is 4 when any write failed.

Names are turned into IDs before anything is sent: the domain's folder, parent systems by name, frameworks and perimeters, and owners, authors and reviewers by user email (as CISO Assistant actors). A name that cannot be found fails that write and sends nothing for it.
