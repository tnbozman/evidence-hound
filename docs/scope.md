# Scope files

A compliance repo declares the organisation's structure in CISO Assistant as files, so it changes through reviewed pull requests rather than clicks. `hound scope validate` checks the files offline, `hound scope plan` shows what they would change in CISO Assistant, and `hound scope apply` writes them.

A complete fictional compliance repo is in [`examples/compliance-repo/`](../examples/compliance-repo/): copy it to a new private repository to start your own (see [The review workflow](#the-review-workflow)). The schema is [`schemas/scope.schema.json`](../schemas/scope.schema.json); add `# yaml-language-server: $schema=…` to the top of each file for editor help.

```text
compliance-repo/
  hound.yaml            the deployment config (docs/configuration.md)
  scope/
    setup.yaml          organisation, frameworks, other libraries, domains with their perimeters and systems
    controls.yaml       applied controls, each declared once
    audits/
      ex-e8-2026.yaml   one file per audit (compliance assessment)
    access.yaml         users with their roles, and teams (optional)
    settings.yaml       feature flags and general settings (optional)
    third-parties/
      example-cloud.yaml  one file per supplier or other third party (optional)
    capabilities.yaml   which system provides each mitigation (optional)
    ssp.yaml            one system security plan per programme (optional)
  mappings/             which checks satisfy which requirements (docs/mapping-files.md)
  frameworks/           framework snapshots and custom library files
  documents/            the files that documents name, relative to the compliance repo (the scope folder's parent)
  manual-sources/       the hand-exported sources; their exports go outside the repo
  CLAUDE.md             instructions for an AI assistant
  .github/workflows/compliance.yml
```

The local CISO Assistant (`make ca-up`, the dev container and the functional tests) is seeded by applying this example scope, so every pull request exercises `hound scope apply`.

## Ref ID conventions

Every ref ID reads the same way whoever wrote it: its kind's prefix, then upper case letters, digits and hyphens. `hound` keeps one table of these conventions, used by every prompt and by `hound scope validate`.

| Kind | Prefix | Example |
| --- | --- | --- |
| third party | `SUP-` | `SUP-EXAMPLE` |
| solution | `SOL-` | `SOL-EXAMPLE-HOSTING` |
| contract | `CTR-` | `CTR-EXAMPLE-2026` |
| system | `SYS-` | `SYS-FIREWALL` |
| control | `AC-` | `AC-BACKUP` |
| perimeter | `PER-` | `PER-CORP` |
| supplier assessment | `EA-` | `EA-SUP-EXAMPLE` |
| evidence plan row | `EV-` | `EV-BACKUP-RESTORE` |

An audit has no prefix. An audit that runs in periods, as `hound init` writes it, is a series `<CODE>-<FRAMEWORK>`, such as `EX-E8`, or `<CODE>-<FRAMEWORK>-<SYSTEM>` for one system; each period reads `<CODE>-<FRAMEWORK>-<INTERVAL>-<YEAR>FY[-<SYSTEM>]`, such as `EX-E8-Q2-2027FY` ([Audit periods](#audit-periods)). An audit without a cycle reads `<CODE>-<FRAMEWORK>-<YEAR>`, such as `EX-E8-2026`.

A prompt for a ref ID (`hound add system`, `hound add control`, `hound onboard <system>`) prints the prefix as part of the prompt, such as `Supplier ref_id: SUP-`, and takes only the rest. The answer is upper-cased, and an answer with a space or any other character is asked again with the rule. An option such as `--ref-id` is taken in full when it has its prefix and is given the prefix when it has not. `hound scope validate` reports any other ref ID as `scope.ref_id_convention`, naming the kind, the prefix and the ref ID to rename it to.

## setup.yaml

| Key | Meaning |
| --- | --- |
| `organisation.root_folder` | The existing folder the domains sit under. Default `Global`. |
| `organisation.entity` | Optional. The organisation's own entity, which CISO Assistant creates itself as its main entity: `name`, and optional `description`, `mission`, `country`, `currency` and `identifiers`, as for a [third party](#third-partiesentityyaml). `hound scope apply` only ever updates it in place, never creates or deletes it, and it carries no `[hound scope]` marker. Left out, the main entity is not read or changed. |
| `frameworks` | Each framework to load: `urn` (the framework URN), and `library` (a library URN from CISO Assistant's stored libraries) or `file` (a custom library file). `snapshot` names the framework snapshot made by `hound framework snapshot`, so groups and requirements can be checked offline; Paths are relative to setup.yaml. |
| `libraries` | Other libraries to load. A URN loads one CISO Assistant already stores, such as the mapping set between the Essential Eight and the Essentials. A map with `urn` and `file` uploads your own library from the repo, such as a custom risk matrix or threat catalogue, and `snapshot` names the snapshot made by `hound library snapshot`. Raise the file's `version` whenever you change it: CISO Assistant only takes a new version, so `hound` stops with `library.version_not_bumped` otherwise, and uploads and updates the library when the version goes up. See [Risk matrices and threats](#risk-matrices-and-threats). |
| `domains` | Folders. Each has a `name`, a `description`, its `perimeters` (or a single `perimeter`) and its `systems`. |
| perimeter | `name`, optional `ref_id` and `description`. |
| system | `name`, `type` (`primary` or `supporting`), optional `ref_id`, `description`, `owner`, `parents` (other systems by name), `tags` (per-framework labels such as `essentials_domain: identity` or `cui: "true"`), `solution` (the third-party solution that provides it, by `ref_id`) `supplied_through` (a reseller or managed service provider, by the third party's `ref_id`) `data` (the [sensitive data](#sensitive-data) it handles) `ai` (its [AI register entry](#ai-systems), when it is an AI system), `objectives` (security objectives on CISO Assistant's 0 to 4 scale, such as `{confidentiality: 3, availability: 2}`) and `recovery` (recovery objectives `rto`, `rpo` and `mtd` as durations such as `4h` or `2d`; see [Business impact analyses](#biabiayaml)). Objectives and recovery objectives are written only when the file declares them, so values set in CISO Assistant are otherwise kept. A system with either supplier key is registered only once those suppliers are approved: see [Supplier approval](#supplier-approval). |
| `programmes` | Optional. The compliance programmes the reports are organised by, such as the Essential Eight and later AI6. Each has `id` (a lowercase slug such as `essential-eight`, unique), `name`, `framework` (a framework URN declared above, with a `snapshot`), `groups` (the implementation groups reported against, which must exist in the snapshot), `audit` (the `ref_id` of the audit, or audit series, of record) and optional `group_by` (`parent`, the default: requirements are grouped under their parent node, such as the Essential Eight strategy), and optional `register`, the register its reports summarise: `of` (`ai_systems`, the [AI systems](#ai-systems), or `systems`, every system) and `by` (the fields to count by: `screening`, `use` and `oversight` for AI systems, `type` for systems), such as `register: { of: ai_systems, by: [screening, use] }`. An audit belongs to at most one programme. Programmes create nothing in CISO Assistant and never appear in `hound scope plan`. `hound programme list` shows each one with its requirement counts per group and per parent node, from the snapshot alone, and its register's counts from the scope. |

### AI systems

An AI system is a system with an `ai` block: its entry in the AI register. The entry is reviewed by pull request like the rest of the
scope, dates included, and is written to CISO Assistant as filtering labels and as lines added to the asset's description, because
CISO Assistant's custom fields are an Enterprise feature. The register is CISO Assistant's asset list filtered by the `ai-system`
label, which its own Excel export can save. [The AI governance primer](ai-grc-primer.md) explains the terms.

```yaml
systems:
  - name: Example lead scoring
    type: supporting
    owner: sales@example.com
    solution: SOL-EXCRM-SCORING          # needed for a third-party AI system
    ai:
      screening: elevated                # normal, elevated or unacceptable: the National AI Centre's screening outcome
      screening_yes: [1, 5]              # the screening questions answered yes, by number; [] for none
      screened_on: "2026-03-02"
      registered_on: "2026-03-04"        # the rest are the National AI Centre register template's columns
      version: "2026.1"
      purpose: Focus the sales team on the leads most likely to buy.
      intended_uses: [Rank new leads each morning]
      limitations: Not for decisions about credit or pricing.
      misuse: Using scores to drop leads from some regions.
      data_sources: [CRM lead records (structured)]
      stakeholders: [Prospective customers, Sales team]
      use: third_party                   # built, customised or third_party
      oversight: human_on_the_loop       # human_in_the_loop, human_on_the_loop or human_out_of_loop
      oversight_note: The sales lead reviews a monthly sample of low scores and can switch scoring off.
      impact_assessment: approved        # not_started, in_progress, approved or not_required
      impact_assessed_on: "2026-03-20"   # only with approved
      in_use_from: "2026-04-01"          # left out while it is a pilot
      last_tested_on: "2026-03-25"
      # retired_on: "2026-10-31"         # when it stopped being used
```

`screening`, `screening_yes`, `screened_on`, `use`, `oversight` and `oversight_note` are required. The register check
`ai.register_complete` fails an entry without `purpose`, `intended_uses`, `limitations`, `misuse`, `data_sources`, `stakeholders`,
`registered_on` or `impact_assessment`. The template's *Status* follows from the dates (pilot, operational, decommissioned).
[AI governance](ai-grc.md#the-national-ai-centres-register-template) traces each template column to its key.

In CISO Assistant the asset gets the labels `ai-system`, `ai-screening-elevated`, `ai-use-third-party` and `ai-oversight-on-the-loop`
(and `ai-retired` once retired), and its description gains the entry in words, starting "AI register: elevated screening (yes to
questions 1, 5), third party, human on the loop." `hound scope validate` reports `ai.screening_disagrees` (normal with a yes, or
elevated with none), `ai.impact_date_without_approval`, `ai.retired_before_use`, and `ai.solution_missing` for a third-party AI system
with no `solution`.

### Risk matrices and threats

Risk studies are rated on a risk matrix and name threats from a threat catalogue. Both come from CISO Assistant libraries. Many arrive
with a framework: CISO Assistant loads a library's declared `dependencies` with it, so loading the Essential Eight also loads intuitem's
Common Catalog of threats (and its policy and reference controls catalogue). The ASD Essentials library declares none. `hound scope plan`
shows what a framework brings in a `brings` line, read from its snapshot. Declare only what does not arrive with a framework.

A built-in matrix, such as `urn:intuitem:risk:library:critical_risk_matrix_5x5`, is a URN under `libraries`. Your own matrix or threats
go in a library file under `libraries/` in the repo, listed with its URN and file:

```yaml
libraries:
  - urn: urn:intuitem:risk:library:critical_risk_matrix_5x5
  - urn: urn:example:risk:library:example-risk
    file: ../libraries/example-risk.yaml
    snapshot: ../libraries/example-risk.json
```

Check the file offline, then snapshot it so risk studies are checked by level name and threat without CISO Assistant:

```console
$ hound library validate libraries/example-risk.yaml
valid: 1 risk matrix, 2 threats
$ hound library snapshot libraries/example-risk.yaml --out libraries/example-risk.json
```

`hound library validate` reports `library.matrix_urn_invalid` (a matrix URN not of the form `urn:<packager>:risk:matrix:<id>`),
`library.matrix_level_gap` (an explicit level `id` that is not its position, counting from 0), `library.matrix_grid_shape` (not one grid
row per probability level with one cell per impact level) and `library.matrix_grid_unknown_risk` (a cell that is not a risk level).
`hound scope validate` runs the same checks on every library file the setup lists.

CISO Assistant only takes a changed library when its `version` goes up. If the file differs from the one it stores at the same version,
`hound scope plan` and `hound scope apply` stop with `library.version_not_bumped`. Raise the version, and the apply uploads the file and
updates the loaded library.

## controls.yaml

`controls` lists each applied control once, because CISO Assistant treats an applied control as a reusable object serving many requirements across frameworks: `ref_id` (the stable key), `name`, and optionally `description`, `domain` (left out for the global domain), `owner`, `status` (`to_do`, `in_progress`, `on_hold`, `active`, `degraded`, `deprecated`), `priority` (`"1"` to `"4"`), `category`, `eta`, `systems`, `reference_control` (a URN), `documents` (ids of documents declared in audit files) and `responsible_entity` (`consumer`, `shared`, `outsourced_service_provider`, `cloud_service_provider` or `not_applicable`: who implements the control, for the SSP annex only; hound never writes it to CISO Assistant. Without it the annex infers *Shared* when the control covers a system a supplier's solution provides, and *Consumer* otherwise).

## access.yaml

Who can do what in CISO Assistant, so access changes are reviewed as pull requests and a rebuilt instance gets the same access. The file is optional; without it `hound` neither reads nor changes users.

- `users`: `email` (the key, compared without case), `first_name`, `last_name`, `active` (default `true`), and `service_account` for an account that is not a person: `publish` (or `true`) for the collector runner, `apply` for an account that applies the scope.
- `teams`: `name`, `description`, `domain` (left out for the root folder), `leader` and `members` by email, and `email`, the team's address for CISO Assistant's task reminders. A team with an `email` and no leader or members is an owner contact: it is reminded and has no login, so it holds no role ([Manual evidence](manual-evidence.md#owner-contacts)).
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

Names are checked offline against the settings snapshot of the CISO Assistant release the stack pins ([`ciso-assistant-settings.json`](../src/EvidenceHound.Core/Scope/ciso-assistant-settings.json), with each flag's default); an unknown name is an error (`settings.flag_unknown`, `settings.setting_unknown`) whose hint lists the valid ones. Two flags exist in the release but its API only offers them when a backend setting the stack leaves off is on: `chat_mode` (`ENABLE_CHAT`) and `infra_config_management` (`ENABLE_INFRA_CONFIG_MANAGEMENT`). Naming either is an error (`settings.flag_gated`). A secret setting such as `openai_api_key` is an error (`scope.secret_in_file`): set it in CISO Assistant itself. Turning off a module the scope still uses is an error too: `compliance: false` would hide the audits the scope declares (`settings.feature_disabled_in_use`). Upgrading CISO Assistant means refreshing the snapshot, and the functional tests compare it with the pinned release.

`scope plan` lists each named setting whose value differs (`| Update | feature_flag | tprm | value: true → false |`). `scope plan --show-unmanaged` adds an **Unmanaged settings** section: every flag and general setting the file does not name, with its current value, to help decide what to manage. Settings carry no marker and are never deleted, so `--prune` leaves them alone.

## third-parties/&lt;entity&gt;.yaml

The supplier register, one file per supplier or other third party, so a new supplier or a renewed contract is a reviewed pull request. How to move a supplier spreadsheet here with your agent, and keep it current: [third-parties.md](third-parties.md). Each file is a CISO Assistant entity with the solutions it provides to the organisation, the contracts covering them and, optionally, its representatives. The folder is optional; without it `hound` neither reads nor changes third parties.

```yaml
ref_id: SUP-EXCLOUD
name: Example Cloud Pty Ltd
description: Hosts the customer portal.
mission: Managed cloud hosting.
country: AU                  # ISO 3166-1 alpha-2
currency: AUD                # ISO 4217
domain: Example Pty Ltd      # left out for the root folder
dependency: 3                # 0-4: how much we depend on it
penetration: 2               # 0-4: how far it reaches into our systems
maturity: 3                  # 1-4: its security maturity
trust: 3                     # 1-4: how far we trust it
identifiers:
  LEI: EXAMPLE0000000000000
solutions:
  - ref_id: SOL-EXCLOUD-HOST
    name: Managed hosting
    criticality: 3           # 1-4
contracts:
  - ref_id: CTR-EXCLOUD-2026
    name: Hosting agreement 2026
    status: active           # draft, active, expired or terminated
    start: "2026-01-01"
    end: "2026-12-31"
    annual_expense: 12000
    currency: AUD
    solutions: [SOL-EXCLOUD-HOST]
representatives:
  - email: account.manager@example.com
    first_name: Sam
    last_name: Example
    role: Account manager
```

- The entity: `ref_id` and `name`, and optionally `description`, `mission`, `country`, `currency`, `parent` (another third party's `ref_id`, for a subsidiary), `domain`, the four levels CISO Assistant uses as defaults for its stakeholder assessments (`dependency`, `penetration`, `maturity`, `trust`), and `identifiers` (legal identifiers by kind, such as LEI, VAT or ABN). CISO Assistant keeps identifiers on the entity, not on its contracts.
- `solutions`: what the entity provides, each with `ref_id`, `name`, and optionally `description` and `criticality`. A level or criticality the file leaves out is left as CISO Assistant holds it (its default on a new supplier), so a drafted supplier with levels still to decide plans no change after it is applied.
- `contracts`: each with `ref_id`, `name`, `status`, and optionally `description`, `start` and `end` (yyyy-MM-dd), `annual_expense`, `currency` and `solutions` (the entity's own, by `ref_id`). A contract sits in the entity's domain.
- `representatives`: people at the entity, by `email`, with optional `first_name`, `last_name`, `role` and `phone`. They are personal data, so keep them in CISO Assistant instead unless the compliance repo is private and access-controlled. `hound` never gives a representative a login.

Validation reports a country or currency code CISO Assistant does not accept (`supplier.country_invalid`, `supplier.currency_invalid`; the lists come from the pinned release), a level outside its range (`supplier.level_invalid`), a contract that ends before it starts (`supplier.contract_dates_invalid`), a contract covering a solution the entity does not provide (`supplier.solution_unknown`), a repeated `ref_id` or email, an unknown `parent` or `domain`, and an `active` contract whose end date has passed (`supplier.contract_lapsed`): renew it and record the new end date, or set it to `expired` or `terminated`. Turning off the `tprm` or `contracts` feature flag in settings.yaml while third parties or contracts are declared is an error. CISO Assistant ships with `contracts` off, so set `contracts: true` in settings.yaml to see contracts in its interface.

Objects are matched by `ref_id` (representatives by email), and the organisation's own entity, which CISO Assistant makes itself, is never touched. `scope plan` shows a renewed contract's new end date as a one-field update.

### Sensitive data

A solution and a system may list the sensitive data they handle, so a supplier handling data it is not approved for is reported and the
list a prime or an auditor asks for comes from the repo. The list is framework-neutral:

```yaml
solutions:
  - ref_id: SOL-EXCLOUD-HOST
    name: Managed hosting
    data:
      - kind: cui                       # cui, pii, export_controlled or classified
        detail: CUI//SP-CTI             # which: needed for export_controlled and classified
        role: stores                    # stores, processes, transmits or protects
        authority:                      # on a solution only: what approves it
          name: FedRAMP Moderate equivalent
          evidence: documents/suppliers/example-cloud-fedramp.pdf
          expires: "2027-06-30"
```

| Kind | Australian example | US example |
| --- | --- | --- |
| `cui` | Information a US prime marks as CUI under a subcontract | Controlled technical information, `CUI//SP-CTI` |
| `pii` | Personal information under the Privacy Act 1988 | Personal information under a state privacy law |
| `export_controlled` | Technology on the Defence and Strategic Goods List (`DSGL`) | Technical data under `ITAR` or `EAR` |
| `classified` | Security-classified information under the PSPF (`PROTECTED`; `OFFICIAL: Sensitive` is not classified but can be recorded here) | Classified national security information |

Validation reports a system that handles a kind through its `solution`, or through `supplied_through` when it names no solution, whose
data does not list that kind (`data.supplier_not_authorised`); an authority past `expires` (`data.authority_expired`); and
export-controlled or classified data with no `detail` (`data.detail_missing`).

The first two are **warnings**: findings about the organisation, not mistakes in the files. They are printed by `scope validate` (and
`--json` gives them `"severity": "warning"`), `scope plan` and `scope apply`, and listed in the weekly review (`hound review`) every week
until they are fixed, but they block nothing. Record the truth while the data moves or the authority is renewed; never hide it to get a
clean validate.

`scope apply` uses CISO Assistant's own features only: each kind is a filtering label `data-<kind>` (`data-cui`, `data-export-controlled`;
CISO Assistant's labels take letters, digits, `_` and `-`) on the solution's and the system's records, and each solution's description
ends with its data and authorities. `hound supplier list --data cui` prints each supplier's solution handling a kind, with its role,
authority, evidence, expiry and the systems that hold it, as Markdown for a prime or `--json` for an agent.

`hound` does not decide whether information is CUI, export controlled or classified: that is the owner's call with the prime, the
contract and, where it applies, the export licence. It records what they decided and checks the suppliers against it.

### Supplier reviews and tiers

```yaml
forced_tier:                 # optional: the tier the organisation decides, whatever CISO Assistant computes
  tier: critical             # critical, important, standard or low-impact
  reason: Holds staff personal data.
assessments:
  - ref_id: EA-EXCLOUD
    name: Example Cloud review
    solutions: [SOL-EXCLOUD-HOST]
    perimeter: Corporate IT    # a perimeter of setup.yaml, in the assessment's domain
    domain: Example Pty Ltd    # default: the entity's domain
    due_date: "2026-06-30"
    criticality: 3             # 1-4
    questionnaire: urn:example:risk:framework:example-baseline   # a framework of setup.yaml
    groups: ["1"]              # the questionnaire's implementation groups
    review_every: 12 months    # optional: "N months" or "N years"
```

- `assessments` are CISO Assistant's entity assessments: `hound` sets up the review (its name, solutions, perimeter, due date and criticality) and, when it creates one with a `questionnaire`, CISO Assistant creates the questionnaire's audit with it. Answers, the expiry date and the tier stay CISO Assistant's. The **conclusion** is approved in this repo: `hound approve` records it under the assessment's `conclusions`, and `hound scope apply` writes it to CISO Assistant. A review with no conclusion in the file is left as CISO Assistant holds it, so one concluded there still counts.
- `conclusions` lists the reviews a person concluded: `review` (the review's key, as below), `result` (`ok`, `warning` or `blocker`), `by` and `on` (who concluded it and the day). Write them with `hound approve`, in a terminal, not by hand:

  ```yaml
      conclusions:
        - review: ENA-EXCLOUD-2026-03
          result: ok
          by: Sam Example
          on: "2026-04-02"
  ```

- A contract can carry `approved_by` and `approved_on`: who approved the supplier by making the contract `active` with `hound approve`, and when. They are this repo's record and are not sent to CISO Assistant.
- With `review_every`, each review is keyed `<ref_id>-<yyyy-MM>` by its due date, and once today reaches a review's due date the plan opens the next one, a cycle later. Without it, the review is the one object `<ref_id>`.
- `forced_tier` needs a `reason`. `hound` never writes the tier; the plan lists a tier in CISO Assistant that differs from the forced one.

Validation reports an assessment naming a solution the entity does not provide (`supplier.solution_unknown`), an unknown perimeter, domain or questionnaire, a criticality outside 1 to 4, `review_every` without a `due_date` (`supplier.review_without_due_date`), a conclusion naming a review that is not one of the assessment's (`supplier.conclusion_review_unknown`) and a forced tier without a reason (`supplier.forced_tier_reason_missing`).

### Supplier approval

A supplier is **approved** when its latest concluded assessment is concluded `ok`, that conclusion has not expired, no later review is past its due date without a conclusion, and it has an `active` contract. A person approves in this repo with `hound approve` (in a terminal; an agent or a pipe is refused), which makes the contract `active` and records the review's conclusion in the supplier's file. What the file records counts before it is applied, and wins over what CISO Assistant holds for the same review, so one `hound scope apply` can write the supplier, its conclusion and the system together. `scope plan` and `scope apply` check this before planning:

- A system not yet registered whose supplier (its `solution`'s entity, or `supplied_through`) is not approved is refused with `system.supplier_not_approved` and the reason, such as "no concluded assessment", and nothing is applied. Approve the supplier and conclude its review as `ok` with `hound approve`, then plan again.
- A registered system whose supplier is no longer approved stays registered; the plan lists it under **Suppliers** as `supplier.approval_lapsed`, for the security committee.
- A tier that differs from `forced_tier` is listed under **Suppliers** as `supplier.tier_differs`. The JSON plan has both as `suppliers`.

## capabilities.yaml

Which system provides each mitigation of one framework, decided with [`hound onboard environment`](onboarding.md#onboard-the-environment-first-which-system-provides-each-mitigation). Optional.

```yaml
framework: urn:intuitem:risk:framework:essential-eight-2023
mitigations:
  - mitigation: ac              # the last part of the mitigation's node URN
    provided_by: system         # system, manual or gap
    systems: [Example app control]
    supplier: SUP-EXAPPCTL      # optional, a third party's ref_id
    owner: it@example.com       # optional
    evidence: profile           # optional: collector, profile or manual
    route: example-app-control  # optional: the collector or profile
    approved_by: GRC lead       # required: the role that approved it
    approved_on: "2026-11-23"
    reason: Application control is enforced by the third-party product, not Intune.
    signals:
      - "Microsoft Graph: licence SPE_E3 (Microsoft 365 E3) is subscribed"
```

The map only decides which system answers each requirement. Results stay in each system's own audit; in every other system's audit of the
same framework, the mitigation's requirements stay open (`not_assessed`) with `provided_by` naming the providing system, and the observation
names its audit; each closes when that system's audit shows the requirement met. Validation reports a framework
`setup.yaml` does not load (`capability.framework_unknown`), a mitigation decided twice (`scope.duplicate`), not in the framework
(`capability.mitigation_unknown`) or not decided at all (`capability.decision_missing`), a missing approver (`capability.approver_missing`),
a system decision with no systems (`capability.systems_missing`) and an unknown supplier. A system named but not declared yet is not an
error: it is waiting to be onboarded. `hound scope plan` lists each gap under "Capability gaps" (JSON `gaps`): the mitigation and the
requirements no system's audit answers. It lists each requirement an audit leaves open for another system under "Open dependencies" (JSON
`dependencies`): the audit, the requirement, the system that meets it, that system's audit if it has one, and the result there (as the scope
declares it, "from collectors" when that audit's mapping computes it, or "no audit yet").

## evidence-plan.yaml

How each control's evidence arrives, from whom and how often. Optional. The guide is [Manual evidence](manual-evidence.md).

| Key | Meaning |
| --- | --- |
| `grc_team` | The team (in `access.yaml`) that receives and reviews evidence, for rows that do not name their own. |
| `evidence` | One row per control: `ref_id` (`EV-`), `control`, `requirements` (optional), `method` (`automated`, `manual` or `hybrid`), `collector` (a collector in `hound.yaml`, for automated and hybrid rows), `owner` (a team, for manual and hybrid rows), `grc_team`, `every` (`3 months`, `6 months` or `12 months`) and `runbook` (a Markdown file in the repo). |

Each row becomes an evidence item labelled `method:` and `collector:`; each manual and hybrid row also becomes a recurring task for the owner and the GRC team, with the runbook as its description. Problems: `evidence_plan.owner_missing`, `evidence_plan.every_missing`, `evidence_plan.runbook_missing`, `evidence_plan.runbook_file_missing`, `evidence_plan.collector_missing`, `evidence_plan.collector_unknown`, `evidence_plan.team_unknown`, `evidence_plan.control_unknown` and `evidence_plan.owner_contact_has_role`.

## ssp.yaml

One system security plan (SSP) per programme. Optional. `hound ssp build --programme <id>` assembles the plan from one read of CISO Assistant and the repo, and writes nothing. It prints the plan in the layout of NIST SP 800-18 Rev 1 ([ssp.md](ssp.md)); `--json` gives every value with its source (`ciso_assistant` with the object, or `repo` with the file and line), so each one can be traced.

| Key | Meaning |
| --- | --- |
| `programme` | The programme's `id` from `setup.yaml`. One plan per programme. |
| `profile` | The controls annex: `ism` (ISM controls grouped by Essential Eight strategy) or `nist-800-171` (CMMC, s17). The layout is always NIST SP 800-18 Rev 1, which also meets ISM-0041. |
| `system_name`, `version` | The system's name as the plan gives it, and the plan's version. |
| `classification`, `operational_status`, `system_type` | SP 800-18 sections 2, 7 and 8: the highest classification the system processes (such as `OFFICIAL`), whether it is operational, under development or being modified, and whether it is a major application or a general support system. Optional; shown as `-` when absent. |
| `perimeter` | The boundary: a perimeter in `setup.yaml`. The inventory is the systems the audits on that perimeter cover, as CISO Assistant holds them, with their owners. |
| `roles` | `system_owner`, `authorising_official` and `security_contact`, each a user's email or a team from `access.yaml`. All three are required. |
| `documents` | Boundary and network diagrams, data flows, policies and procedures, by document `id` from an audit. |
| `sections` | The parts that cannot be generated, as Markdown files in the repo (paths relative to the folder that holds `scope/`): `purpose`, `environment`, `management` and `laws_and_policies`, all required. See [ssp.md](ssp.md) for where each one goes in the plan. |

Each requirement row gets a status from its result: `compliant` is *Implemented*, `partially_compliant` *Partially implemented*, `not_applicable` *Not applicable*, and `non_compliant` or `not_assessed` *Planned* when a linked control has an ETA, otherwise *Not implemented*. Its statement is the linked controls' descriptions in ref order, then the observation; the responsible party is the controls' owners. A row in place with no statement, a row in place or planned with no owner, and a row not applicable with no justification (its observation) are **gaps**: listed first, in `--json`, and fatal with `--strict` (exit 1). A row not yet implemented is not a gap: `hound ssp poam --programme <id>` lists it, one line per linked control with its owner, ETA and status.

External services are the solutions the scope links the boundary's systems to (`solution:` in `setup.yaml`), with their suppliers as CISO Assistant holds them. Controls and requirement rows come from the programme's audit of record. Problems: `ssp.programme_unknown`, `ssp.perimeter_unknown`, `ssp.role_missing`, `ssp.role_unknown`, `ssp.document_unknown`, `ssp.section_missing`, and `scope.duplicate` for a second plan for the same programme.

## risks/&lt;study&gt;.yaml

One file per risk study (CISO Assistant's risk assessment) and its scenarios. Ratings, treatment and justification are proposed and
reviewed in a pull request like any other change; accepting a risk and approving it stays with the Approver in CISO Assistant. Ratings
use the matrix's level names from its [library snapshot](#risk-matrices-and-threats). CISO Assistant computes the risk levels, so they
are never written.

```yaml
ref_id: EX-RISK-2026
name: Example Pty Ltd risk register 2026
domain: Example Pty Ltd
perimeter: Corporate IT                  # optional
matrix: urn:example:risk:matrix:example-4x4
status: in_progress                      # planned, in_progress, in_review, done or deprecated
authors: [it@example.com]
reviewers: [ceo@example.com]
eta: "2026-12-31"
scenarios:
  - ref_id: R-01
    name: Ransomware encrypts the file shares
    systems: [Example 365]               # impacted systems, by name
    threats: [THR-02]                    # by URN or ref_id, from a library snapshot
    existing_controls: [AC-BACKUP]       # by ref_id from controls.yaml
    planned_controls: [AC-MFA]
    current: {probability: Possible, impact: Major}
    residual: {probability: Unlikely, impact: Major}
    # inherent: {probability: Likely, impact: Severe}   # when CISO Assistant's inherent risk flag is on
    treatment: mitigate                  # open (the default), mitigate, accept, avoid or transfer
    justification: Backups are restored and tested each month.
```

`--` (or leaving a rating out) means not rated. `hound scope validate` reports `risk.matrix_unknown` (no library snapshot holds the
matrix), `risk.level_unknown` (a level name the matrix does not have, with its line), `risk.residual_above_current` (treatment can only
lower a risk, as CISO Assistant's own consistency check says), `risk.system_unknown`, `risk.threat_unknown`, `risk.control_unknown`,
`risk.domain_unknown`, `risk.perimeter_unknown`, duplicate `ref_id`s, and the warning `risk.planned_control_active` for a planned control
that controls.yaml says is already active.

`hound scope plan` shows a rating change by level name, such as `current_probability: Likely → Possible`, so a reviewer reads what
changed without the matrix to hand.

### Risk acceptances

A scenario with `treatment: accept` asks CISO Assistant for a risk acceptance, under the ref_id `ACC-<study>-<scenario>` with the
scenario's justification. `hound` never sets its state: the Approver accepts or rejects it in CISO Assistant, and `hound scope plan`
shows what they decided.

## exceptions.yaml

Security exceptions: a known gap the organisation lives with for a time, such as an application that cannot use single sign-on until its
upgrade. The request is written and reviewed in the repo; only an approver in CISO Assistant approves it, so the decision keeps its
separation of duties.

```yaml
exceptions:
  - ref_id: EXC-01
    name: Legacy finance app without MFA
    description: The finance app cannot use single sign-on until its upgrade.
    domain: Example Pty Ltd
    severity: medium                     # undefined, info, low, medium, high or critical
    owners: [it@example.com]
    expiration_date: "2027-03-31"
    observation: Sign-in is limited to the office network.
    systems: [Example 365]               # by name, from setup.yaml
    controls: [AC-MFA]                   # by ref_id, from controls.yaml
    scenarios: [EX-RISK-2026/R-01]       # <study ref_id>/<scenario ref_id>
    requirements:                        # audit rows the exception covers
      - audit: EXAMPLE-BASELINE-2026
        requirement: EX-1.1
```

- A `status` in the file is an error (`scope.approval_not_configurable`). Apply the exception as a request; its approver decides in
  CISO Assistant.
- `hound scope validate` reports `exception.domain_unknown`, `exception.system_unknown`, `exception.control_unknown`,
  `exception.scenario_unknown`, `exception.audit_unknown` and duplicate `ref_id`s.
- `hound scope plan` lists each exception and risk acceptance with its CISO Assistant status (`draft`, `in_review`, `approved`,
  `rejected`, `resolved`, `expired` or `deprecated`), then what to renew or close: `exception.expiring` (an approved one that expires
  within 60 days) and `exception.expired_but_approved`.
- `hound status --config hound.yaml --scope scope` lists, offline, the exceptions whose declared expiry is within 60 days or past.
- When a check fails on a row an approved, unexpired exception covers, `hound run` names the exception in the row's observation, such as
  "Covered by security exception EXC-01 (Legacy finance app without MFA), approved until 2027-03-31." The result itself stays
  non-compliant: an exception explains a gap, it does not close it.

## bia/&lt;bia&gt;.yaml

One file per business impact analysis (BIA): how long each system can be down before the impact grows, on a risk matrix's impact scale.
The continuity owner proposes it in a pull request; `hound scope apply` writes it to CISO Assistant's BIA, its asset assessments and their
escalation thresholds, and CISO Assistant draws the impact-over-time chart.

```yaml
ref_id: EX-BIA-2026
name: Example Pty Ltd business impact analysis 2026
domain: Example Pty Ltd
perimeter: Corporate IT                  # optional
matrix: urn:example:risk:matrix:example-4x4
version: "1.0"
status: in_progress                      # planned, in_progress, in_review, done or deprecated
authors: [it@example.com]
due_date: "2026-12-31"
assessments:
  - system: Example 365                  # one assessment per system, by name
    dependencies: [Example laptops]      # systems it needs to run
    controls: [AC-BACKUP]                # controls that keep it running, by ref_id
    recovery: {documented: true, tested: false, targets_met: false}
    observation: Mail and files stop when Example 365 is down.
    thresholds:                          # in ascending time
      - {after: 4h, impact: Minor, justification: Staff use phones.}
      - {after: 1d, impact: Major, qualifications: [Reputation], justification: Customers notice.}
      - {after: 3d, impact: Severe}
```

Durations are a whole number and a unit: `m`, `h`, `d` or `w`. `impact` is an impact level name from the matrix's [library
snapshot](#risk-matrices-and-threats); `qualifications` are CISO Assistant's qualification terms by name, such as Reputation.
`hound scope validate` reports `bia.system_duplicate` (one assessment per system), `bia.threshold_order` (thresholds must be unique and
ascend), `bia.impact_unknown`, `bia.system_unknown`, `bia.control_unknown`, `bia.matrix_unknown`, `bia.domain_unknown`,
`bia.perimeter_unknown`, `scope.duration_invalid`, and the warning `bia.impact_falls` for an impact lower than an earlier one, because
CISO Assistant carries the highest impact forward. In a plan, an assessment is keyed `bia/system` and a threshold `bia/system/after`, such
as `EX-BIA-2026/Example 365/1d`.

### Recovery flags from checks

A recovery flag can come from evidence instead of a hand-set value: name the checks that prove it with `documented_by`, `tested_by` or
`targets_met_by`.

```yaml
    recovery: {documented: true, tested_by: [backup.restore_tested]}
```

After collecting, each `hound run` sets the flag from every collector's latest results: true only when every named check passed, false
when any failed or has no result. It writes only when the value changes, and links the checks' evidence to the asset assessment when
CISO Assistant already holds it (evidence a run published for a requirement); evidence it does not hold is named in a
`recovery.evidence_not_published` warning, and the flag is still set. The run's JSON lists each flag it set under `recovery`, with why.
`hound scope plan` leaves such a flag alone, and an asset assessment that `hound scope apply` has not created yet waits for it. A flag
cannot be both set by hand and named by checks (`scope.result_conflict`), and a check is named by its id (`scope.value_invalid` if it is
not one).

## audits/&lt;audit&gt;.yaml

| Key | Meaning |
| --- | --- |
| `ref_id`, `name` | The audit's stable key and title. |
| `framework`, `groups` | The framework URN and the implementation groups it assesses, such as `["1", "2"]` for Essential Eight ML2. |
| `domain`, `perimeter` | Where the audit sits. |
| `eta`, `due_date`, `authors`, `reviewers` | Optional audit fields (dates as `YYYY-MM-DD`). |
| `field_visibility` | Optional, per field: `{auditor: edit\|read\|hidden, respondent: …}`.`result`,`observation` and `evidences` must stay `edit` for the auditor, because CISO Assistant silently ignores writes to fields the writer can only read. |
| `systems` | The systems in scope, by name. They are written to the audit's assets in CISO Assistant; systems added there by hand are kept until the scope names some. |
| `documents` | Static evidence such as policies and reports: `id`, `name`, `files` (paths or globs, relative to the compliance repo, the scope folder's parent), `systems` and `requirements` it supports. A document is linked to the rows it cites in its own audit, and in every other audit of the same framework that shares one of its systems. |
| `controls` | The applied controls this audit relies on, by `ref_id`, each with the `requirements` it supports in this framework. |
| `assessments` | Hand-written results for requirements no check covers: `ref` (or `urn`), `status` (`to_do`, `in_progress`, `in_review`, `done`), `result`, `observation`, and `provided_by` (the systems that meet it, when another system does: the row stays open, never `not_applicable`, and `hound scope plan` lists it under "Open dependencies" with that system's audit and its result there). |
| `cycle`, `sign_offs` | Optional, for an audit that runs in periods: `cycle: {period: month\|quarter, starts: YYYY-MM-DD, close: sign_off\|auto}` and `sign_offs: [{period, by, date}]`. See [Audit periods](#audit-periods). |
| `unlisted` | Required. What happens to in-group requirements the file does not list: `{result: not_assessed}`, or `{result: not_applicable, reason: …}` for an audit scoped to one system. Nothing is marked not applicable silently. It is applied only with `--config`, because the deployment's mappings say which rows a run computes, and those are left alone. |

A requirement is named by its ref_id, or by its URN when the value starts with `urn:` (needed when a framework repeats a ref).

Start an audit file with every in-group requirement listed, titled in a comment, to do and not assessed:

```sh
hound scope new-audit --framework frameworks/essential-eight-2023.json --groups 1,2 --ref-id EX-E8-2026 --out scope/audits/ex-e8-2026.yaml
```

## Audit periods

An audit file with a `cycle` describes a series rather than one audit. The series' `ref_id` is `<ORG>-<FRAMEWORK>`, or `<ORG>-<FRAMEWORK>-<SYSTEM>` for one system's audit, where `<SYSTEM>` is the system's ref ID without `SYS-`. Each period is its own audit, and its label goes after the organisation and framework: `<ORG>-<FRAMEWORK>-<INTERVAL>-<YEAR>FY[-<SYSTEM>]`.

```yaml
ref_id: EX-E8-BACKUP
name: Backups
cycle: {period: quarter, starts: "2026-07-01"}   # close: sign_off (the default) or auto
sign_offs:
  - {period: Q1-2027FY, by: ceo@example.com, date: "2026-10-14"}
```

The year is the financial year, 1 July to 30 June, named by the year it ends: `2027FY` runs from 1 July 2026 to 30 June 2027.

| `period` | Interval | Example `ref_id` |
| --- | --- | --- |
| `quarter` | `Q1` to `Q4`, the quarter of the financial year (`Q1` is July to September) | `EX-E8-Q2-2027FY-BACKUP` (October to December 2026) |
| `month` | `M01` to `M12`, the calendar month | `EX-E8-M07-2027FY-BACKUP` (July 2026), `EX-E8-M02-2027FY-BACKUP` (February 2027) |

`starts` must be the first day of a month, and for a quarterly cycle the first day of a financial-year quarter (1 July, 1 October, 1 January or 1 April); otherwise `scope.cycle_start_invalid`. Periods follow on from it, each labelled by the quarter or month it starts in. `hound` works out the periods from the date it runs on (UTC), and each `scope plan` and `scope apply` brings CISO Assistant up to that date:

- The **open** period (the one holding today) is in progress. It is created from the previous period's audit as its baseline, so CISO Assistant copies its results, evidence links and hand-written assessments over, and its ETA is the period's last day. Its documents, controls, hand-written assessments and `unlisted` rule are written to it only.
- An **ended** period moves to in review and is never written again, by `scope apply` or by runs.
- With `close: sign_off`, an ended period stays in review until the file records its sign-off in `sign_offs`; the next `scope apply` sets it to done and locks it, so the approval is itself a reviewed commit. With `close: auto` it is done and locked as soon as it ends.
- A missed boundary is caught up once: an apply after several periods have ended creates or closes each of them in turn.

A deployment target may name the series (`compliance_assessment: EX-E8-BACKUP`): with the config's `scope:` key pointing at this folder, each run publishes to the period open on the run's date. A run never writes to an audit that is in review, done or locked; it reports `plan.audit_closed` instead. Set `HOUND_TODAY=YYYY-MM-DD` to run any command as of another date, for example to check a boundary.

The plan lists what happens to each period under **Audit periods**: opens (with its baseline), closes (in review, awaiting sign-off), awaiting sign-off (still in review from an earlier period), or signed off (done and locked). In JSON these are `periods[]`, each `{audit, event, baseline}`.

CISO Assistant v4.3.4 audits have no start or end dates, so the period shows in the `ref_id`, the name (`Backups Q2-2027FY`) and the ETA. Locking an audit does not freeze the applied controls it links to, so a locked period never blocks the open one.

## The review workflow

Every change to CISO Assistant's structure goes in as a pull request to the compliance repo:

1. **Copy the template.** Copy [`examples/compliance-repo/`](../examples/compliance-repo/) to a new private repository, replace Example Pty Ltd with your own organisation, and point `hound.yaml` at your CISO Assistant. If CISO Assistant is already set up, start from an [export](#export) instead.
2. **Change the files.** By hand, or with an assistant: the repo's `CLAUDE.md` tells it the layout, to validate after every change, never to write secrets or evidence into the repo, and to use the plan as the pull request's summary.
3. **Validate.** `hound scope validate scope/ --config hound.yaml` and `hound mapping validate mappings/<file>.yaml` report every problem with its file and line.
4. **Review the plan.** The pull request's workflow validates again and comments `hound scope plan` as Markdown: every create, field-by-field update, delete (only with `--prune`) and drift. Reviewers approve the change in CISO Assistant terms, not just the YAML diff.
5. **Apply on merge.** Merging runs the apply job in a protected environment with required reviewers, so a second person approves the write itself. Once approved, it saves a plan with `hound scope plan --out` (shown in the job summary) and applies exactly that file with `hound scope apply <file>` ([Saved plans](#saved-plans)). Apply is safe to run again; drift found later is written back the same way.

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
| `scope.ref_id_convention` | A ref ID without its kind's prefix, or with characters other than upper case letters, digits and hyphens. See [Ref ID conventions](#ref-id-conventions). |
| `scope.reference_unknown` | A domain, perimeter, system, framework, document or control is used but not declared. |
| `scope.framework_snapshot_missing`, `scope.framework_file_missing`, `scope.framework_snapshot_mismatch` | A framework cannot be checked offline: no snapshot, a missing file, or a snapshot of another framework. |
| `scope.group_unknown` | An audit selects a group its framework does not define. |
| `scope.requirement_unknown` | A requirement is not in the framework. |
| `scope.requirement_not_assessed` | A document, control or assessment cites a requirement outside the audit's groups. |
| `scope.result_conflict` | A hand-written result for a requirement a target's mapping also computes, so a person and a collector would write the same row. |
| `scope.other_system_as_not_applicable` | A requirement marked `not_applicable` because another system meets it (`provided_by`, or an observation starting "Provided by"): it would show as closed while nothing proves it met. Keep it open with `provided_by`. |
| `scope.field_not_writable` | `field_visibility` leaves `result`, `observation` or `evidences` below `edit` for the auditor. |
| `scope.unlisted_missing`, `scope.unlisted_reason_missing` | `unlisted` is missing, or marks requirements not applicable without a reason. |
| `scope.cycle_start_invalid` | A cycle's `starts` is not the first day of a month. |
| `scope.sign_off_without_cycle`, `scope.sign_off_date_invalid`, `scope.sign_off_unknown_period`, `scope.sign_off_early` | A sign-off in an audit without a cycle, with a date that is not `YYYY-MM-DD`, for a period the cycle does not have, or dated before its period ends. |
| `config.target_not_in_scope` | A deployment target names an audit or control the scope does not declare. |

## Plan

```sh
HOUND_CA_URL=http://localhost:8000/api/ HOUND_CA_TOKEN=… hound scope plan scope/ [--config hound.yaml] [--format markdown|json] [--prune] [--detailed-exitcode] [--out plan.json]
```

With `--config`, the plan reaches CISO Assistant at the config's `ciso_assistant.url` with the token its `ciso_assistant.token` reference resolves to, so `hound scope plan scope/ --config hound.yaml` needs no variables when the token reference does (for example `env:HOUND_CA_TOKEN`). `HOUND_CA_URL` and `HOUND_CA_TOKEN` override them when set, and are needed without `--config`. `hound scope apply` reads them the same way.

With `--detailed-exitcode`, as with Terraform, the plan exits 0 when there is nothing to change and no drift, 2 when there is, and 1 on any error, a usage error included, so that a pipeline can gate on 2. Without it, the exit codes are hound's usual ones. `--out` also saves the plan to a file, for [Saved plans](#saved-plans).

The plan validates the scope first and stops, reading nothing, when there are problems. Otherwise it reads CISO Assistant (GET requests only) and prints what applying the scope would do, as Markdown for a pull request comment or as JSON for tools. It writes nothing.

- **Create**: objects the scope declares that CISO Assistant does not hold, with their fields.
- **Update**: objects whose fields differ, field by field (old → new). An object that exists but was not made by `hound` is taken over: "mark as managed".
- **Delete**, only with `--prune`: objects `hound` manages that the scope no longer declares. Objects made by hand are never deleted.
- **Suppliers**: lapsed supplier approvals and tiers that differ from the forced one (see [Supplier approval](#supplier-approval)). A system whose supplier is not approved stops the plan.
- **Drift**: objects changed in CISO Assistant since `hound` last wrote them (they are written back), objects under the root folder that the scope does not declare, an audit that is locked in CISO Assistant (it and its rows are never changed), and a framework whose loaded library is older than the snapshot's (it is reported, not upgraded).

### Changes to compliance

Below the change table, the plan shows what the change does to each audit's results, the way Terraform shows changes to outputs below the resource diff, so a reviewer approves the compliance effect and not only the objects. For each audit with a row whose result changes, it reads every row of the audit (GET only) and, from the framework snapshot, gives:

- one line per selected implementation group (for the Essential Eight, each maturity level): requirements compliant before and after, out of the group's total;
- each row whose result changes: the requirement and its title, its strategy or domain, the old and new result, and why (a hand-written assessment in the audit file, or the `unlisted` rule);
- results that go backwards (from compliant or partially compliant to anything lower, or to not assessed) first, marked ⚠, and counted on the audit's line.

```markdown
### Changes to compliance

**EX-E8-2026**: 2 result changes, 1 regression ⚠.

- Maturity Level One: 30 → 30 compliant of 48
- Maturity Level Two: 41 → 41 compliant of 107

|  | Requirement | Title | Strategy | Result | Why |
| --- | --- | --- | --- | --- | --- |
| ⚠ | ISM-1515 | Restoration of systems, applications and data from backups is tested… | RB | compliant → not_assessed | the unlisted rule |
|  | ISM-1486 | Web browsers do not process Java from the internet. | UAH | not_assessed → compliant | hand-written in the audit file |
```

Rows that collector runs own are never changed by the scope, so they are left out; `hound plan` shows their effect. A plan with no result changes has no such section. JSON plans have the same as `compliance[]`, each `{audit, groups[{group, name, before, after, total}], rows[{requirement, title, parent, old, new, why, regresses}]}`, and `scope apply` puts it in its report, so the record of each apply carries the effect it had.

Changes come in dependency order: frameworks, domains, perimeters, users and teams, systems, audits, documents, controls, then assessments. Deletes come last, in reverse.

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

### Saved plans

```sh
hound scope plan scope/ --config hound.yaml --out plan.json   # review the printed plan
hound scope apply plan.json                                    # writes exactly that plan
```

`hound scope apply <file>` applies a plan saved with `--out` without planning again, so what is written is what was reviewed. The file holds the changes and drift, the compliance impact, the CISO Assistant URL, the day the plan was made for, the scope folder and config it was made from with a SHA-256 of their files and of the documents they name, and, for each object the plan writes or deletes, a fingerprint of the state it read in CISO Assistant (none when it creates it).

Apply refuses the plan, writes nothing and exits 3 with `scope.plan_stale`, as Terraform refuses a stale saved plan, when:

- `HOUND_CA_URL` names another CISO Assistant;
- a scope file, the config or a document file changed since the plan was made;
- an object the plan writes or deletes changed in CISO Assistant since, or one it creates now exists. The problem names each one.

Plan again, review it and apply the new file. A file that is not a saved plan from this version is `scope.plan_invalid`. Run apply from the folder the plan was made in: the file names the scope folder and config as they were given.

**Do not commit a saved plan.** It holds no token and no document's content (only hashes), but it does hold the URL and every value the plan writes. Keep it outside the repo, as the template's workflow does with `$RUNNER_TEMP`, and delete it once applied.

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
