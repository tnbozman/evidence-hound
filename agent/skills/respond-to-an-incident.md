---
name: respond-to-an-incident
description: "Help during and after a cyber security incident: list the reports it owes and their deadlines, and build or improve the incident response plan. Use when an incident is reported, when the person asks what is owed or overdue, or when the incident response plan needs work."
---
## Respond to an incident

The guide is [docs/incidents.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/incidents.md).

During an incident:

1. The incident itself is recorded in CISO Assistant by the person, with a detection entry on its timeline; the clock starts there.
2. List what it owes: `hound incident obligations <incident> --scope scope/ --config hound.yaml --format json`. Each report has whom it
   goes to and its deadline (for example DFARS 252.204-7012 within 72 hours, ReportCyber, the Notifiable Data Breaches assessment).
   Tell the person the earliest deadline first, in their time zone. A report is marked made by a timeline entry
   `Reported: <id>, <their reference>`, which the person adds after they report.
3. Follow the plan's containment and evidence steps in `irp/`; never wipe, rebuild or power off a system the incident manager has not
   released. Do not contact anyone outside the organisation; the executive is the only spokesperson.

Building the plan:

4. Build it, gaps first: `hound irp build --programme <id> --scope scope/ --json`. Roles come from `scope/irp.yaml`, systems and
   recovery objectives from the scope, reports from `scope/reporting.yaml`, parties from `scope/third-parties/`.
5. Ask the person for what only they know (who holds each role, how severity is judged here) and draft the judgement sections in `irp/`
   for their review. Run `hound irp build --programme <id> --strict` until no gap remains.
6. After an incident closes, draft the lessons-learned changes as a pull request and raise the plan's version in `scope/irp.yaml`.
