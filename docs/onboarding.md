# Onboarding with a language model

`hound onboard` helps a GRC team member onboard a system: a language model the person chooses proposes how the system fits a framework and
drafts its configuration, and the person accepts or edits every decision. The output is files for a pull request in the config repo; nothing
is applied to CISO Assistant until a person reviews it and runs `hound scope validate`, `plan` and `apply`. The design is in
[design/onboarding-assistant.md](design/onboarding-assistant.md).

**No model in evidence collection.** Only `hound onboard` uses a model. `hound run`, `hound serve` and every collector never do: they cannot
reach the onboarding code, and the runner refuses a deployment config (`hound.yaml`) that has an `llm` section or refers to the onboarding
key (`config.llm_in_runner`).

## Set up the model

```console
$ hound onboard config
Model provider (anthropic or openai) [anthropic]:
Model (empty for the provider's default):
API key for anthropic (not shown):
Wrote /work/config-repo/onboarding.yaml; the key is in /home/example/.config/hound/secrets/llm_api_key, readable only by you.
$ hound onboard config --check
anthropic claude-opus-5-5: the key works.
```

The key is saved once, outside the repo, in a file only you can read (`$XDG_CONFIG_HOME/hound/secrets/llm_api_key`, else
`~/.config/hound/secrets/llm_api_key`). `onboarding.yaml` holds only a reference to it, so it is safe to commit. `--check` makes one minimal
request and never prints the key. Options: `--provider`, `--model`, `--config <file>` and `--yes`.

## Onboard a system

```console
$ hound onboard EXAMPLE-ASSETS --framework frameworks/essential-eight-2023.json --group 1 --group 2 --spec ../specs/example-asset-api.openapi.yaml
```

Run it in the config repo (or pass `--repo`). The session goes in this order, and saves after every answer in
`onboarding/<system>/session.json`, with each request and reply beside it in `onboarding/<system>/requests/`. Run the same command again to
carry on where it stopped; the model is never asked again for what it already answered.

1. **Who and what.** Your name (recorded as who accepted each decision), the system owner, the product, and who supplies it: the supplier,
   the solution it provides and the contract covering it, each by `ref_id` and name.
2. **Supplier first.** A supplier not yet under `scope/third-parties/` is drafted there: the model proposes dependency, penetration,
   maturity and trust, and the solution's criticality, each with its reason and sources, and you accept each or type your own. Vendor
   documents you keep (an https URL or a file) are saved under `documents/suppliers/<supplier>/` with their SHA-256 and retrieval date; the
   model reads only the text ones. Its summary of them stays in the session as a draft, never as evidence. The contract is drafted as
   `status: draft`, and the session stops: **a system is onboarded only once its supplier is approved.** Onboarding checks the scope
   files for an active contract covering the solution; `hound scope plan` then checks approval in CISO Assistant, where the GRC team
   concludes the supplier's assessment (see [Supplier approval](scope.md#supplier-approval)). A drafted supplier file includes that
   review (`EA-<supplier>`, due a month from today, then every 12 months, in the first domain and perimeter of `scope/setup.yaml`). So the
   order is: approve the contract in the file, apply the supplier, conclude its review as ok in CISO Assistant, then finish onboarding
   and apply the system.
3. **What the API offers.** Each `--spec` (an OpenAPI 3 document, JSON or YAML) is read into a short summary: the endpoints that read,
   sign-in and paging. Only that summary is sent, never the specification.
4. **Applicability.** Every requirement in the `--group` implementation groups (all of them without `--group`) gets one proposal. Give the
   groups as your audits declare them, so the system's audit matches: for Essential Eight ML2 that is `--group 1 --group 2`. Each requirement gets one of `api_check`,
   `manual`, `not_applicable` or `other_system`, with its reasoning. For each, type `a` to accept, `c`, `m`, `n` or `o` to decide otherwise,
   `l` to accept every remaining proposal that is not `not_applicable`, or `s` to save and stop. Not applicable always needs a justification
   and the name of who approves it; another system needs that system's name.

5. **Written for review.** Once every requirement is decided, it asks for the domain and perimeter of the system's audit (the first ones in
   `scope/setup.yaml` by default) and writes, into the working copy only:
   - `scope/audits/<system>-<framework>.yaml`: the system's own audit. Not applicable rows are done with result `not_applicable` and the
     justification and approver in the observation; rows another system meets say which system; manual rows are to do. Rows checked
     through the API are left to the collector (`unlisted: not_assessed` until its results are published). The audit lists the system.
   - `scope/setup.yaml`: the system is declared in the audit's domain with `solution:` set to the supplier's solution, or, if it is already
     declared, `solution:` is added to it (a different solution already there stops the write). That is what lets `hound scope plan` hold
     the system back until its supplier is [approved in CISO Assistant](scope.md#supplier-approval).
   - `mappings/<framework>.yaml`: manual requirements the mapping does not name yet are added to `manual_only`, with your reasoning as the
     suggested source. Existing lines are never changed.
   - `onboarding/<system>-<framework>-<date>.md`: the record for auditors: the system, the supplier and its levels, the documents kept
     with their hashes, the model and skills (with versions and SHA-256), and every decision with its proposal and who accepted it.

   Nothing is sent to CISO Assistant. Review the files in a pull request, then run `hound scope validate scope`, `hound scope plan scope`
   and `hound scope apply scope`. Running `hound onboard` again for the same system writes nothing; to onboard it against another
   framework, pass that framework's file and it writes a separate audit and record.

`--no-model` runs the same steps with empty proposals, for teams without a key. `--show-requests` prints each request before it is sent.

## Onboard the environment first: which system provides each mitigation

Before onboarding systems one by one, decide once, for each mitigation of the framework, which product actually provides it. Then `hound`
judges a requirement only on the audit of the system that provides it, and never fails an audit because it looked at the wrong system.

```sh
hound onboard environment --repo . --framework frameworks/essential-eight-2023.json --signals signals.json --no-model
```

```sh
hound onboard environment --repo . --graph example-m365 --no-model      # read the signals live, as an m365 collector in hound.yaml
```

- **Signals suggest, you decide.** `--graph <collector>` signs in as that `m365` collector of the deployment config (`--deployment`, default
  `hound.yaml` in the repo), with its own settings and secrets, and makes two read-only Graph reads: subscribed licences
  (`v1.0/subscribedSkus`, enabled or in warning, needs `Organization.Read.All`) and the kinds of Intune configuration
  (`v1.0/deviceManagement/deviceConfigurations`, already granted to the collector). A tenant without Intune has no configuration kinds.
  Nothing is kept as evidence. Or give `--signals`, a JSON file of recorded answers (`source`, `licences` as SKU part numbers,
  `configuration_types` as Intune configuration types), not both. A licence such as Entra ID P2 suggests the Microsoft 365 system
  (`--microsoft-system`, by default the first system whose name has "365") for MFA; a system in `scope/setup.yaml` tagged
  `capabilities: ac, rb` is suggested for those mitigations first. Each suggestion shows the signal and its source, and is recorded with
  the decision, never as the decision.
- **One decision per mitigation.** For each, type `a` to accept the suggestion, `s` to name the systems, `m` for manual evidence, `g` for a
  gap, or `q` to save and stop. It asks once for your name and the role that approves the decisions. A product not onboarded yet can be
  named; you are asked for its supplier.
- **What it writes, for review:** `scope/capabilities.yaml` (see [scope.md](scope.md#capabilitiesyaml)), and in each system's own audit,
  every requirement another system provides as `not_applicable`, "Provided by <system> (audit <ref>)". Nothing is merged into a combined
  audit, rows you wrote by hand are never changed, and running it again adds nothing.
- **What comes next:** each named system not onboarded yet is listed with `hound onboard "<system>"`, whose supplier goes through the
  supplier gate first; each gap is listed as requirements no system's audit answers.

- **Then the model proposes.** Without `--no-model`, after the signals are read, one request through the onboarding gate (the model in
  `onboarding.yaml`, `--config`; requests kept in `onboarding/environment/requests/`; `--show-requests` prints them) sends the mitigations,
  the systems in scope with their descriptions, and the signals, guided by the `environment` skills. The model proposes a system, manual or
  gap for each mitigation with its reasoning, shown as `Model: ...` under the suggestion. Accepting records "from the model's proposal" and
  the reasoning; it is still your decision. A reply about a mitigation not asked about is refused.
- **Gaps in the plan.** `hound scope plan` lists each mitigation decided as a gap under "Capability gaps" (and `gaps` in `--json`): the
  requirements no system's audit answers, never a fail on some system.

The default skill `environment-capabilities` describes how to tell which product is authoritative; organisation skills that apply to the
`environment` step extend it.

## Explain a change for review

`hound explain` writes a plain-English review summary of a plan or a config change, so you approve only what you understand. It reads
the output of `hound scope plan` (Markdown or `--json`), `hound plan`, a diff (a file, or piped in: `git diff | hound explain`), or two
versions of one file (`hound explain before.yaml after.yaml`).

- **Code reads it first.** The counts, the objects and what needs a careful look (requirements newly marked not applicable, deletions,
  removed checks in mappings or profiles, a check removed from a profile that a mapping in `mappings/` still uses, a system
  without an owner, a supplier contract that is not active) come from code, not the model. The plan is the source
  of truth: the summary quotes its counts, and a reply that names an object the plan does not contain is refused (`llm.invalid_reply`).
- **The right skills.** The `explain` step's skills, plus those for what the change touches: third parties get the supplier skills,
  not-applicable rows the applicability skill, profiles the profile authoring skill, mappings and audits the framework's mapping skill
  (`--framework`, default `essential-eight-2023`). `--skill <name>` chooses instead. The summary lists each skill with its source,
  version and SHA-256.
- **Same gate as onboarding.** The model connection from `onboarding.yaml`, the outbound guard and deny terms, `--show-requests`, and a
  record in `onboarding/explain/requests/`. Object IDs, email, IP and MAC addresses and secret values are replaced before anything is sent.
- **Review only.** It never edits config or applies a plan, and collection never uses it. `--no-model` prints only what code found.

## onboarding.yaml

Separate from `hound.yaml`; only `hound onboard` reads it.

| Key | Meaning |
| --- | --- |
| `llm.provider` | Required. `anthropic` or `openai`. |
| `llm.model` | The model. Empty means the provider's default from `onboarding/models.yaml`, shipped beside `hound` and editable without a new build (`claude-opus-5-5` and `gpt-6.2-astra` today). |
| `llm.api_key` | Required. A reference, `env:NAME` or `file:/path`. A key written here is refused (`onboarding.api_key_literal`). |
| `llm.effort` | Optional: `low`, `medium`, `high` or `max`. How hard the model thinks. |
| `llm.max_tokens_per_session` | Optional, default 2,000,000. A session stops asking the model once it has used this many tokens (`llm.budget_reached`). |
| `llm.deny_terms_file` | Optional. A text file of terms, one per line, that must never be sent: your organisation's name, domains, project names. |
| `skills_path` | The organisation's own skills. Default `/config/skills`. |
| `profiles_path` | The organisation's profiles folder. Default `/config/profiles`. |

## What is sent, and what is not

Every request passes one gate before it leaves:

- **The outbound guard** scans the instructions, every input and the schema for the values of the secrets in use, the deny terms, and email,
  IP and MAC addresses. A hit sends nothing and names what matched and where (`llm.blocked_content`), never the matched value. Documentation
  addresses pass (`192.0.2.0/24`, `198.51.100.0/24`, `203.0.113.0/24`, `2001:db8::/32`, loopback, `example.com`), so fictional material can be
  sent.
- **Response shapes, not responses.** A system's API answers are reduced to field names, types and counts. Values are kept only for
  settings-like fields (a few short distinct values, such as `active` and `inactive`) and never for a profile's `identities`, email
  addresses, IDs or free text.
- **A local record.** Every request and reply is saved with the session (`NNN-step.request.json`, `NNN-step.reply.json`), and
  `--show-requests` prints each request before it is sent.
- **Replies are checked.** Each reply must meet the step's JSON schema (`llm.invalid_reply`); a model that declines is `llm.refused`, and a
  provider that cannot be reached is `llm.unreachable`.

## Skills: the assistant's guidance

A skill is a Markdown file with frontmatter that tells the model how to approach a step. Skills only guide: the outbound guard, response
shapes, the named approver for not applicable, the supplier gate, schema checks and profile tests stay in code, and no skill can change them.

```markdown
---
name: our-applicability
description: How we justify not applicable.
version: 1.0.0
applies_to: [applicability]          # supplier, system, applicability, profile, mapping, explain
frameworks: [essential-eight-2023]    # optional: only for these frameworks
overrides: applicability              # optional: replace this default on purpose
---
Guidance for the model, in plain Markdown.
```

- **Defaults** are built into `hound`, from this repository's [`skills/`](../skills/) folder: supplier assessment and tiering, supplier
  assurance documents, applicability and not-applicable justification, Essential Eight ML2 mapping, profile authoring, reading an API
  specification, and reviewing a config change (for `hound explain`).
- **Organisation skills** live in the config repo's `skills/` folder (`skills_path`, mounted read-only like profiles). They add to the
  defaults; one with a default's name is an error unless it says `overrides:`.
- **Choosing:** at each step the model gets every skill whose `applies_to` names the step and whose `frameworks` match, defaults first, within
  a size budget. Over the budget is an error naming the skills, never a silent cut. Specifications and responses are passed separately, as
  material to read.
- **Recorded:** each session's `skills.json` lists every skill used with its source, version and SHA-256.

| Command | Does |
| --- | --- |
| `hound skill list` | Every active skill: source, version, SHA-256, steps and override. |
| `hound skill new <name>` | Writes a skeleton to fill in. |
| `hound skill add <file>` | Validates the skill against the defaults and the folder, then copies it in. |
| `hound skill validate [--deny-terms <file>]` | Checks every skill and refuses (`skill.sensitive_content`) any that holds organisation data such as an email, IP or MAC address or a deny term, since skills are sent to the model provider. |
| `hound skill draft [--repo <repo>] [--only <name>]... [--no-model] [--show-requests]` | Drafts the organisation's own skills (`organisation-profile`, `risk-appetite`, `it-landscape`, `ai-landscape`) from your answers and the repo's systems, suppliers and capability map, through the same gate as onboarding. Identifiers are stripped before writing; each draft starts at version 1 and a re-draft shows the change and bumps it. `--no-model` writes templates with the questions as headings. Drafts go only into the skills folder, for you to review, edit and commit. |

Each takes `--skills <folder>` (default `HOUND_SKILLS_PATH`, else `./skills`).
