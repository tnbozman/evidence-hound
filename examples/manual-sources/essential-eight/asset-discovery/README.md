# Runbook: Asset discovery and vulnerability scanner

Essential Eight: Patch applications, Patch operating systems. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. Export the asset discovery tool's schedule and its run history for the quarter (CSV or PDF), showing a run at least every 14 days.
2. Export the vulnerability scanner's settings page or report header showing the date its vulnerability database was last updated.

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every 3 months. The source is overdue 92 days after the last attested file.

## Who attests

The IT owner, who runs the discovery tool and the scanner. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source asset-discovery <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `vuln.asset_discovery.fortnightly` | ISM-1807 | An automated asset discovery runs at least fortnightly. |
| `vuln.scanner.database_current` | ISM-1808 | The vulnerability scanner's database is up to date. |
