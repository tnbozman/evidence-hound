# s10 Third parties as configuration

Status: In progress

**Goal:** suppliers, their solutions and contracts, and supplier assessments live in the private configuration repo and are applied to CISO Assistant by `hound`, replacing the third-party spreadsheet. The evidence plan (which controls are evidenced automatically or manually, by whom and how often) is applied the same way, as recurring CISO Assistant tasks, so manual evidence and supplier reviews are chased by CISO Assistant's own reminders.

The governance collector and metrics, planned here before the release 1 reorder, moved to [s15](s15-governance-and-metrics.md) in release 2.

Builds on s06 (scope files, plan, apply, inventory, framework libraries for questionnaires). Everything is matched by `ref_id`, or by the key CISO Assistant uses where it has no `ref_id` (name and domain; email for representatives). All examples use the fictional Example Pty Ltd and `example.com`.

What is configuration here, and what is not:

| Configuration in git, applied by `hound` | Stays in CISO Assistant |
| --- | --- |
| Entities, solutions, contracts, representatives (optional), supplier assessment set-up and review cycle, forced tiers with reasons | Questionnaire answers, tiering form results and tier approvals, tier history, campaigns |
| Evidence plan: evidence items, recurring task templates (assignees, schedule, runbook), owner contacts | Task occurrences and their status, uploaded evidence revisions, reviews, requirement results set by the Cyber GRC team |

### s10-01 Suppliers, solutions and contracts

Moved to s09 as s09-19 on 2026-11-22, so suppliers are registered and approved before systems are onboarded.

### s10-02 Supplier assessments and tiers

Moved to s09 as s09-20 on 2026-11-22, with a supplier approval gate added. The recurring supplier review task stays here, in s10-04.

### s10-03 Documentation and example repo
- [x] Done

**Acceptance criteria**
- `docs/third-parties.md`: file formats, validation, plan and apply, privacy guidance for representatives, and what stays in CISO Assistant (questionnaire answers, tiering results, campaigns).
- The example compliance repo (s06-12) gains two fictional suppliers and a supplier assessment, all of which validate.

**Tests first**
- `ExampleRepoTests.Example_compliance_repo_validates` covers the new files.
- Docs lint and link check.

**Functional check**
- Following the guide verbatim, add a supplier to the example repo and apply it.

**As built (2026-11-23)**
- `docs/third-parties.md`: the how-to for a small team and its agent (spreadsheet, vendor documents and trust centre pages in; `hound supplier draft`, validate, `hound approve`, plan and apply), keeping it current, representatives as personal data, and what stays in CISO Assistant. The scope reference links to it.
- Deviation: the example compliance repo stays free of suppliers, as s09-32 decided (the onboarding tutorial is a copy of the template plus its own files, and `ExampleRepoTests.The_template_holds_none_of_the_tutorials_systems` keeps it so). The tutorial's four fictional suppliers, which validate on a copy of the template, are the guide's worked example. The template turns `contracts` on in `settings.yaml`.
- From the rehearsal: `hound supplier draft` takes `--description`, `--start`, `--end`, `--annual-expense` and `--currency` (dates checked), and `--kind msp` without `--for` for a provider that runs the organisation's own systems. The supplier-assessment skill points to its own assurance section rather than a skill that does not exist.

**Rehearsal (2026-11-23), persona: five-person machine shop going for CMMC Level 2**
- Inputs: a fictional six-row supplier spreadsheet (MSP, cloud mail, CAD licences, the prime's transfer portal, payroll, cloud backup), two saved trust centre pages and the owner's note. An agent with only the repo (`hound init`, `hound agent setup`) and the guide drafted all six suppliers and kept the two pages; scope valid. It found the decisive fact for this shop: drawings marked CUI arrive through a commercial mail plan its provider says is not authorised for CUI.
- Verdict: the register itself needs no consultant. What CMMC Level 2 turns on cannot yet be recorded or shown to a prime: which suppliers handle CUI and on what authority, a CUI-scoped perimeter, and an NIST SP 800-171 audit. Listed under Discovered work.
- Functional: no CISO Assistant in the cloud session; supplier apply is covered by CI's functional suite (s09-19, s09-20).

### s10-04 Evidence plan and manual evidence tasks
- [ ] Done

**Story:** As the Cyber GRC team, I want the evidence plan declared in the repo and applied as recurring CISO Assistant tasks, so that system owners are emailed their runbook before evidence is due and the committee can see who is not providing it, without owners needing a CISO Assistant login.

How it works, using CISO Assistant's own features only (checked against the v4.3.4 source):
- A recurring task template (`/api/task-templates/`, keyed by `ref_id`) holds the assignees, the schedule, the runbook as its Markdown description, and the linked evidence, applied control and requirement assessments. CISO Assistant creates an occurrence per due date.
- CISO Assistant emails assignees 30, 7 and 1 days before each due date (with the description and a link) and daily while an occurrence is overdue. A team assignee is emailed at its team email as well as its leader, deputies and members.
- Escalation is the monthly committee, not email (person's decision, Nov 2026): the first email carries the instructions, the later ones are the warning, and once overdue the owner gets no more email. The example `settings.yaml` turns the daily overdue email off with the general setting `disabled_email_templates: [task_node_overdue]`; overdue occurrences become open actions in the committee report (s11-05).
- Only the Cyber GRC team uses CISO Assistant. A system owner is an **owner contact**: a team with a team email and no members, so it receives the reminders but has no login. The owner sends evidence to the GRC team as the runbook says; GRC uploads it against the occurrence, marks it complete, reviews it and sets the requirement result.
- `/api/task-templates/analytics/` counts tasks by assignee and by domain, with overdue and due-soon buckets; the s11 reports read it.

**Acceptance criteria**
- `scope/evidence-plan.yaml` declares one row per control or requirement: `ref_id`, `control` (applied control `ref_id`), `requirements` (optional), `method` (`automated`, `manual` or `hybrid`), `collector` (instance name, for `automated` and `hybrid`), `owner` (a team from `access.yaml`), `grc_team` (default from the file's top level), `every` (`3 months`, `6 months` or `12 months`) and `runbook` (a Markdown file in the repo).
- `access.yaml` teams gain an optional `email` (CISO Assistant's team email). A team with an email and no members or role assignments is an owner contact.
- `scope apply` creates or updates, by `ref_id`, the evidence item (owner, filtering labels `method:<method>` and `collector:<name>`) and, for `manual` and `hybrid` rows, a recurring task template: assignees owner and GRC team, schedule `MONTHLY` with interval 3, 6 or 12 (12 months as `YEARLY`, 1), description from the runbook, links to the evidence, applied control and requirement assessments.
- Validation: a `manual` or `hybrid` row needs an owner, an interval and a runbook file that exists; an `automated` or `hybrid` row names a configured collector instance; owner and GRC teams exist in `access.yaml`; an owner contact has no role assignments.
- `scope plan` shows task template changes and drift like other objects. Occurrence status, evidence revisions, reviews and requirement results are never in the plan or written by `hound`.
- Manual rows stay `manual_only` in the mapping files, so runs never overwrite a result set by the GRC team.
- Supplier reviews (moved from s10-02): an entity assessment's `review_every` (s09-20) also creates a recurring task template for the supplier owner and the GRC team, so supplier reviews get CISO Assistant's reminders and appear in the same overdue reporting.
- `docs/manual-evidence.md`: the plan file, a runbook template (what to export, from where, how to send it to the GRC team), turning mail on (`EMAIL_*` in the deployment, `notifications_enable_mailing` in `settings.yaml`) and the overdue email off (`disabled_email_templates`), the GRC team's steps in CISO Assistant, and the limits below.
- The example compliance repo gains a fictional evidence plan with two manual rows, an owner contact and a runbook, all of which validate.

**Templates for the remaining Essential Eight requirements (moved from s09-05 and s09-06, 2026-11-23)**
- `examples/manual-sources/essential-eight/` holds one manual source per remaining area (at least user application hardening, administrative privilege reviews, privileged account restrictions, event log retention, and backup restore tests; the maintainer does not use Veeam, so backup evidence is manual). Each has a config snippet, the check IDs it supports, and a README (what to export, from where, how often, who attests) that is the runbook its evidence plan row points to, so one file drives both the drop folder and the CISO Assistant task.
- `hound manual template --source <name>` writes an attestation skeleton (`<file>.attest.yaml`) listing that source's check IDs with empty outcomes, for `hound manual approve` (s09-42) to fill in at a terminal.
- `docs/collectors/manual-templates.md` indexes the templates by Essential Eight strategy.
- **Decided (maintainer, 2026-11-23): attested in the terminal, published by runs.** Manual check IDs are mapped in the Essential Eight mappings, so a person attests with `hound manual approve` (s09-42) and `hound run` publishes the result; one attestation can then answer several frameworks and a prime's questionnaires. This replaces the criterion above that manual rows stay `manual_only`: only rows no manual source covers stay `manual_only`, and the GRC team still reviews each result in CISO Assistant.
- Tests (from s09-05): `ManualTemplateTests.Skeleton_lists_source_checks_with_empty_outcomes`, `ExampleManualSourcesTests.Every_template_config_validates`, `E8MappingTests.Every_manual_only_requirement_names_an_example_source`.

**Limits, accepted rather than built**
- No timed escalation: the committee's overdue list (s11-05) is the escalation.
- Reminder days (30, 7, 1) are fixed by CISO Assistant, so an owner gets the instructions plus two warnings before the due date rather than exactly one. Turning off `task_node_due_soon` would remove all three, so it stays on.
- Disabling an email template applies to every task in the instance, including the GRC team's own.
- Occurrences have no completion date; the s11 period snapshots record who was overdue at each month end.
- The email's link opens a CISO Assistant sign-in that owners cannot use; the runbook text in the email is what they act on.

**Tests first**
- `EvidencePlanScopeTests.Manual_row_without_runbook_is_reported`
- `EvidencePlanScopeTests.Automated_row_names_configured_collector`
- `EvidencePlanScopeTests.Owner_contact_with_role_is_reported`
- `TaskTemplatePlanTests.Interval_becomes_recurring_schedule`
- `TaskTemplatePlanTests.Occurrence_status_is_never_in_the_plan`
- Contract: `TaskTemplateClientTests.Creates_recurring_task_with_assignees` (recorded responses)
- `ExampleRepoTests.Example_compliance_repo_validates` covers the new files.

**Functional check**
- Apply the fictional plan to the local CISO Assistant: both task templates appear with their next due date and both assignees; re-applying writes nothing. With mail pointed at a local SMTP catcher and a due date within 7 days, the owner contact's team email receives the reminder with the runbook text; with a past due date and `task_node_overdue` disabled, no email is sent.

### s10-05 Rehearse the lifecycle as a small defence supplier (added 2026-11-23)
- [ ] Done

Added at the maintainer's request (2026-11-23): "trial, onboard and maintain evidence" with small defence-industry teams in mind, because heavy AI support across the cyber GRC and AI GRC lifecycle is the point. That is why configuration is text and the inputs are agent-readable: OpenAPI specifications, vendor documents, digital trust centres, skills and agent files.

**Personas** (fictional, Example Pty Ltd style):
- A five-person machine shop that holds CUI for a prime and is working towards CMMC Level 2, with no security staff and an MSP quote it cannot afford.
- A small Australian defence supplier at Essential Eight maturity level 2 that must move to the ASD Essential Series.

**What a rehearsal judges**, from the pain points small contractors raised at a US House small-business hearing on CMMC (2026-11-18): how tightly the scope can be drawn around CUI rather than every system; how much of the work an agent carries from the team's own documents rather than paid consultants; whether evidence already verified for one framework (ISO 27001, the Essential Eight) is reused for another; and whether one evidence set answers a prime's repeat questionnaires and screenshot requests after certification.

**Acceptance criteria**
- Each task in this sprint records, beside its functional check, a rehearsal in which an agent given only what a person's agent would have (the compliance repo, its skills, vendor documents, specifications and the dev servers) carries the persona through the trial, onboarding and at least one evidence upkeep cycle (a later run, a lapsed manual source, a supplier review coming due). A person's steps (`hound approve`, `hound manual approve`) are never run by the agent; the trial's committed approved copies stand in for them.
- Every gap a persona would hit is fixed in the task or listed under Discovered work with the persona and the pain point it touches.
- The sprint review says, for each persona, which stages an agent can carry today and what still needs a consultant or the person.

**Functional check**
- The rehearsal notes themselves, under each task, and the CI trial job green.

### Carried from s09 (2026-11-23)

Built and tested in CI; each waits on a run in the maintainer's own environment, not on code. Details stay under the task in `s09-e8-gap-collectors.md`.
- [ ] s09-01 Application control policies from Intune: a real Graph export for the App Control for Business setting IDs.
- [ ] s09-15 Firewall firmware update timeliness: whether the read-only role may call the firmware upgrade check, and a dry run on the maintainer's tenant.
- [ ] s09-17 The Sophos trial on a real tenant.
- [ ] s09-29 Microsoft 365 checks follow the capability map: a run against the maintainer's tenant.
- [ ] s09-38 to s09-42 Agent-driven onboarding and manual evidence: the maintainer's own run with their agent, including s09-39, their XGS firewall.
- [ ] s09-54 and s09-57 `make trial` on the maintainer's machine.

## Discovered work

- From the s10-03 rehearsal (machine shop, CMMC Level 2), not fixed there:
  - No CUI on a supplier: which suppliers handle or protect CUI, of what kind (cloud service, managed service, security service) and on what authority (FedRAMP Moderate or equivalent, ISO 27001 certificate with scope and expiry). Today it is a comment. With it, a `hound supplier list --cui` would be the list a prime asks for (maintainer's choice pending).
  - `hound init` writes only an Essential Eight audit; a CMMC shop gets the wrong framework. CMMC / NIST SP 800-171 is s16/s17; until then `hound init` should say so.
  - Scope cannot be kept tight: every drafted supplier goes into the first perimeter with a review a month out and yearly after, whatever its criticality. Offer `--perimeter` on `supplier draft` and a review interval from criticality, as the assess-a-supplier skill describes.
  - Structured assurance (standard, certificate ID, scope, valid until, document) so an MSP's ISO 27001 is recorded once, warned on expiry, and reused across frameworks.
  - `hound init` writes no `settings.yaml`, so a repo with suppliers has the `contracts` flag off in CISO Assistant without a word. Say so when the first supplier is drafted.
  - On a refused option, `--json` commands print plain text rather than JSON.
  - Cost: nothing totals `annual_expense` or shows what compliance adds (here, moving to a CUI-authorised mail plan).

## Sprint review
