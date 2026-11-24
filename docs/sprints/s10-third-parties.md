# s10 Third parties as configuration

Status: Done

**Goal:** suppliers, their solutions and contracts, and supplier assessments live in the private configuration repo and are applied to CISO Assistant by `hound`, replacing the third-party spreadsheet. The evidence plan (which controls are evidenced automatically or manually, by whom and how often) is applied the same way, as recurring CISO Assistant tasks, so manual evidence and supplier reviews are chased by CISO Assistant's own reminders.

The governance collector and metrics, planned here before the release 1 reorder, moved to [s16](s16-governance-and-metrics.md) in release 2.

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
- [x] Done

**Story:** As the Cyber GRC team, I want the evidence plan declared in the repo and applied as recurring CISO Assistant tasks, so that system owners are emailed their runbook before evidence is due and the committee can see who is not providing it, without owners needing a CISO Assistant login.

How it works, using CISO Assistant's own features only (checked against the v4.3.4 source):
- A recurring task template (`/api/task-templates/`, keyed by `ref_id`) holds the assignees, the schedule, the runbook as its Markdown description, and the linked evidence, applied control and requirement assessments. CISO Assistant creates an occurrence per due date.
- CISO Assistant emails assignees 30, 7 and 1 days before each due date (with the description and a link) and daily while an occurrence is overdue. A team assignee is emailed at its team email as well as its leader, deputies and members.
- Escalation is the monthly committee, not email (person's decision, Nov 2026): the first email carries the instructions, the later ones are the warning, and once overdue the owner gets no more email. The example `settings.yaml` turns the daily overdue email off with the general setting `disabled_email_templates: [task_node_overdue]`; overdue occurrences become open actions in the committee report (s12-05).
- Only the Cyber GRC team uses CISO Assistant. A system owner is an **owner contact**: a team with a team email and no members, so it receives the reminders but has no login. The owner sends evidence to the GRC team as the runbook says; GRC uploads it against the occurrence, marks it complete, reviews it and sets the requirement result.
- `/api/task-templates/analytics/` counts tasks by assignee and by domain, with overdue and due-soon buckets; the s12 reports read it.

**Acceptance criteria**
- `scope/evidence-plan.yaml` declares one row per control or requirement: `ref_id`, `control` (applied control `ref_id`), `requirements` (optional), `method` (`automated`, `manual` or `hybrid`), `collector` (instance name, for `automated` and `hybrid`), `owner` (a team from `access.yaml`), `grc_team` (default from the file's top level), `every` (`3 months`, `6 months` or `12 months`) and `runbook` (a Markdown file in the repo).
- `access.yaml` teams gain an optional `email` (CISO Assistant's team email). A team with an email and no members or role assignments is an owner contact.
- `scope apply` creates or updates, by `ref_id`, the evidence item (owner, filtering labels: the method and the collector's name, as CISO Assistant takes labels only as `^[\w-]{1,36}$`) and, for `manual` and `hybrid` rows, a recurring task template: assignees owner and GRC team, schedule `MONTHLY` with interval 3, 6 or 12 (12 months as `YEARLY`, 1), description from the runbook, links to the evidence, applied control and requirement assessments.
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
- No timed escalation: the committee's overdue list (s12-05) is the escalation.
- Reminder days (30, 7, 1) are fixed by CISO Assistant, so an owner gets the instructions plus two warnings before the due date rather than exactly one. Turning off `task_node_due_soon` would remove all three, so it stays on.
- Disabling an email template applies to every task in the instance, including the GRC team's own.
- Occurrences have no completion date; the s12 period snapshots record who was overdue at each month end.
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

**Functional check (2026-11-24, CI run 411 on a08d31a, all jobs green)**
- `make test-functional`: `ScopePlanFunctionalTests.The_seed_applies_every_kind_then_plan_lists_no_changes_and_apply_writes_nothing` applies the example evidence plan to CISO Assistant v4.3.4 with every other kind: evidence items and task templates are written with no failures (213 changes in run 410), a re-plan lists no changes and a second apply writes nothing. `TaskTemplateClientTests` hold the recorded v4.3.4 shape (from the CI probe of `/api/task-templates/`).
- `trial` job: the trial applies its scope, including the evidence plan, unattended, and a second run plans nothing new.
- Not checked: the email legs (owner reminder via an SMTP catcher, no overdue email). The reference Compose stack does not pass `EMAIL_*` to CISO Assistant yet; listed under Discovered work.
- Deviations: `hound manual template` is not built; the decided flow is `hound manual propose` (agent) and `hound manual approve` (person) from s09-42. Evidence labels are the method and the collector's name (such as `manual`, `example-m365`), not `method:manual`, because CISO Assistant takes labels only as `^[\w-]{1,36}$`.

### s10-05 Rehearse the lifecycle as a small defence supplier (added 2026-11-23)
- [x] Done

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

**Functional check result (2026-11-24):** the rehearsals below; CI green on b9a57bb, the trial job included.

**Rehearsal (2026-11-24), persona: five-person machine shop going for CMMC Level 2 (continues the s10-03 repo)**
- s10-06: the agent recorded CUI on the backup, managed IT and the prime's portal from `suppliers.csv`, with no authority (none is on
  file), and added the file server and cloud mail as systems holding CUI. `hound scope validate` reported the decisive fact at once:
  cloud mail holds CUI through a plan that does not list it (`data.supplier_not_authorised`). `hound supplier list --data cui` gave the
  prime's list. But the finding is an error, so every later `hound add`, plan and apply refused until the agent commented the mail's
  data out: the repo then hid the one thing the prime most needs to know. Listed under Discovered work for a decision.
- s10-04: the agent copied the Essential Eight backups runbook and source, wrote `scope/access.yaml` and the evidence plan (a quarterly
  backup row and the weekly review row), and proposed a result for a fictional backup report (five checks pass, the two privileged
  checks partial, with the extracts). `hound add control AC-BACKUP` refused while the evidence plan named it (fixed by adding the control
  first).
- s10-07: the queue held six suppliers and the backup export, 65 minutes. **Gap fixed here:** the agent could not give a supplier a
  recommendation, so the person would have read six suppliers with none; `hound review recommend` now records one for any item (accept
  or decline, or a review's conclusion), with reasons and sources, shown in the session and cleared once accepted. The agent recommended
  declining the cloud mail contract (its trust centre says the plan is not authorised for CUI) and accepting the rest. The session, with
  recorded answers as the owner: five accepted, the managed IT supplier challenged ("can its remote tool reach the drawings share?"),
  the mail skipped; the backup export was attested in the drop folder. **Gap fixed here:** each accepted supplier's first review, due in a
  month, was then asked at once and would have topped the next session; a supplier review now joins the queue 14 days before it is due.
  The agent answered the challenge with `hound review recommend`; the next session showed the note and the answer side by side, the
  owner accepted, and only the declined mail contract stayed queued.
- Upkeep not run here: a lapsed source and a later run need the clock moved or a CISO Assistant; the manual collector's overdue and
  changed-file results are covered by its unit tests, and the plan against CISO Assistant by CI's functional suite.

**Rehearsal (2026-11-24), persona: an 18-person Australian defence supplier at Essential Eight ML2, moving to the Essential Series**
- Inputs: a fictional four-row supplier list (cloud office, managed endpoint security, payroll, a cloud ERP hosted offshore) and the
  owner's note (DISP member, DSGL drawings, OFFICIAL: Sensitive email from the prime, four hours a week).
- `hound init --assistant claude` wrote the repo and the agent's files. **Gap fixed here:** a new repo had no evidence plan, so nothing
  would remind the owner of the weekly review; `hound init` now writes `scope/access.yaml` (the owner and a GRC team) and the evidence
  plan with the weekly review task and its runbook.
- The agent drafted all four suppliers in one command each with `--data` (DSGL `export_controlled`, `OFFICIAL: Sensitive` under
  `classified`, `pii`), added the privileged access review source with `hound add collector manual --set ...`, and proposed a result for a
  fictional admin role export. It recommended declining the ERP contract (DSGL drawings stored offshore with no assessment: ask Defence
  Export Controls first) and flagged the endpoint supplier's ISO 27001 certificate expiring on 2026-12-15. The queue was 45 minutes; the
  session (`--for 45m`) accepted four and skipped the ERP.
- The Essential Series itself cannot be rehearsed yet: hound has no Essential Series framework or mapping (s14). The checks are
  framework-neutral, so the attested backup and privileged access results will map to it once the mapping exists.

### s10-06 Sensitive data on suppliers and systems (added 2026-11-23)
- [x] Done

Decided (maintainer, 2026-11-23): add it in s10 and make it broader than CUI. A small defence supplier is asked about four kinds of sensitive data, and a prime, an auditor or the security committee needs to see which suppliers and systems handle each kind and on what authority.

**Story:** As a small team's GRC lead, I want each supplier's solution and each system to say which sensitive data it handles and what authorises it, so that I can draw the boundary tightly, see a supplier handling data it is not approved for, and give a prime the list it asks for without a questionnaire.

**Acceptance criteria**
- A framework-neutral `data` list on a solution (in `third-parties/<entity>.yaml`) and on a system (in `setup.yaml`): `kind` (`cui`, `pii`, `export_controlled`, `classified`), `detail` (free text such as `CUI//SP-CTI`, `ITAR`, `EAR`, `DSGL`, `OFFICIAL: Sensitive`, `PROTECTED`), `role` (`stores`, `processes`, `transmits`, `protects`), and for a solution an `authority`: what approves it (such as FedRAMP Moderate or equivalent, an IRAP assessment to PROTECTED, an ISO 27001 certificate, an export licence or registration, DISP membership), with `evidence` (a document in the repo) and `expires`.
- Validation: a system that handles a kind through a solution (`solution:` or `supplied_through:`) whose `data` does not list that kind is reported (`data.supplier_not_authorised`); an authority past `expires` is reported (`data.authority_expired`); `classified` and `export_controlled` need a `detail`.
- `scope apply` shows the kinds in CISO Assistant with its own features only: filtering labels `data:<kind>` on the solution's and the system's records, and the authority in the solution's description. Nothing bespoke.
- `hound supplier list --data <kind>` prints, as Markdown or JSON, each supplier and solution handling that kind, with its role, authority, evidence and expiry: the list a prime or auditor asks for.
- `docs/third-parties.md` and `docs/scope.md` explain the four kinds with an Australian and a US example of each (Privacy Act and state privacy law; DSGL and ITAR/EAR; PSPF classifications; CUI), and say what `hound` does not decide (whether data is CUI or export controlled is the owner's and the prime's call).
- The supplier skill and `hound supplier draft --data` let an agent record a kind from a trust centre or contract, leaving the authority empty when no document backs it.

**Tests first**
- `SensitiveDataScopeTests.System_with_cui_through_an_unauthorised_supplier_is_reported`
- `SensitiveDataScopeTests.Expired_authority_is_reported`
- `SensitiveDataScopeTests.Classified_and_export_controlled_need_detail`
- `SensitiveDataPlanTests.Kinds_become_filtering_labels`
- `SupplierListCommandTests.Lists_suppliers_handling_a_kind_with_authority`

**Functional check**
- Apply the tutorial's suppliers with data kinds to the local CISO Assistant and filter solutions by `data:cui`; the machine-shop rehearsal produces the prime's list with `hound supplier list --data cui`.

**Functional check (2026-11-24, CI run 45308123901 on 0e92b86, all jobs green)**
- `SensitiveDataFunctionalTests` applies a fictional supplier whose solution stores CUI (with a FedRAMP Moderate equivalent authority) and personal information to CISO Assistant v4.3.4: the solution carries the `data-cui` and `data-pii` filtering labels, filtering solutions by `data-cui` finds it, its description ends with the data and the authority, a re-plan lists no changes, and `hound supplier list --data cui` prints the prime's row with the authority, evidence and expiry.
- Deviations: labels are `data-<kind>`, not `data:<kind>`, as CISO Assistant takes labels only as `^[\w-]{1,36}$`. A system supplied through a third party with no `solution` is checked against all of that third party's solutions. The prime's list comes from the functional test, not a separate machine-shop rerun; s10-05 uses it in the persona rehearsal.

### s10-07 Review queue and guided weekly session (added 2026-11-24)
- [x] Done

Decided (maintainer, 2026-11-24): assume a GRC member with 2 to 8 hours a week, get as close as possible to AI approving everything,
and gather every human review into one simple process. Design: [one guided review a week](../design/guided-review.md).

**Story:** As a GRC member with a few hours a week, I want one weekly session in which my agent walks me through everything that
needs me, with its recommendation and reasons, so that I accept or challenge each item quickly, learn as I go, and apply the lot in one
batch.

**Acceptance criteria**
- `hound review pending --json` lists, in one shape, every item that waits on a person: undecided onboarding decisions and
  not-applicable rows, supplier approvals and review conclusions, manual evidence proposals, and scope changes not yet applied. Each item
  has its kind, title, why it matters (from the requirement), the agent's recommendation with reasons and sources when one is drafted,
  the effect of accepting it, its deadline and an estimate in minutes. Prompt-free, for the agent.
- `hound review` refuses an agent or a pipe, walks the queue most-urgent first, stops at `--for <minutes>`, and asks accept, challenge
  (a note, the item stays queued for the agent) or skip. Accepted items are written as `hound approve` and `hound manual approve` write
  them today; it then shows the plan and applies it (`--no-apply` to stop at the files). One sign-off record per session names the
  person, the time and every accepted item.
- Standing approvals: a session can accept a rule for re-attesting an unchanged manual source (same checks, same results as the last
  attestation) for 12 months; the rule is a file in the repo, listed in each session summary, and never covers a not-applicable
  decision, a risk acceptance or a first approval.
- The example evidence plan has a "Weekly compliance review" row: a recurring task for the GRC team every week whose description says how
  to start the session. (Needs a weekly interval in the evidence plan: `every: 1 week`.)
- A `weekly-review` skill tells the agent how to prepare the queue before the session and answer challenges after it.
- `docs/manual-evidence.md` and the onboarding docs point to the weekly session as the one place to sign off.

**Tests first**
- `ReviewQueueTests.Lists_every_pending_kind_in_one_shape`
- `ReviewQueueTests.Most_urgent_first_and_stops_at_the_time_given`
- `ReviewCommandTests.Refuses_without_a_terminal`
- `ReviewCommandTests.Accepted_items_are_written_as_approve_writes_them_and_one_sign_off_is_recorded`
- `StandingApprovalTests.Unchanged_reattestation_is_accepted_and_nothing_else_is`
- `EvidencePlanScopeTests.Weekly_interval_is_a_task_schedule`

**Functional check**
- The machine-shop rehearsal (s10-05): the agent prepares a queue with a supplier, a manual export and two onboarding decisions; the
  session, run in a terminal with recorded answers, accepts three and challenges one, and the next plan is empty but for the challenge.

**Functional check result (2026-11-24)**
- On a copy of `examples/compliance-repo`, after `hound supplier draft --ref SUP-EXAMPLE-MAIL ...`:
  `hound review pending --repo . --json` listed one `supplier` item (10 minutes, its why, effect and file).
  `hound review --repo . --no-apply`, run in a pseudo-terminal with the answers "Alex Example", `a`, `s`: accepted the supplier (the
  contract became `active` with `approved_by` and `approved_on`), then asked its first review "[now ready]", which was skipped, and wrote
  `review/sign-offs/2026-11-24-025051.yaml` (person, on, at, accepted, skipped). Piped input exits 2 with "hound review needs a person
  at a terminal". `hound scope validate scope` on the example passes with the weekly row (`every: 1 week`).
- The full machine-shop rehearsal (agent prepares the queue, three accepted and one challenged, the next plan empty but for the
  challenge, against CISO Assistant) runs with s10-05.
- Deviations: the tests are `ReviewCommandTests.Lists_every_pending_kind_in_one_shape` (in the CLI, which reads the repo) and
  `ReviewQueueTests` for the order and the time limit; scope changes waiting to be applied are not queue items, the session ends by
  showing the plan and applying it after a confirmation; an item that becomes ready during the session (a system after its supplier) is
  asked there, marked "now ready", rather than listed beforehand.

### Carried from s09 (2026-11-23)

Moved to s11 on 2026-11-24, at the maintainer's request, so s10 can close: each waits on a run in the maintainer's own environment, not
on code. See "Carried from s09 and s10" in `s11-e8-ssp-and-ai-grc.md`.

## Discovered work

- From the s10-03 rehearsal (machine shop, CMMC Level 2), not fixed there:
  - No CUI on a supplier: which suppliers handle or protect CUI, of what kind (cloud service, managed service, security service) and on what authority (FedRAMP Moderate or equivalent, ISO 27001 certificate with scope and expiry). Today it is a comment. Now s10-06, broadened to PII, export-controlled data and security classification (maintainer, 2026-11-23).
  - `hound init` writes only an Essential Eight audit; a CMMC shop gets the wrong framework. CMMC / NIST SP 800-171 is s16/s17; until then `hound init` should say so.
  - Scope cannot be kept tight: every drafted supplier goes into the first perimeter with a review a month out and yearly after, whatever its criticality. Offer `--perimeter` on `supplier draft` and a review interval from criticality, as the assess-a-supplier skill describes.
  - Structured assurance (standard, certificate ID, scope, valid until, document) so an MSP's ISO 27001 is recorded once, warned on expiry, and reused across frameworks.
  - `hound init` writes no `settings.yaml`, so a repo with suppliers has the `contracts` flag off in CISO Assistant without a word. Say so when the first supplier is drafted.
  - On a refused option, `--json` commands print plain text rather than JSON.
  - Cost: nothing totals `annual_expense` or shows what compliance adds (here, moving to a CUI-authorised mail plan).
- From s10-04: the reference Compose stack (`deploy/`) does not pass `EMAIL_*` to CISO Assistant, so task reminders cannot be sent or tested end to end. Wire it with a local SMTP catcher in the compose smoke test.
- From s10-07: the weekly task's email is CISO Assistant's task reminder with the runbook. Putting the queue's size and the time it needs
  in it would need hound to update the task description before each week; left for s12-03 (operating guide).
- From the s10-05 rehearsals:
  - Machine shop, scope kept tight around CUI: a sensitive-data finding (`data.supplier_not_authorised`, `data.authority_expired`) is an
    error, so recording the truth (CUI in a mail plan not authorised for it) blocks every `hound add`, plan and apply until it is fixed,
    and an agent hides it to carry on. Decided (maintainer, 2026-11-24): they warn. Done in
    s10-05: validate, plan and apply print them, `--json` marks them `"severity": "warning"`, the weekly review lists each as a
    `finding` until it is fixed, and nothing is blocked. The committee report (s12-05) should list them too.
  - Both personas: a solution holding CUI or DSGL data with no authority recorded is listed as "no authority recorded" but never
    reported; nor is a supplier's country, so offshore DSGL storage is found only by reading the notes.
  - Both personas: no command adds `data` to a supplier already drafted, or `solution`, `supplied_through` and `data` to a system
    (`hound add system` takes none of them): the agent edits YAML by hand.
  - Essential Series persona: an Essential Eight manual source template brings no control, so the evidence plan row borrows the health
    control. Ship a control with each template, or let `hound add control` take one from it.
  - Essential Series persona: a recommendation to decline leaves the item queued every week (skip is the only way to leave it); show it
    as "declined, waiting on <what>" rather than asking again.
  - `hound manual upload` waits for an answer when no terminal is attached instead of refusing.

## Sprint review

**2026-11-24.** Closed at the maintainer's request. Five tasks done (s10-01 and s10-02 moved to s09 as s09-19 and s09-20), each with red
and green commits; CI green on every job, the functional and trial jobs included.

- **Third parties and the evidence plan as configuration** (s10-03, s10-04): the documentation and example repo for suppliers, and the
  evidence plan applied as CISO Assistant evidence items and recurring tasks, with a runbook per manual source and the Essential Eight
  manual-source templates.
- **Sensitive data** (s10-06): CUI, personal information, export-controlled and classified data on solutions and systems, with
  authority and expiry; filtering labels in CISO Assistant; `hound supplier list --data` for the prime's list. Findings warn and join the
  weekly review rather than block (maintainer's decision, s10-05).
- **One guided weekly review** (s10-07): `hound review pending`, `hound review recommend`, the `hound review` session with one sign-off,
  standing approvals for unchanged re-attestations, a weekly task from `hound init`, and the `weekly-review` skill.
- **Persona rehearsals** (s10-03, s10-05):
  - *Machine shop going for CMMC Level 2.* An agent can carry the supplier register, the sensitive-data record and the prime's list,
    the evidence plan, manual evidence proposals and the weekly queue; the owner's part is about an hour a week in `hound review`. Still
    needs the person or a consultant: whether data is CUI (the owner and the prime), moving CUI off the commercial mail plan, and a CMMC
    Level 2 / NIST SP 800-171 audit, which hound does not have until s17.
  - *Essential Eight ML2 supplier moving to the Essential Series.* An agent can carry suppliers with DSGL and OFFICIAL: Sensitive data,
    manual sources and the weekly queue in about 45 minutes a week. Still needs the person: export permits for offshore DSGL storage, and
    the Essential Series itself, which waits on its framework and mapping (s14).

Moved to s11, waiting on the maintainer's own environment: s09-01, s09-15, s09-17, s09-29, s09-38 to s09-42, s09-54 and s09-57.
Open findings are under Discovered work.
