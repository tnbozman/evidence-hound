# Scope files

A compliance repo declares the organisation's structure in CISO Assistant as files, so it changes through reviewed pull requests rather than clicks. `hound scope validate` checks the files offline, `hound scope plan` shows what they would change in CISO Assistant, and `hound scope apply` writes them.

A complete fictional compliance repo is in [`examples/compliance-repo/`](../examples/compliance-repo/): copy it to a new private repository to start your own (see [The review workflow](#the-review-workflow)). The schema is [`schemas/scope.schema.json`](../schemas/scope.schema.json); add `# yaml-language-server: $schema=…` to the top of each file for editor help.

```
compliance-repo/
  hound.yaml            the deployment config (docs/configuration.md)
  scope/
    setup.yaml          organisation, frameworks, other libraries, domains with their perimeters and systems
    controls.yaml       applied controls, each declared once
    audits/
      ex-e8-2026.yaml   one file per audit (compliance assessment)
    access.yaml         users with their roles, and teams (optional)
    settings.yaml       feature flags and general settings (optional)
  mappings/             which checks satisfy which requirements (docs/mapping-files.md)
  frameworks/           framework snapshots and custom library files
  documents/            the files that documents name, relative to the compliance repo (the scope folder's parent)
  manual-sources/       the hand-exported sources; their exports go outside the repo
  CLAUDE.md             instructions for an AI assistant
  .github/workflows/compliance.yml
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

## access.yaml

Who can do what in CISO Assistant, so access changes are reviewed as pull requests and a rebuilt instance gets the same access. The file is optional; without it `hound` neither reads nor changes users.

- `users`: `email` (the key, compared without case), `first_name`, `last_name`, `active` (default `true`), and `service_account` for an account that is not a person: `publish` (or `true`) for the collector runner, `apply` for an account that applies the scope.
- `teams`: `name`, `description`, `domain` (left out for the root folder), `leader` and `members` by email.
- `roles`: a `role` on a `domain` (or the root folder, `Global`, for the whole organisation), for `users` and for every leader and member of `teams`. Built-in roles are `administrator`, `domain_manager`, `analyst`, `approver`, `auditor` (CISO Assistant calls it Reader) and `baseline_reader`; list user groups you created in CISO Assistant under `custom_roles` to use them as roles.

`hound` never sets or reads a password and never creates an API token: a key such as `password` or `api_token` anywhere in the file is an error (`scope.secret_in_file`) whose message never shows the value. A new user gets CISO Assistant's own invitation and sets their password there. Validation also reports duplicate or malformed emails, unknown roles, domains, users and teams, and any domain without an active user who can approve (an approver, domain manager or administrator on it or on `Global`), because risk acceptances and period sign-offs need one.

CISO Assistant gives a role on a domain through membership of that domain's built-in user group for the role ("Example Pty Ltd - Approver"); its API lets no one create role assignments directly. So each user's roles, given directly and through teams, become one field of the user, such as `roles: BI-RL-ADM on Global, BI-RL-APP on Example Pty Ltd`, and `scope apply` sets the user's groups to exactly those. A role removed from the file is taken away on the next apply, and the plan shows it as an update of the user (`roles: … → …`). A custom role is a user group you made in CISO Assistant on that domain, named as `custom_roles` lists it. A user removed from the file stays in CISO Assistant: `scope apply --prune` deactivates them (never deletes, so their history stays attributable) and leaves their groups as they were. Users the file does not list keep their groups.

`scope plan` ends with an **Access review**: active people who have not turned on multi-factor authentication, and each role a `publish` service account holds beyond analyst, however it was given (`runner-token.sh` starts the runner's account in the administrators group; declare it here to narrow it). CISO Assistant requires MFA for everyone or no one: set `enforce_mfa: true` in [settings.yaml](#settingsyaml) and each person turns it on at their next sign-in. Its API reports who has (`has_mfa_enabled`) but cannot turn it on for a person, so the access review lists anyone who has not yet.

## settings.yaml

CISO Assistant's feature flags (which modules people see) and general settings (instance-wide behaviour such as `enforce_mfa`), by name. The file is optional, and only the settings it names change: `hound` never resets a setting the file leaves out.

```yaml
feature_flags:
  tprm: true       # third-party records
  dora: false      # an EU regulation the organisation is not subject to
general:
  enforce_mfa: true
```

- `feature_flags`: each flag `true` or `false`.
- `general`: each setting's value as CISO Assistant stores it: a boolean, a number, text or a list.

Names are checked offline against the settings snapshot of the CISO Assistant release the stack pins ([`ciso-assistant-settings.json`](../src/EvidenceHound.Core/Scope/ciso-assistant-settings.json), with each flag's default); an unknown name is an error (`settings.flag_unknown`, `settings.setting_unknown`) whose hint lists the valid ones. A secret setting such as `openai_api_key` is an error (`scope.secret_in_file`): set it in CISO Assistant itself. Turning off a module the scope still uses is an error too: `compliance: false` would hide the audits the scope declares (`settings.feature_disabled_in_use`). Upgrading CISO Assistant means refreshing the snapshot, and the functional tests compare it with the pinned release.

`scope plan` lists each named setting whose value differs (`| Update | feature_flag | tprm | value: true → false |`). `scope plan --show-unmanaged` adds an **Unmanaged settings** section: every flag and general setting the file does not name, with its current value, to help decide what to manage. Settings carry no marker and are never deleted, so `--prune` leaves them alone.

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
| `cycle`, `sign_offs` | Optional, for an audit that runs in periods: `cycle: {period: month|quarter|half_year|year, starts: YYYY-MM-DD, close: sign_off|auto}` and `sign_offs: [{period, by, date}]`. See [Audit periods](#audit-periods). |
| `unlisted` | Required. What happens to in-group requirements the file does not list: `{result: not_assessed}`, or `{result: not_applicable, reason: …}` for an audit scoped to one system. Nothing is marked not applicable silently. It is applied only with `--config`, because the deployment's mappings say which rows a run computes, and those are left alone. |

A requirement is named by its ref_id, or by its URN when the value starts with `urn:` (needed when a framework repeats a ref).

Start an audit file with every in-group requirement listed, titled in a comment, to do and not assessed:

```sh
hound scope new-audit --framework frameworks/essential-eight-2023.json --groups 1,2 --ref-id EX-E8-2026 --out scope/audits/ex-e8-2026.yaml
```

## Audit periods

An audit file with a `cycle` describes a series rather than one audit. Each period is its own audit, with `ref_id` `<series>-<label>`:

```yaml
ref_id: EX-E8-BACKUP
name: Backups
cycle: {period: quarter, starts: "2026-07-01"}   # close: sign_off (the default) or auto
sign_offs:
  - {period: 2026Q3, by: ceo@example.com, date: "2026-10-14"}
```

| `period` | Labels | Example `ref_id` |
| --- | --- | --- |
| `month` | year and month | `EX-E8-BACKUP-2026M02` |
| `quarter` | year and calendar quarter | `EX-E8-BACKUP-2026Q4` |
| `half_year` | year and calendar half | `EX-E8-BACKUP-2027H1` |
| `year` | year, or `FY` and the year it starts when `starts` is not 1 January | `EX-E8-BACKUP-2026`, `EX-E8-BACKUP-FY2026` |

`starts` must be the first day of a month. Periods follow on from it, each labelled by the month, quarter, half or year it starts in. `hound` works out the periods from the date it runs on (UTC), and each `scope plan` and `scope apply` brings CISO Assistant up to that date:

- The **open** period (the one holding today) is in progress. It is created from the previous period's audit as its baseline, so CISO Assistant copies its results, evidence links and hand-written assessments over, and its ETA is the period's last day. Its documents, controls, hand-written assessments and `unlisted` rule are written to it only.
- An **ended** period moves to in review and is never written again, by `scope apply` or by runs.
- With `close: sign_off`, an ended period stays in review until the file records its sign-off in `sign_offs`; the next `scope apply` sets it to done and locks it, so the approval is itself a reviewed commit. With `close: auto` it is done and locked as soon as it ends.
- A missed boundary is caught up once: an apply after several periods have ended creates or closes each of them in turn.

A deployment target may name the series (`compliance_assessment: EX-E8-BACKUP`): with the config's `scope:` key pointing at this folder, each run publishes to the period open on the run's date. A run never writes to an audit that is in review, done or locked; it reports `plan.audit_closed` instead. Set `HOUND_TODAY=YYYY-MM-DD` to run any command as of another date, for example to check a boundary.

The plan lists what happens to each period under **Audit periods**: opens (with its baseline), closes (in review, awaiting sign-off), awaiting sign-off (still in review from an earlier period), or signed off (done and locked). In JSON these are `periods[]`, each `{audit, event, baseline}`.

CISO Assistant v4.3.4 audits have no start or end dates, so the period shows in the `ref_id`, the name (`Backups 2026Q4`) and the ETA. Locking an audit does not freeze the applied controls it links to, so a locked period never blocks the open one.

## The review workflow

Every change to CISO Assistant's structure goes in as a pull request to the compliance repo:

1. **Copy the template.** Copy [`examples/compliance-repo/`](../examples/compliance-repo/) to a new private repository, replace Example Pty Ltd with your own organisation, and point `hound.yaml` at your CISO Assistant. If CISO Assistant is already set up, start from an [export](#export) instead.
2. **Change the files.** By hand, or with an assistant: the repo's `CLAUDE.md` tells it the layout, to validate after every change, never to write secrets or evidence into the repo, and to use the plan as the pull request's summary.
3. **Validate.** `hound scope validate scope/ --config hound.yaml` and `hound mapping validate mappings/<file>.yaml` report every problem with its file and line.
4. **Review the plan.** The pull request's workflow validates again and comments `hound scope plan` as Markdown: every create, field-by-field update, delete (only with `--prune`) and drift. Reviewers approve the change in CISO Assistant terms, not just the YAML diff.
5. **Apply on merge.** Merging runs `hound scope apply` in a protected environment with required reviewers, so a second person approves the write itself. Apply is safe to run again; drift found later is written back the same way.

For example, to change who owns a system: edit its `owner` in `scope/setup.yaml`, run `hound scope validate scope/ --config hound.yaml` (it passes), and `hound scope plan scope/ --config hound.yaml` lists one update, `| Update | system | Example laptops | owner: it@example.com → ceo@example.com |`. `hound scope apply scope/ --config hound.yaml` writes it and reports `"written": 1`.

The workflow in the template, [`.github/workflows/compliance.yml`](../examples/compliance-repo/.github/workflows/compliance.yml), needs a self-hosted runner that can reach CISO Assistant, the `HOUND_CA_URL` and `HOUND_CA_TOKEN` secrets, and an environment named `ciso-assistant` with required reviewers. Audit periods are closed the same way: a period's sign-off is a line in its audit file, reviewed and merged like any other change ([Audit periods](#audit-periods)).

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
| `scope.cycle_start_invalid` | A cycle's `starts` is not the first day of a month. |
| `scope.sign_off_without_cycle`, `scope.sign_off_date_invalid`, `scope.sign_off_unknown_period`, `scope.sign_off_early` | A sign-off in an audit without a cycle, with a date that is not `YYYY-MM-DD`, for a period the cycle does not have, or dated before its period ends. |
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

`hound` marks the objects it manages by ending their description with `[hound scope <state>]`, a fingerprint of the fields it wrote, other than links and an audit's status and lock. Those are compared one by one instead, because runs add their own, such as a collector's health evidence on a control. That is how the plan tells an object edited in CISO Assistant (drift) from one the scope changed (an update). Frameworks and assessments carry no marker.

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

## Export

```sh
HOUND_CA_URL=http://localhost:8000/api/ HOUND_CA_TOKEN=… hound scope export --out compliance-repo/scope [--root-folder Global] [--with-files]
```

Export is the way in for an organisation that set up CISO Assistant by hand. It reads CISO Assistant (GET requests only) and writes the scope folder from what it holds under the root folder:

- `setup.yaml`: the root folder, each framework an audit uses (with its library and a snapshot under `frameworks/`, made from CISO Assistant's own copy so the files validate offline), and each domain with its perimeters and systems.
- `controls.yaml`: every applied control, with its systems and documents.
- `audits/<ref_id>.yaml`: each compliance assessment, with the documents and controls its rows link (each with the requirements it is linked to), and its hand-written results as `assessments`. `unlisted` is `not_assessed`, which changes nothing until you give `--config`.

What is left out, because collectors and manual sources rebuild it: rows a run computed (their observation ends with a run marker), rows still to do and not assessed, and evidence in the root folder, which is where runs put theirs. A control's status and reference control, a system's tags and an audit's field visibility are not exported; add them by hand if you want the files to set them. Libraries CISO Assistant loaded that no audit uses, such as mapping sets, are not listed either.

Everything is sorted and written the same way each time, so exporting an unchanged instance again gives no diff. With `--with-files`, each document's latest file is downloaded to `documents/<id>/` next to the scope folder and listed in the audit file; without it, documents list no files and CISO Assistant keeps the ones it has.

An audit, control or document without a `ref_id` is given one made from its name (`Essential Eight review 2026` becomes `ESSENTIAL-EIGHT-REVIEW-2026`; a document's is lower case). `scope plan` matches the object by that name until the next `scope apply` writes the `ref_id` to it.

Planning a fresh export of an instance `hound` manages lists no changes. Objects made by hand carry no marker yet, so the first plan lists each as "mark as managed"; the first apply marks them and writes any missing `ref_id`, and nothing else changes.
