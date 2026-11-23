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
     justification and approver in the observation; rows another system meets stay open (`to_do`, `not_assessed`) with `provided_by` naming
     that system, until its own audit shows the requirement met; manual rows are to do. Rows checked
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

## Draft, sample and test the profile

Once applicability is decided, the model drafts the profile for the requirements you accepted as API checks:

```sh
hound onboard profile EXAMPLE-ASSETS --name example-assets \
  --set client_id=00000000-0000-4000-8000-000000000001 --set base_url=https://api.example.com/ \
  --secret client_secret=env:EXAMPLE_ASSETS_SECRET
```

- **Drafted, then checked.** The model writes `profiles/<name>.yaml` (`--profiles`, `--name`; by default the system's name in lower case)
  from the API summary of the session's `--spec` files and the accepted checks, guided by the `profile` skills. Every draft is loaded and
  validated as `hound profile validate` does.
- **Sampled once, with your go-ahead.** It asks before it calls the API (`--sample` answers yes). The draft's queries run once, read-only,
  with the instance settings (`--set name=value`) and secrets (`--secret name=env:VAR` or `file:/path`) you give. A query that fails is a
  problem for the next draft, such as a wrong path. Each answer is kept as a recording with the profile's `identities` fields hashed,
  tokens replaced and the API's address as `{{api}}`; an answer still holding a secret is refused. Only the answers' shapes (field names,
  types, counts and short setting-like values; never identities, IDs or dates) go to the model.
- **You confirm each result.** Each check's result on the sample is shown; type `y` if it is right, or the result it should give. Your
  answers and the recordings are saved as the profile test `profiles/<name>.tests/onboarding.yaml`, and the test runs.
- **A few rounds.** Validation, sample and test problems go back to the model with its previous draft, for at most `llm.max_rounds`
  drafts (default 3; `--rounds` for one run). What is still wrong is listed, and the command exits 1, as a failing `hound profile test` does.
- **Nothing is scheduled.** Review the profile and its test in a pull request, run `hound profile validate <name>` and
  `hound profile test <name>`, then add a `rest` collector instance for it to `hound.yaml`.

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
  every requirement another system provides as an open row (`not_assessed`, `provided_by: [<system>]`), "Met by <system> (audit <ref>) ...; open until that audit shows it met", never as not applicable, so no gap shows as closed. `hound scope plan` lists these rows under "Open dependencies". A requirement the audit's mapping already computes (a target in `hound.yaml`, `--deployment`) is left to the mapping and listed, since a hand-written row would conflict with it (`scope.result_conflict`); take it out of that mapping if the system should not be judged on it. Nothing is merged into a combined
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

## Drive it with an agent: pending, decide, supplier draft, approve

A coding agent (Claude Code, GitHub Copilot) can draft a whole onboarding without answering a prompt. Every command below takes options
instead of questions and answers in JSON with `--json`; without `--json` the same facts print as text for a person. `hound` checks each
step exactly as the interactive flow does. What an agent records is a **draft**: nothing is written to the scope's audits, and nothing is
approved, until a person runs `hound approve`.

```console
$ hound supplier draft --repo . --system EXAMPLE-ASSETS --ref SUP-EXAMPLE --name "Example Assets Ltd" \
    --solution-ref SOL-EXAMPLE-ASSETS --solution-name "Asset inventory" \
    --contract-ref CTR-EXAMPLE-ASSETS --contract-name "Asset inventory subscription" \
    --level "dependency=2:Holds the asset inventory only." --level "maturity=3:Holds an ISO 27001 certificate." --json
$ hound onboard EXAMPLE-ASSETS pending --json --repo . --group 1 --spec ../specs/example-asset-api.openapi.yaml \
    --owner it@example.com --product "Example Asset API" --solution SOL-EXAMPLE-ASSETS
$ hound onboard EXAMPLE-ASSETS decide urn:intuitem:risk:req_node:essential-eight-2023:pa:ism-1807 --repo . --json \
    --kind api_check --reasoning "GET /v1/devices lists every device, discovered daily." --source "GET /v1/devices"
$ hound onboard EXAMPLE-ASSETS pending --json --repo .
```

- **`hound supplier draft`** writes `scope/third-parties/<ref>.yaml` with a draft contract and an open review. Each `--level` is
  `<name>=<value>:<reason>` (dependency and penetration 0 to 4; maturity, trust and criticality 1 to 4); a level outside its scale or
  without a reason is refused. It never overwrites a file.
- **`hound onboard <system> pending`** starts the session when given `--owner`, `--product` and `--solution` (a solution a third-party
  file declares), then lists the requirements not yet decided with their text, the API summary from `--spec`, and the skills that apply
  (the defaults, plus the repo's own under `skills/`). Later calls need only the system and `--repo`.
- **`hound onboard <system> decide <requirement>`** records one decision: `--kind api_check|manual|not_applicable|other_system`,
  `--reasoning`, any `--source` it rests on, `--other-system <name>` for another system, `--justification <text>` for not applicable.
  Name the requirement as `pending` lists it under `requirement` (its `ref_id` when that is unique in the framework, else its URN).
  It is marked `drafted_by: agent` in the session, with no one as its accepter or approver. Deciding a requirement again replaces the
  draft; a decision a person made is never replaced.
- **`--json`** is also on `hound scope validate`, `hound scope plan` and `hound explain` (what code reads from a change: counts, objects,
  flags and topics; no model is asked).

### Approve, in a terminal

```console
$ hound approve --repo .
```

`hound approve` walks you through everything drafted in the repo that needs a person, one item at a time, in this order:

1. **Environment capabilities** in `scope/capabilities.yaml` with no `approved_by`.
2. **Suppliers** whose contract is a draft. Approving makes the contract `active`.
3. **Supplier reviews** that are open, for suppliers with an active contract: conclude each as `ok`, `warning` or `blocker`, once the
   review is done.
4. **Not-applicable decisions** an agent drafted, each with its reasoning and justification.
5. **Each system an agent drafted**, with its decisions. Approving writes the system's audit, its entry in `scope/setup.yaml` and the
   onboarding record, as the interactive command does. A system waits while its supplier is not approved, a not-applicable decision is
   declined, or requirements are still undecided.

Answer `y`, `n` (leave it as a draft) or `s` (stop; nothing more is asked). Your name, asked once, and today's date are recorded on
everything you approve: `approved_by` and `approved_on` on the capability and the contract, `by` and `on` on the review's conclusion,
and the approver in the session and the onboarding record.

**It needs a person at a terminal.** When its input or output is redirected, as when an agent runs it or answers are piped in,
`hound approve` refuses and changes nothing. Nothing is approved in CISO Assistant: review the changed files in a pull request, then
`hound scope plan` and `hound scope apply` write the approved state, including each review's conclusion.

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

## Tutorial: onboard the example API

For a worked example of a whole stack, two systems and four suppliers, see the [onboarding tutorial](examples/onboarding-tutorial.md):
Example Pty Ltd's privately hosted GRC platform on Oracle Cloud and its office firewall, onboarded, reviewed, validated and applied.

This walk-through onboards the fictional Example Asset API into a copy of the template config repo, without a model (`--no-model`), so it
needs no key and sends nothing anywhere. It takes about ten minutes. Run it from the root of this repository.

1. **A config repo to work in.**

   ```sh
   cp -R examples/compliance-repo /tmp/tutorial-repo
   ```

2. **Start onboarding.** Group 1 keeps the list short (48 requirements); for Essential Eight ML2 you would give `--group 1 --group 2`.

   ```sh
   hound onboard EXAMPLE-ASSETS --repo /tmp/tutorial-repo --group 1 \
     --spec examples/apis/example-asset-api.openapi.yaml --no-model
   ```

   Answer the questions: your name, the system owner's email (`owner@example.com`), the product (`Example Asset Service`), the supplier
   (`SUP-EXAMPLE`, `Example Vendor Pty Ltd`), the solution (`SOL-EXAMPLE-ASSETS`, `Example Asset Service`), the contract
   (`CTR-EXAMPLE-ASSETS`, `Example asset service subscription`) and what the supplier provides. Press Enter when asked for a vendor
   document. The supplier is new, so `hound` drafts `scope/third-parties/sup-example.yaml` and stops:

   ```console
   SUP-EXAMPLE is not approved yet. Review scope/third-parties/sup-example.yaml; once the supplier is approved, make contract CTR-EXAMPLE-ASSETS active and run 'hound onboard EXAMPLE-ASSETS' again to decide applicability.
   ```

3. **Approve the supplier.** Open the drafted file, fill in the levels it leaves to decide, and set the contract to `status: active`. In a
   real repo you would now apply the supplier and conclude its review `EA-SUP-EXAMPLE` as ok in CISO Assistant (see
   [Supplier approval](scope.md#supplier-approval)); the tutorial skips that, because onboarding itself checks only the contract.

4. **Decide applicability.** Run the same command again. It shows what the API offers, then each requirement with "No proposal", since no
   model is asked. Type `c` for ISM-1807 (the API lists devices and their last check-in) with a reason, and `m` with a reason for the rest.
   Type `s` at any point to stop; the next run carries on from there. At the end, accept the default domain and perimeter:

   ```console
   All 48 requirements decided: 1 API check(s), 47 manual, 0 not applicable, 0 for another system. The session is in onboarding/EXAMPLE-ASSETS/session.json.
   Wrote scope/audits/example-assets-essential-eight-2023.yaml, scope/setup.yaml, onboarding/example-assets-essential-eight-2023-<date>.md. ...
   ```

5. **Review what it wrote.** Read the audit, the system's new `solution:` line in `scope/setup.yaml` and the onboarding record, as a
   reviewer would in a pull request, then check the scope files offline:

   ```sh
   cd /tmp/tutorial-repo && hound scope validate scope
   ```

6. **The profile.** With a model, `hound onboard profile EXAMPLE-ASSETS` drafts it (see [Draft, sample and test the
   profile](#draft-sample-and-test-the-profile)). Without one, copy the library's example profile and its tests, and run them offline:

   ```sh
   mkdir -p profiles && cp -R <evidence-hound>/profiles/example.yaml <evidence-hound>/profiles/example.tests profiles/
   hound profile test example --profiles profiles
   ```

To try the model steps, run `hound onboard config` with your own key and repeat from step 2 without `--no-model`, adding
`--show-requests` to see each request before it is sent.

## onboarding.yaml

Separate from `hound.yaml`; only `hound onboard` reads it.

| Key | Meaning |
| --- | --- |
| `llm.provider` | Required. `anthropic` or `openai`. |
| `llm.model` | The model. Empty means the provider's default from `onboarding/models.yaml`, shipped beside `hound` and editable without a new build (`claude-opus-5-5` and `gpt-6.2-astra` today). |
| `llm.api_key` | Required. A reference, `env:NAME` or `file:/path`. A key written here is refused (`onboarding.api_key_literal`). |
| `llm.effort` | Optional: `low`, `medium`, `high` or `max`. How hard the model thinks. |
| `llm.max_tokens_per_session` | Optional, default 2,000,000. A session stops asking the model once it has used this many tokens (`llm.budget_reached`). |
| `llm.max_rounds` | Optional, default 3. The most drafts the model makes of a profile in `hound onboard profile`, each checked and its problems sent back. |
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
