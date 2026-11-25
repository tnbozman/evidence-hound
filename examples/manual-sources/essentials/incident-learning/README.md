# Runbook: Incident learning and detection

ASD Essentials: cyber security incident insights; event detection. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. The post-incident review of each incident in the period: what let each stage happen and what was changed.
2. The latest review of monitoring coverage and the incident response plan against those lessons.
3. The logging platform's settings showing events are time-synchronised, parsed into a common format and enriched (asset, user).

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every 6 months, and after each significant incident. The source is overdue 183 days after the last attested file.

## Who attests

The security officer, or whoever runs incident response. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source incident-learning <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `incident.post_incident_reviewed` | IT.RES-04.1 | Each incident in the period had a post-incident review that found the gaps it exploited. |
| `incident.response_coverage_evaluated` | IT.RES-04.2 | Monitoring coverage and the incident response plan were evaluated against incident lessons. |
| `logging.events_normalised` | IT.DET-02.1 | Event logs are normalised, time-synchronised and enriched so they can be correlated. |
