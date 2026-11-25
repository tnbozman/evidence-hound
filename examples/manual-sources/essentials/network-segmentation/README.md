# Runbook: Network segmentation

ASD Essentials: network segmentation and segregation. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. The network zone diagram: which systems sit in which zone, and why (business criticality and trust).
2. The firewall rules between zones, showing traffic is restricted and logged.

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every 6 months, and after any network change. The source is overdue 183 days after the last attested file.

## Who attests

The network owner. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source network-segmentation <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `network.segmentation.zones_defined` | IT.PRO-18.1 | Systems are placed in network zones by criticality and trust, and traffic between zones is restricted and monitored. |
