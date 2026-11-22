# s10 Third parties as configuration

Status: Planned

**Goal:** suppliers, their solutions and contracts, and supplier assessments live in the private configuration repo and are applied to CISO Assistant by `hound`, replacing the third-party spreadsheet. The evidence plan (which controls are evidenced automatically or manually, by whom and how often) is applied the same way, as recurring CISO Assistant tasks, so manual evidence and supplier reviews are chased by CISO Assistant's own reminders.

The governance collector and metrics, planned here before the release 1 reorder, moved to [s15](s15-governance-and-metrics.md) in release 2.

Builds on s06 (scope files, plan, apply, inventory, framework libraries for questionnaires). Everything is matched by `ref_id`, or by the key CISO Assistant uses where it has no `ref_id` (name and domain; email for representatives). All examples use the fictional Example Pty Ltd and `example.com`.

What is configuration here, and what is not:

| Configuration in git, applied by `hound` | Stays in CISO Assistant |
| --- | --- |
| Entities, solutions, contracts, representatives (optional), supplier assessment set-up and review cycle, forced tiers with reasons | Questionnaire answers, tiering form results and tier approvals, tier history, campaigns |
| Evidence plan: evidence items, recurring task templates (assignees, schedule, runbook), owner contacts | Task occurrences and their status, uploaded evidence revisions, reviews, requirement results set by the Cyber GRC team |

### s10-01 Suppliers, solutions and contracts
- [ ] Done

**Story:** As a supplier owner, I want our suppliers, what they provide and our contracts with them declared in the repo, so that the supplier register is reviewed and complete.

**Acceptance criteria**
- `scope/third-parties/<entity>.yaml` declares one entity: `ref_id`, name, description, mission, country (ISO 3166-1 alpha-2), currency (ISO 4217), parent entity, domain, and its dependency (0–4), penetration (0–4), maturity (1–4) and trust (1–4).
- It also declares the entity's solutions (`ref_id`, name, description, criticality 1–4) and contracts (`ref_id`, name, status `draft`, `active`, `expired` or `terminated`, start and end dates, annual expense, currency, the solutions covered, and identifiers such as LEI or VAT).
- Representatives are optional. They are personal data, so the guide recommends keeping them in CISO Assistant unless the repo is private and access-controlled; when declared, they are matched by email and never given a login by `hound`.
- If s06-05 confirmed the API can update the main entity, `organisation` in `scope/setup.yaml` manages it here (the s06 discovered-work item).
- Validation: country and currency codes; contract end after start; an `active` contract past its end date is reported (`supplier.contract_lapsed`); solutions named by a contract belong to the same entity.

**Tests first**
- `SupplierScopeTests.Invalid_country_or_currency_is_reported`
- `SupplierScopeTests.Active_contract_past_end_date_is_reported`
- `SupplierScopeTests.Contract_solution_must_belong_to_entity`
- Contract: `SupplierClientTests.Creates_entity_solution_and_contract`

**Functional check**
- Apply two fictional suppliers to the local CISO Assistant; re-applying writes nothing; a changed contract end date is a one-field update.

### s10-02 Supplier assessments and tiers
- [ ] Done

**Story:** As a supplier owner, I want each supplier's assessment set up from the repo on a review cycle, so that reviews start on time and nobody sets them up by hand.

**Acceptance criteria**
- An entity file may declare assessments: name, solutions, perimeter, due date, criticality, the questionnaire framework (a library URN), and `review_every` (such as `12 months`). Applying creates the entity assessment and its linked questionnaire audit.
- When a review is due, `hound` opens the next one: a new revision if the API supports it (s06-05), otherwise a new assessment named with its period, using the same periods logic as audits (s06-10).
- Answers, tiering form results and campaigns are never written by `hound`.
- `forced_tier` (one of the criticality scale's keys, such as `critical`) with a required `reason` may be declared; `scope plan` reports when CISO Assistant's current tier differs from it.
- `review_every` also creates a recurring task template for the supplier owner and the Cyber GRC team (the same mechanism as s10-04), so supplier reviews get CISO Assistant's reminders and appear in the same overdue reporting.

**Tests first**
- `SupplierAssessmentTests.Due_review_opens_the_next_one`
- `SupplierAssessmentTests.Answers_are_never_in_the_plan`
- `SupplierScopeTests.Forced_tier_requires_reason`
- `SupplierAssessmentTests.Review_cycle_creates_recurring_task_for_owner`
- `ScopePlanTests.Tier_differing_from_forced_tier_is_reported`

**Functional check**
- With a fixed clock past a review date, `scope apply` opens the next fictional supplier assessment in the local CISO Assistant with its questionnaire.

### s10-03 Documentation and example repo
- [ ] Done

**Acceptance criteria**
- `docs/third-parties.md`: file formats, validation, plan and apply, privacy guidance for representatives, and what stays in CISO Assistant (questionnaire answers, tiering results, campaigns).
- The example compliance repo (s06-12) gains two fictional suppliers and a supplier assessment, all of which validate.

**Tests first**
- `ExampleRepoTests.Example_compliance_repo_validates` covers the new files.
- Docs lint and link check.

**Functional check**
- Following the guide verbatim, add a supplier to the example repo and apply it.

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
- `docs/manual-evidence.md`: the plan file, a runbook template (what to export, from where, how to send it to the GRC team), turning mail on (`EMAIL_*` in the deployment, `notifications_enable_mailing` in `settings.yaml`) and the overdue email off (`disabled_email_templates`), the GRC team's steps in CISO Assistant, and the limits below.
- The example compliance repo gains a fictional evidence plan with two manual rows, an owner contact and a runbook, all of which validate.

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

## Discovered work

## Sprint review
