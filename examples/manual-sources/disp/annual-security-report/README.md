# Runbook: Annual security report to Defence

DISP: governance and reporting. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. The annual security report as sent to Defence, and Defence's acknowledgement.
2. The security incident and contact reports sent to Defence during the year (dates and reference numbers only).

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every year, by the date Defence sets. The source is overdue 366 days after the last attested file.

## Who attests

The chief security officer, who signs the report. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source annual-security-report <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `disp.annual_report_submitted` | DISP-GOV-04 | The annual security report was submitted to Defence on time and acknowledged. |
| `disp.incidents_reported` | DISP-INF-03, DISP-PER-05 | Security incidents and suspicious contacts were reported to Defence as required, and a record of each report is kept. |
