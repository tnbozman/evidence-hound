# Incidents and reporting deadlines

This guide is for a GRC team that must report an incident to regulators and customers on their clocks: 72 hours to the DoD under
DFARS 252.204-7012 when covered defence information is involved, 30 days to assess a data breach under the Notifiable Data
Breaches scheme, and the reports to ASD's ACSC. You declare those obligations once, in the compliance repo; the incident itself is
recorded in CISO Assistant; and `hound incident obligations` tells you which reports one incident owes and by when.

The example compliance repo has the obligations in `examples/compliance-repo/scope/reporting.yaml` and the plan's yearly test as a
manual source (`example-incident-response-plan` in its `hound.yaml`, with the runbook
`examples/compliance-repo/manual-sources/incident-response-test.md`).

## 1. Declare what you must report

`scope/reporting.yaml` lists each obligation: an `id`, who receives it (`report_to`), how long after the incident's discovery it is
due (`within`, such as `72h` or `30d`), and what triggers it: `when: {data: [cui]}` for incidents on a system that handles those
data kinds (the `data` of each system in `scope/setup.yaml`, see [Scope files](scope.md)), or `when: {always: true}` for every
incident.

```yaml
obligations:
  - id: dfars-7012
    name: DFARS 252.204-7012 cyber incident report
    report_to: DoD, through the DIBNet portal (needs a medium assurance certificate)
    within: 72h
    when: {data: [cui]}
    reference: DFARS 252.204-7012(c)
  - id: reportcyber
    name: Report to ASD's ACSC through ReportCyber
    report_to: ASD's ACSC (cyber.gov.au/report)
    within: 1d
    when: {always: true}
```

Check each clock against its source, and add your customers' contract clauses the same way. `hound scope validate` checks the file.

## 2. Record the incident in CISO Assistant

Under Operations, Incidents, record the incident with a reference ID (such as `INC-2026-01`) and the affected systems as its
assets. Add a timeline entry of type *detection* at the time it was discovered: every clock starts there (without one, when the
incident was reported).

## 3. List the reports it owes

```bash
hound incident obligations INC-2026-01 --config hound.yaml
```

```text
INC-2026-01 Malware on a laptop: discovered 2026-11-20 09:30 UTC, 2 report(s) owed
  reportcyber  due 2026-11-21 09:30 UTC  made 2026-11-20 15:00 UTC  to ASD's ACSC (every incident)
  dfars-7012  due 2026-11-23 09:30 UTC  due  to DoD, through the DIBNet portal (Example laptops handles cui)
```

`--format json` gives the same for an agent. The command reads CISO Assistant and writes nothing.

## 4. Record each report you make

Add a timeline entry to the incident that starts with `Reported:` and the obligation's id, then their reference:
`Reported: dfars-7012, DIBNet report 12345`. The report then shows as made, and is never overdue.

## Where overdue reports show up

- **The weekly review.** With CISO Assistant configured (`HOUND_CA_URL` and `HOUND_CA_TOKEN`), `hound review` lists each report an
  open incident has not made, due by its deadline, every week until the incident's timeline records it.
- **The committee report.** `hound report snapshot` records the reports each incident owes; `hound report committee` lists the
  overdue ones in section 6 and proposes making each in section 8.

## The plan and its yearly test

The incident response plan is approved and tested once a year, for example with a one-hour tabletop exercise. Make it a manual
source with the checks `incident.response_plan_approved` and `incident.response_plan_tested`, due every 366 days, with a runbook and
an evidence plan row (see [Manual evidence](manual-evidence.md)), as the example repo does.

## What stays in CISO Assistant

The incidents, their timelines, severity and status are recorded in CISO Assistant, never in the repo. The repo holds only the
obligations: who must be told, and how soon.
