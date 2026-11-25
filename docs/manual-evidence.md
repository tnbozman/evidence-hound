# Manual evidence

Some evidence has no API: a quarterly restore test, a privileged access review, the browser and Office policies on laptops nobody
manages centrally. This guide shows how a small team gets that evidence on time without chasing people, and without giving system
owners a CISO Assistant login. It uses CISO Assistant's own recurring tasks and email reminders; `hound` only writes them from the
repo.

The flow:

1. The evidence plan in the repo says, for each control, how its evidence arrives, who provides it and how often.
2. `hound scope apply` turns each row a person provides into a recurring CISO Assistant task. CISO Assistant emails the owner the
   runbook before each due date.
3. The owner exports what the runbook asks for and sends it to the GRC team. Their agent may read the export and propose a result
   (`hound manual propose`); a person in the GRC team attests it at a terminal (`hound manual approve` or the weekly review).
4. The next `hound run` publishes the attested result to every requirement the source's checks are mapped to. The GRC team reviews it
   in CISO Assistant and marks the task done.
5. A task still open at month end is an overdue item in the security committee's report. That is the escalation; nobody is emailed
   daily.

## The evidence plan

`scope/evidence-plan.yaml` has one row per control, or per group of requirements under a control:

```yaml
grc_team: IT team                 # receives and reviews evidence, for rows that do not name their own
evidence:
  - ref_id: EV-MFA                # EV- then upper case letters, digits and hyphens
    control: AC-MFA               # an applied control from controls.yaml
    method: automated             # automated, manual or hybrid
    collector: example-m365       # a collector in hound.yaml (automated and hybrid rows)
  - ref_id: EV-BACKUP-RESTORE
    control: AC-BACKUP
    requirements: [ISM-1515]      # optional; every requirement of the control when left out
    method: manual
    collector: example-backup-restore-test
    owner: Backup owners          # a team in access.yaml (manual and hybrid rows)
    every: 3 months               # 1 week, 3 months, 6 months or 12 months
    runbook: manual-sources/backup-restore-test.md
```

`hound scope validate scope/ --config hound.yaml` checks it: a manual or hybrid row needs an owner, an interval and a runbook that is a
file in the repo; an automated or hybrid row names a collector that `hound.yaml` configures; the control and the teams exist; an owner
contact holds no role. The full reference is in [scope.md](scope.md#ref-id-conventions) and the scope schema.

`hound scope apply` writes, for every row, an evidence item named after the row and its control, labelled with its method (`automated`,
`manual` or `hybrid`) and its collector's name so the GRC team can filter by them (CISO Assistant takes a label of letters, digits,
underscores and hyphens, at most 36 long, so a longer collector name is left out). For each manual and hybrid row it also writes a recurring task: assigned to the
owner and the GRC team, due every week, every 3 or 6 months, or yearly, with the runbook as its description, linked to the evidence item, the
control and the requirements. The first due date is set when the task is created, one interval from that day; CISO Assistant moves it
on after that.

## Owner contacts

Only the GRC team signs in to CISO Assistant. A system owner is an **owner contact**: a team in `scope/access.yaml` with an email and
no members.

```yaml
teams:
  - name: Backup owners
    description: Run the backup service and its quarterly restore test.
    email: backup-owners@example.com
```

CISO Assistant emails a task's reminders to its team's email, so the owner is reminded with no account. An owner contact never holds
a role (`evidence_plan.owner_contact_has_role`): it has nobody to give it to. Use a shared mailbox or a distribution list, so the
reminders survive a person leaving.

## Runbooks

A runbook is a Markdown file in the repo that tells the owner, in plain steps, what to do. CISO Assistant sends it as the task's
description, so write it for someone who will read it in an email:

- **What to export**: the export, report or settings page, from which console, as which file type. Ask for exports, not screenshots,
  where the product allows: an export can be read again and compared next time.
- **How often** and **who attests**.
- **Send it to the GRC team**: where to send it (a mailbox, or `hound manual upload` to the source's drop folder).
- **If something fails**: a failed test is evidence too. Say so and send it.

`examples/manual-sources/essential-eight/` has a runbook for each Essential Eight area no collector reads, with the collector to paste
into `hound.yaml` beside it, and `examples/manual-sources/essentials/` one for each ASD Essentials area the Essential Eight never asked
about ([index](collectors/manual-templates.md)). Copy the ones you need into `manual-sources/` in your repo,
set the owner and the drop folder, and point an evidence plan row at the copy.

## Turning mail on

CISO Assistant sends no email until its mail server is set and mailing is turned on:

1. Give the CISO Assistant backend its mail server through the `EMAIL_*` settings of its deployment (`EMAIL_HOST`, `EMAIL_PORT`,
   `EMAIL_HOST_USER`, `EMAIL_USE_TLS`, `DEFAULT_FROM_EMAIL`, and the password as a secret, never in the repo).
2. Turn mailing on and the daily overdue email off in `scope/settings.yaml`:

   ```yaml
   general:
     notifications_enable_mailing: true
     # Owners get the runbook 30, 7 and 1 days before each due date. Once a task is overdue, the committee report escalates it,
     # not a daily email.
     disabled_email_templates: [task_node_overdue]
   ```

Test it with a task due within 7 days and a mailbox you can read before you rely on it.

## The GRC team's steps

When an owner's evidence arrives:

1. Check the file is complete and is what the runbook asked for.
2. If it came by email, upload it: `hound manual upload --config hound.yaml --source <source> <file>`.
3. Ask your agent to read it and propose a result for each check, with the extract that supports it: `hound manual propose`.
4. A person attests the proposal at a terminal, in the [weekly review](#the-weekly-review) with everything else that waits on them
   (`hound review`), or on its own with `hound manual approve`. An agent or a pipe is refused.
5. After the next run, review the published results in CISO Assistant, attach the file to the task occurrence, and mark the occurrence
   done.

## The weekly review

The GRC team signs off everything that waits on a person in one session a week. The example evidence plan has a row for it: a weekly
task assigned to the GRC team, whose runbook ([weekly-compliance-review.md](../examples/compliance-repo/manual-sources/weekly-compliance-review.md))
says how to start.

1. Before the session, the agent prepares the queue with the skill "weekly-review": `hound review pending --json` lists every item
   (manual evidence proposals, onboarding decisions, suppliers, supplier reviews, drafted audits and imported rows), and the agent
   records a recommendation for each with its reasons and sources (`hound review recommend`, kept in `review/recommendations.json`),
   and answers last week's challenges.
2. The person runs `hound review` in their own terminal (`--for 30m` to stop after half an hour). Most urgent first, each item shows
   what it is, why it matters, the recommendation and what accepting it does. They accept, challenge (with a note the agent answers
   before the next session; the item stays queued) or skip.
   For a long queue, or decisions you want to understand before you accept them, go through them with your agent first (the agent
   `hound-decisions`, skill "work-through-pending-decisions"). `hound review pending --json` gives each item a `risk` (`high` for a
   not-applicable decision, a supplier, a finding, or an item whose recommendation is missing or has no source) and a `summary` by kind
   and recommendation, so the agent starts with the size of the job and the riskiest items. Then accept, challenge or skip each one by
   its key, `hound review --only <key>` (repeat `--only` for several): nothing else is asked or written. An unknown key is refused with
   the nearest keys, and it still needs you at a terminal. `hound approve` opens with the same count by kind and points here.
3. Accepted items are written as `hound approve` and `hound manual approve` write them. A session records one sign-off under
   `review/sign-offs/`, naming the person, the time and every item, then shows the plan and applies it after a confirmation
   (`--no-apply` stops at the files).

**Standing approvals.** When a source's new export has the same checks and the same results as its last attestation, the session
offers to accept that source's future unchanged exports for 12 months. The rule is written to `review/standing-approvals.json`, listed
in every session's summary, and expires. Later unchanged exports from that source are accepted without being asked, and counted in the
summary. A rule never covers a first attestation, a changed result, a not-applicable decision or any other kind of item.

## Attesting and publishing

The Essential Eight mapping maps each template's checks to its requirements, so one attestation answers every requirement it covers,
and the same checks can be mapped to another framework or a prime's questionnaire. A run publishes a requirement only when every one of
its checks is reported. Until `hound.yaml` configures a requirement's manual source, no run writes it, and the GRC team may write its
result by hand in the audit file. Once the source is configured, a hand-written result for the same requirement is reported as
`scope.result_conflict`: remove it and let the attested source take over.

## Limits

These are accepted rather than built:

- No timed escalation. The security committee's list of overdue tasks is the escalation.
- CISO Assistant fixes the reminder days (30, 7 and 1 days before), so an owner gets the instructions and two warnings. Turning off
  `task_node_due_soon` would remove all three.
- Disabling an email template applies to every task in the instance, the GRC team's own included.
- Occurrences have no completion date; the period snapshots record who was overdue at each month end.
- The email's link opens a CISO Assistant sign-in that owners cannot use. The runbook text in the email is what they act on.
- `hound` never writes an occurrence's status, an evidence revision, a review or a result the GRC team sets in CISO Assistant.
