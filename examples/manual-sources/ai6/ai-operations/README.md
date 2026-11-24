# Runbook: AI operations records

AI6 (Guidance for AI Adoption): the day-to-day records of the AI systems in use that CISO Assistant does not hold. No collector
reads these, so a person exports them, an agent may propose a result with `hound manual propose`, and the owner attests it with
`hound manual approve` at a terminal. The next `hound run` publishes the result to every AI6 row below. CISO Assistant emails
this runbook to the owner before each due date when an evidence plan row points at it (docs/manual-evidence.md).

## What to export

1. For each AI system in use: who it affects, as recorded in its screening or impact assessment.
2. The concerns and feedback about AI outcomes this quarter, and what the review of them found.
3. The AI incidents this quarter (an AI system got something wrong that mattered), with what was done and learned. None is a valid answer.
4. How each AI system's performance was watched this quarter: the spot checks or reports its owner reviewed.
5. For each AI system: that its data and accounts are covered by the cyber programme (for example the system is in scope of the
   Essential Eight audit, or its supplier is assessed).

Do not send passwords, keys or personal details beyond names and roles. Export records, not screenshots, where you can.

## How often

Every 3 months. The source is overdue 92 days after the last attested file.

## Who attests

The AI system owners, collected by the GRC team. An agent may read the export and propose a result for each check; only the owner
attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source ai-operations <file>`. The GRC team checks the file is complete, records it
against the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `ai.stakeholders_identified` | AI6-2.1 Identify and engage stakeholders | Every AI system in use has the people it affects recorded. |
| `ai.systemic_issues_reviewed` | AI6-2.3 Monitor for systemic issues | This quarter's feedback about AI outcomes was reviewed for patterns. |
| `ai.incidents_reviewed` | AI6-3.4 Monitor and report incidents | Every AI incident this quarter was recorded, investigated and learned from. |
| `ai.performance_monitored` | AI6-5.2 Monitor system performance | Every AI system in use was watched this quarter in proportion to its risk tier. |
| `ai.data_and_cyber_covered` | AI6-5.4 Data and cybersecurity measures | Every AI system's data and accounts are in scope of the cyber programme. |
