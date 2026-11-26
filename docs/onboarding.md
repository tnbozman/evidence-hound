# Onboarding

`hound onboard` takes a GRC team member through onboarding a system: the supplier first, then how each requirement of a framework is
met for the system. The output is files for a pull request in the config repo; nothing is applied to CISO Assistant until a person reviews
it and runs `hound scope validate`, `plan` and `apply`.

**hound has no language model of its own.** There is no API key, provider or token budget to manage. For model help, set up your coding
assistant (Claude Code or GitHub Copilot) with [`hound agent setup`](#set-up-your-coding-assistant-hound-agent-setup): it reads your
documents and API specifications, drafts through `hound` commands that check everything it writes, and you approve with `hound approve`
in a terminal. Without an assistant, every step below works by answering `hound`'s questions yourself. The design is in
[design/agent-driven-onboarding.md](design/agent-driven-onboarding.md); it supersedes the earlier
[onboarding assistant design](https://github.com/tnbozman/evidence-hound/blob/d05f4d042885553cfd94a16a8189b535be63b84e/docs/design/onboarding-assistant.md), in which `hound` called a model itself.

**No model in evidence collection, and no evidence without a person.** `hound run`, `hound serve` and every collector are deterministic
code, and the runner refuses a deployment config (`hound.yaml`) that has an `llm` section (`config.llm_in_runner`). For manual evidence a
coding assistant may read a file and propose its result with `hound manual propose`, but the result becomes evidence only when a person
attests it with `hound manual approve` ([manual sources](configuration.md#manual-sources)).

Removed with the built-in model: `hound onboard config`, `onboarding.yaml`, and the options `--no-model`, `--show-requests` and (for the
onboarding config) `--config`. `hound onboard config` now only says so. The options are still taken, so an older script keeps working;
each does nothing and prints a line pointing to `hound agent setup`.

## Onboard a system

```console
$ hound onboard EXAMPLE-ASSETS --framework frameworks/essential-eight-2023.json --group 1 --group 2 --spec ../specs/example-asset-api.openapi.yaml
```

Run it in the config repo (or pass `--repo`). The session goes in this order, and saves after every answer in
`onboarding/<system>/session.json`. Run the same command again to carry on where it stopped.

1. **Who and what.** Your name (recorded as who accepted each decision), the system owner, the product, and who supplies it: the supplier,
   the solution it provides and the contract covering it, each by `ref_id` and name.
2. **Supplier first.** A supplier not yet under `scope/third-parties/` is drafted there. Dependency, penetration, maturity and
   trust, and the solution's criticality, are each asked with their scale printed first (see
   [What the terminal tells you](#what-the-terminal-tells-you)); a level you leave empty stays in the file to decide (a coding assistant
   drafts them with their reasons through `hound supplier draft`). What the supplier provides, in your words, is kept in the session. Vendor documents you keep (an https URL or a file) are saved under `documents/suppliers/<supplier>/` with
   their SHA-256 and retrieval date. The contract is drafted as
   `status: draft`, and the session stops: **a system is onboarded only once its supplier is approved.** Onboarding checks the scope
   files for an active contract covering the solution; `hound scope plan` then checks approval in CISO Assistant, where the GRC team
   concludes the supplier's assessment (see [Supplier approval](scope.md#supplier-approval)). A drafted supplier file includes that
   review (`EA-<supplier>`, due a month from today, then every 12 months, in the first domain and perimeter of `scope/setup.yaml`). So the
   order is: approve the contract in the file, apply the supplier, conclude its review as ok in CISO Assistant, then finish onboarding
   and apply the system.
3. **What the API offers.** Each `--spec` (an OpenAPI 3 document, JSON or YAML) is read into a short summary: the endpoints that read,
   sign-in and paging.
4. **Applicability.** Every requirement in the `--group` implementation groups (all of them without `--group`) gets one decision. Give the
   groups as your audits declare them, so the system's audit matches: for Essential Eight ML2 that is `--group 1 --group 2`. Each requirement gets one of `api_check`,
   `manual`, `not_applicable` or `other_system`, with its reasoning. For each, type `c`, `m`, `n` or `o` to decide (`a` accepts a
   proposal where a session has one, and `l` every remaining one that is not `not_applicable`), or `s` to save and stop. Not applicable always needs a justification
   and the name of who approves it; another system needs that system's name.

5. **Written for review.** Once every requirement is decided, it asks for the domain and perimeter of the system's audit. These are
   names, not yes or no: the prompt lists the ones `scope/setup.yaml` declares, Enter takes the first, and any other answer is asked
   again. It then writes, into the working copy only:
   - `scope/audits/<system>-<framework>.yaml`: the system's own audit. Not applicable rows are done with result `not_applicable` and the
     justification and approver in the observation; rows another system meets stay open (`to_do`, `not_assessed`) with `provided_by` naming
     that system, until its own audit shows the requirement met; manual rows are to do. Rows checked
     through the API are left to the collector (`unlisted: not_assessed` until its results are published). The audit lists the system.
     When the repo has an audit series, as `hound init` names it in `scope/setup.yaml` (`audit_series`, `EX-E8` by quarter) or an
     organisation audit that runs in periods, the system's audit joins it: its ref ID is the series and the system's ref ID without `SYS-`
     (`EX-E8-SOPHOS-NETWORK`), on the same cycle, so a period reads `EX-E8-Q2-2027FY-SOPHOS-NETWORK`
     ([Audit periods](scope.md#audit-periods)). Without a series it is `<SYSTEM>-<FRAMEWORK>`, with no cycle. Its target is added to
     `hound.yaml` when the repo has the system's own mapping (`mappings/<system>-<framework>.yaml`); the framework's general mapping maps
     every collector's checks, so another system's results would land in this system's audit.
   - `scope/setup.yaml`: the system is declared in the audit's domain with `solution:` set to the supplier's solution, or, if it is already
     declared, `solution:` is added to it (a different solution already there stops the write). That is what lets `hound scope plan` hold
     the system back until its supplier is [approved in CISO Assistant](scope.md#supplier-approval).
   - `mappings/<framework>.yaml`: manual requirements the mapping does not name yet are added to `manual_only`, with your reasoning as the
     suggested source. Existing lines are never changed.
   - `onboarding/<system>-<framework>-<date>.md`: the record for auditors: the system, the supplier and its levels, the documents kept
     with their hashes, and every decision with who drafted it and who accepted it.

   Nothing is sent to CISO Assistant. Review the files in a pull request, then run `hound scope validate scope`, `hound scope plan scope`
   and `hound scope apply scope`. Running `hound onboard` again for the same system writes nothing; to onboard it against another
   framework, pass that framework's file and it writes a separate audit and record.

### What the terminal tells you

Before the first question, `hound onboard` prints the three steps ahead (the supplier, applicability, the files) and reminds you that nothing reaches CISO Assistant until `hound scope apply`. It refuses to start, before writing anything, in a folder with no `scope/setup.yaml`, because that is not a compliance repo. Without `--spec` it says that it cannot list what the system's API offers, so you can stop and add one.

Every opening question comes with a line that says what the answer is for and gives an example, such as `The service, and what depends on it, e.g. "Perimeter firewall; all office internet traffic passes through it"`. A ref ID's prompt ends with its prefix (`Supplier ref_id: SUP-`), so you type only the rest; see [Ref ID conventions](scope.md#ref-id-conventions).

After the vendor documents, each supplier level is asked with its scale printed first. Answer with a number and, when asked, a sentence saying why; the reason is written beside the level in the supplier file with your name. Leave a level empty to decide it later in the file. These are the scales, as the terminal prints them. Dependency, penetration, maturity and trust follow EBIOS Risk Manager's stakeholder criteria, which CISO Assistant's third-party records use.

**dependency**: how much the system relies on this supplier to keep working.

| Value | Meaning |
| --- | --- |
| 0 | None: nothing stops if the supplier does. |
| 1 | Low: the supplier is useful, and work carries on without it. |
| 2 | Moderate: work is harder without it, and another supplier could step in. |
| 3 | High: the supplier is needed, though it could be replaced with effort. |
| 4 | Total: the supplier is needed, and nothing could replace it in the short term. |

**penetration**: how far the supplier can reach into your systems and data.

| Value | Meaning |
| --- | --- |
| 0 | None: no access to your systems or data. |
| 1 | Access as an ordinary user, or to user devices only. |
| 2 | Administrator access to user devices, or physical access to your premises. |
| 3 | Administrator access to business systems or servers. |
| 4 | Administrator access to core infrastructure, or physical access to server rooms. |

**maturity**: how well the supplier manages its own cyber security.

| Value | Meaning |
| --- | --- |
| 1 | Security is handled case by case, with nothing written down. |
| 2 | Good practice and regulation are followed, without one overall policy. |
| 3 | An overall security policy is applied, and problems are mostly handled as they arise. |
| 4 | Risk is managed under an overall policy, ahead of problems. |

**trust**: how sure you are that the supplier's intentions and interests line up with yours.

| Value | Meaning |
| --- | --- |
| 1 | Its intentions cannot be judged. |
| 2 | Its intentions are taken to be neutral. |
| 3 | Its intentions are known and probably good. |
| 4 | Its intentions are known and fully in line with yours. |

**criticality**: how much the business depends on what the supplier provides (the solution).

| Value | Meaning |
| --- | --- |
| 1 | Low: the business carries on without it. |
| 2 | Medium: some work is slower or harder without it. |
| 3 | High: important work stops without it. |
| 4 | Critical: the business cannot run without it. |

Maturity, trust and criticality have no 0: a level you leave empty is not yet assessed, and stays in the file as a comment to decide.

## Validate, sample and test the profile

Once applicability is decided, write the profile for the requirements you accepted as API checks: by hand ([REST profiles](collectors/rest.md)),
or ask your coding assistant, whose `write-a-profile` skill iterates with `hound profile validate` and `hound profile test`. Then check it
against the real API yourself:

```sh
hound onboard profile EXAMPLE-ASSETS --name example-assets \
  --set client_id=00000000-0000-4000-8000-000000000001 --set base_url=https://api.example.com/ \
  --secret client_secret=env:EXAMPLE_ASSETS_SECRET
```

- **Checked as written.** It reads `profiles/<name>.yaml` (`--profiles`, `--name`; by default the system's name in lower case), which
  must exist, and loads and validates it as `hound profile validate` does. It never changes the profile.
- **Sampled once, with your go-ahead.** It asks before it calls the API (`--sample` answers yes). The profile's queries run once, read-only,
  with the instance settings (`--set name=value`) and secrets (`--secret name=env:VAR` or `file:/path`) you give. A query that fails is
  listed, such as a wrong path. Each answer is kept as a recording with the profile's `identities` fields hashed, tokens replaced and the
  API's address as `{{api}}`; an answer still holding a secret is refused.
- **You confirm each result.** Each check's result on the sample is shown; type `y` if it is right, or the result it should give. Your
  answers and the recordings are saved as the profile test `profiles/<name>.tests/onboarding.yaml`, and the test runs.
- **What is still wrong is listed,** and the command exits 1, as a failing `hound profile test` does. Fix the profile (or have your
  assistant fix it from the listed problems) and run it again.
- **Nothing is scheduled.** Review the profile and its test in a pull request, run `hound profile validate <name>` and
  `hound profile test <name>`, then add a `rest` collector instance for it to `hound.yaml`.

## Onboard the environment first: which system provides each mitigation

Before onboarding systems one by one, decide once, for each mitigation of the framework, which product actually provides it. Then `hound`
judges a requirement only on the audit of the system that provides it, and never fails an audit because it looked at the wrong system.

```sh
hound onboard environment --repo . --framework frameworks/essential-eight-2023.json --signals signals.json
```

```sh
hound onboard environment --repo . --graph example-m365      # read the signals live, as an m365 collector in hound.yaml
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

- **With a coding assistant,** the same decisions are drafted through `hound onboard environment pending` and `decide`
  ([every kind of onboarding](#every-kind-of-onboarding-the-same-way)) and approved with `hound approve`.
- **Gaps in the plan.** `hound scope plan` lists each mitigation decided as a gap under "Capability gaps" (and `gaps` in `--json`): the
  requirements no system's audit answers, never a fail on some system.

The default skill `environment-capabilities` describes how to tell which product is authoritative; organisation skills that apply to the
`environment` step extend it. Both reach a coding assistant through its skill files and `pending --json`.

### Microsoft 365: which capabilities it provides

Run it with `--graph <m365 collector>` to read the tenant's licences and Intune configuration kinds live, read-only, as that collector of
`hound.yaml`. Microsoft 365 is then suggested for the mitigations they cover. An accepted suggestion is written with `evidence: collector`
and `route: <collector>`, so the collector's checks for that capability run; a mitigation you give to another product (an application
control or identity product, for example) is routed away from Microsoft 365, and its checks there answer not applicable, naming that
product. See [Capabilities](collectors/m365.md#capabilities). With `--signals` (recorded answers) nothing is routed, because no collector
read them; set `route` by hand, or `system` on the collector.

## Set up your coding assistant: hound agent setup

```console
$ hound agent setup --repo . --assistant both
```

`hound agent setup` writes the files a coding assistant reads in the compliance repo, so that it can onboard systems and suppliers, import
an audit workbook, write a profile and review a plan before you apply it. Without `--assistant` it asks which one you use in VS Code:
`claude`, `copilot` or `both`. `hound init` offers the same at its end (or takes `--assistant`).

| Assistant | Files |
| --- | --- |
| Claude Code | `CLAUDE.md`; `.claude/skills/<skill>/SKILL.md`; `.claude/agents/hound-onboarding.md`, `hound-review.md`, `hound-decisions.md` and `hound-operations.md` |
| GitHub Copilot in VS Code | `.github/copilot-instructions.md`; `.github/agents/hound-onboarding.agent.md`, `hound-review.agent.md`, `hound-decisions.agent.md` and `hound-operations.agent.md`; `.github/skills/<skill>/SKILL.md` |

With `both`, the skills are written once, under `.claude/skills/`, which Copilot in VS Code reads too.

- **Twenty skills, from one source, covering every hound command.** Onboarding: onboard the organisation, the environment, a third
  party, a system and an audit; assess a supplier; read an API specification; import an audit workbook; write a profile. Upkeep:
  process manual evidence, prepare the weekly review, work through pending decisions with you, review before apply, operate the
  collectors, keep frameworks, libraries, mappings, profiles and these files current. Documents and response: build the SSP and POA&M,
  respond to an incident and build the incident response plan, prepare the committee and board reports, answer a prime's
  questionnaire, move registers from spreadsheets. Each is the steps for driving `hound`, linking the guide it follows, and where hound
  has default guidance for that step (the same text `hound skill list` shows), that guidance too.
- **A command reference built from hound itself.** The skill `hound-commands` lists every command and its options, generated from the
  hound that ran `hound agent setup`, so it matches what is installed. Every agent loads it. Run `hound agent setup` again after
  upgrading hound.
- **Four agents, with the least tools each needs.** `hound-onboarding` reads, searches, edits and runs commands. `hound-review` reads,
  searches and runs commands; it cannot edit a file. `hound-decisions` takes you through the decisions waiting on you, one at a time,
  highest risk first, and gives you the `hound review --only <key>` to accept or challenge each; it records recommendations and edits
  no file. `hound-operations` runs the collectors, the SSP, incidents, frameworks and libraries, and the reports.
- **Every file opens with the same rules:** organisation data stays in this private repo; never open `secrets/`; never approve on the
  person's behalf; nothing reaches CISO Assistant except through `hound scope plan` and `hound scope apply`. The rules guide the
  assistant; what enforces them is `hound` itself: `hound approve` refuses anything but a person at a terminal.
- **Your files are yours.** Each file hound writes carries a comment naming hound and its version. Running the command again replaces
  those files (`Updated`), and leaves alone any file without the comment (`Kept`), including a `CLAUDE.md` you already had. To take a
  file over, delete the comment; to get hound's version back, delete the file and run the command again.

Your assistant's provider sees what the assistant reads in the repo. Keep secrets out of it, as the rules say.

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

### Every kind of onboarding, the same way

The agent drafts each kind through a command that asks nothing; `hound` checks it; a person approves with `hound approve`.

| What | Command | Where the draft waits |
| --- | --- | --- |
| The organisation | `hound init <folder> --org ... --code ... --url ... --time-zone ... --level ... --interval ... --owner ... --assistant ... --json` | The new repo itself, for review |
| The environment | `hound onboard environment pending --json`, then `hound onboard environment decide <mitigation> --provided-by system\|manual\|gap --reason ... [--system ...]` | `scope/capabilities.yaml`, with no approver |
| A third party | `hound supplier draft --kind vendor\|reseller\|msp [--for <vendor>] ... [--document <path>]...` | `scope/third-parties/<ref>.yaml`, with a draft contract |
| A system | `hound onboard <system> pending` and `decide` | `onboarding/<SYSTEM>/session.json` |
| An audit | `hound audit draft --ref ... --name ... --framework ... --group ... --domain ... --perimeter ... --system ... --due ...` | `onboarding/drafts/audits/<ref>.yaml` |
| An audit workbook | `hound scope import <workbook> --audit <ref> [--sheet ...] [--column <name>=<heading>]...` | Approved rows go to the audit; the rest wait in `onboarding/drafts/imports/<audit>.json` |
| Manual evidence | `hound manual propose <source> <file> --result ... --observation ... --supports ...` | `<file>.proposal.yaml` beside the file in the drop folder, until `hound manual approve` |

- **The environment.** `pending` lists the mitigations not yet decided, the drafts waiting and the systems in scope. `decide` records one
  mitigation; deciding it again replaces the draft, and a decision a person approved is refused. Once every mitigation is decided
  and the decisions are approved, `hound onboard environment` asks nothing and writes the rows each system's audit leaves open for
  another system. While any mitigation is undecided it asks a person for each one, so an agent checks `pending` is empty first.
- **A reseller or managed service provider** is a third party of its own. Draft the vendor first, then the reseller or provider with
  `--kind` and `--for <vendor ref_id>`; its description names the vendor it stands for. `--document` names a document kept in the repo
  that the draft rests on, and the file records it with its SHA-256. A system bought through one names it as `supplied_through`.
- **An audit** is drafted per system, with a shared platform audited on its own ([one audit per system](scope.md#one-audit-per-system-and-one-per-shared-platform)). It is checked against the scope as it is drafted: its framework, domain, perimeter and systems must be in `scope/setup.yaml`.
  It stays out of the scope, and out of every plan, until `hound approve` moves it into `scope/audits/`.
- **An audit workbook** (`.xlsx` or `.csv`) is read where it is; it is never copied into the repo. `hound` finds the columns by their
  headings (requirement, result, observation, evidence, approved by, approved on) or by `--column`. A row the workbook says someone
  approved is written to the audit's `assessments` with the workbook's own words, ending "Imported from `<workbook>`, row `<n>`, approved
  by `<who>` on `<date>`", so the provenance shows in `hound scope plan` and in CISO Assistant. A row with no approver is a draft: `hound
  approve` asks about it, and records the person who approves it, today. Rows not assessed are left out; rows whose requirement is not in
  the audit's framework are listed as unmatched and never guessed. Importing again adds nothing twice. Try it with
  [`examples/workbooks/example-audit-workbook.csv`](../examples/workbooks/README.md).

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
6. **Audits an agent drafted** with `hound audit draft`. Approving moves the audit into `scope/audits/`, with your name and the date in
   its first line.
7. **Imported workbook rows with no approver.** Approving writes the row to its audit, with you as its approver.

Answer `y`, `n` (leave it as a draft) or `s` (stop; nothing more is asked). Your name, asked once, and today's date are recorded on
everything you approve: `approved_by` and `approved_on` on the capability and the contract, `by` and `on` on the review's conclusion,
and the approver in the session and the onboarding record.

**It needs a person at a terminal.** When its input or output is redirected, as when an agent runs it or answers are piped in,
`hound approve` refuses and changes nothing. `hound manual approve` is the same walk for manual evidence only: it shows each file an
agent proposed a result for, and you attest, change or reject it ([manual sources](configuration.md#manual-sources)). Nothing is approved in CISO Assistant: review the changed files in a pull request, then
`hound scope plan` and `hound scope apply` write the approved state, including each review's conclusion.

**Or sign off once a week.** `hound review` walks the same items, and manual evidence proposals, in one session: most urgent first, each
with why it matters and the agent's recommendation, answered accept, challenge or skip. It records one sign-off and applies what you
accepted. See [the weekly review](manual-evidence.md#the-weekly-review).

## Explain a change for review

`hound explain` writes a plain-English review summary of a plan or a config change, so you approve only what you understand. It reads
the output of `hound scope plan` (Markdown or `--json`), `hound plan`, a diff (a file, or piped in: `git diff | hound explain`), or two
versions of one file (`hound explain before.yaml after.yaml`).

- **Code reads it.** The counts, the objects and what needs a careful look (requirements newly marked not applicable, deletions,
  removed checks in mappings or profiles, a check removed from a profile that a mapping in `mappings/` still uses, a system
  without an owner, a supplier contract that is not active) come from code. The plan is the source of truth: the summary quotes its
  counts. No model is asked.
- **For an agent,** `--json` gives the same facts (counts, objects, flags and topics); the `review-before-apply` skill has a coding
  assistant read them with the plan and the changed files before it proposes an apply.
- **Review only.** It never edits config or applies a plan, and collection never uses it.

## Tutorial: onboard the example API

For a worked example of a whole stack, two systems and four suppliers, see the [onboarding tutorial](tutorials/onboarding-tutorial.md):
Example Pty Ltd's privately hosted GRC platform on Oracle Cloud and its office firewall, onboarded, reviewed, validated and applied.

This walk-through onboards the fictional Example Asset API into a copy of the template config repo by answering `hound`'s questions
yourself: it needs no key and sends nothing anywhere. It takes about ten minutes. Run it from the root of this repository.

1. **A config repo to work in.**

   ```sh
   cp -R examples/compliance-repo /tmp/tutorial-repo
   ```

2. **Start onboarding.** Group 1 keeps the list short (48 requirements); for Essential Eight ML2 you would give `--group 1 --group 2`.

   ```sh
   hound onboard EXAMPLE-ASSETS --repo /tmp/tutorial-repo --group 1 \
     --spec examples/apis/example-asset-api.openapi.yaml
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

4. **Decide applicability.** Run the same command again. It shows what the API offers, then each requirement with "No proposal": the
   decisions are yours. Type `c` for ISM-1807 (the API lists devices and their last check-in) with a reason, and `m` with a reason for the rest.
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

6. **The profile.** Copy the library's example profile and its tests, and run them offline (for your own system you would write the
   profile, or have your coding assistant write it, then [validate, sample and test it](#validate-sample-and-test-the-profile)):

   ```sh
   mkdir -p profiles && cp -R <evidence-hound>/profiles/example.yaml <evidence-hound>/profiles/example.tests profiles/
   hound profile test example --profiles profiles
   ```

To do the same with a coding assistant, run `hound agent setup` in the repo and ask it to onboard the system with hound.

## Skills: the assistant's guidance

A skill is a Markdown file with frontmatter that says how to approach a step. `hound` gives the skills for a step to a coding assistant
with `hound onboard <system> pending --json`, and `hound agent setup` writes the defaults into the assistant's own skill files. Skills only
guide: the named approver for not applicable, the supplier gate, `hound approve` and profile tests stay in code, and no skill can change them.

```markdown
---
name: our-applicability
description: How we justify not applicable.
version: 1.0.0
applies_to: [applicability]          # supplier, system, applicability, profile, mapping, explain
frameworks: [essential-eight-2023]    # optional: only for these frameworks
overrides: applicability              # optional: replace this default on purpose
---
Guidance, in plain Markdown.
```

- **Defaults** are built into `hound`, from this repository's [`skills/`](../skills/) folder: supplier assessment and tiering, supplier
  assurance documents, applicability and not-applicable justification, Essential Eight ML2 mapping, profile authoring, reading an API
  specification, and reviewing a config change (for `hound explain`).
- **Organisation skills** live in the config repo's `skills/` folder (`skills_path`, mounted read-only like profiles). They add to the
  defaults; one with a default's name is an error unless it says `overrides:`.
- **Choosing:** a step gets every skill whose `applies_to` names the step and whose `frameworks` match, defaults first, within
  a size budget. Over the budget is an error naming the skills, never a silent cut.

| Command | Does |
| --- | --- |
| `hound skill list` | Every active skill: source, version, SHA-256, steps and override. |
| `hound skill new <name>` | Writes a skeleton to fill in. |
| `hound skill add <file>` | Validates the skill against the defaults and the folder, then copies it in. |
| `hound skill validate` | Checks every skill: frontmatter, steps, size and overrides. |
| `hound skill draft [--only <name>]...` | Writes the organisation's own skills (`organisation-profile`, `risk-appetite`, `it-landscape`, `ai-landscape`) as templates with their questions as headings, for you or your coding assistant to answer. A skill already in the folder is kept as it is. Templates go only into the skills folder, for you to review, edit and commit. |

Each takes `--skills <folder>` (default `HOUND_SKILLS_PATH`, else `./skills`).
