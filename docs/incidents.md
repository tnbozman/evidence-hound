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

## Build the plan

`hound irp build --programme essential-eight` writes the incident response plan from what the repo already holds, so you write only
the parts that need judgement. `scope/irp.yaml` names the programme, the plan's version, who holds each role and the Markdown file of
each judgement section:

```yaml
programme: essential-eight
version: "0.1"
roles:
  incident_manager: it@example.com    # leads a response
  deputy: IT team                     # stands in
  executive: ceo@example.com          # decides for the business, the only spokesperson
  legal_privacy: ceo@example.com      # law, contracts and privacy
  authorising_officer: ceo@example.com  # approves each version (ISM-0047)
sections:
  severity: irp/severity.md
  escalation: irp/escalation.md
  containment: irp/containment.md
  communication: irp/communication.md
  lessons_learned: irp/lessons-learned.md
```

- **Derived:** the contacts from the roles, the systems with the sensitive data they handle and their recovery objectives
  (`setup.yaml`), the suppliers and customers with their contacts and contract clauses (`third-parties/`, including flowed-down
  clauses), and each report with its deadline and what triggers it (`reporting.yaml`). `--json` gives every value with its file and line.
- **Written by you:** severity definitions, escalation decisions, containment priorities, communication approach and lessons learned.
  A missing role or section, or an empty file, is a gap listed first; `--strict` exits non-zero while any remain.
- **Layout:** NIST SP 800-61 Rev 3's functions (govern, identify, protect, detect, respond, recover), with the Australian reporting
  (ReportCyber, the Notifiable Data Breaches scheme, DISP) and DFARS 252.204-7012 inside them, and a section for each of ISM-0043's
  eight items. It cites NIST SP 800-171 3.6.1 to 3.6.3 and the
  [ACSC Cyber Incident Response Plan Guidance](https://www.cyber.gov.au/sites/default/files/2023-03/ACSC%20Cyber%20Incident%20Response%20Plan%20Guidance_A4.pdf).

The plan holds names and contacts: keep the built plan out of a public repo, and store the approved copy as the evidence of the yearly
check below. Raise `version` when you change the plan; the authorising officer approves each version.

## The plan and its yearly test

The incident response plan is approved and tested once a year, for example with a one-hour tabletop exercise. Make it a manual
source with the checks `incident.response_plan_approved` and `incident.response_plan_tested`, due every 366 days, with a runbook and
an evidence plan row (see [Manual evidence](manual-evidence.md)), as the example repo does.

## What stays in CISO Assistant

The incidents, their timelines, severity and status are recorded in CISO Assistant, never in the repo. The repo holds only the
obligations: who must be told, and how soon.
