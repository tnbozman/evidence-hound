# Runbook: Content filtering

ASD Essentials: content filtering. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. The firewall or secure web gateway's outbound policy: data loss prevention rules and the categories and reputation lists it blocks.
2. The email and web filtering policy for inbound content: attachment types blocked, sandboxing and malware scanning.
3. A sample of recent blocks from the logs, to show the rules are in force.

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every 3 months. The source is overdue 92 days after the last attested file.

## Who attests

The network owner, who runs the firewall or web and email filtering service. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source content-filtering <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `network.content.outbound_inspected` | IT.PRO-09.1 | Outbound content is inspected to detect unauthorised transfer of sensitive data. |
| `network.content.malicious_destinations_blocked` | IT.PRO-09.2 | Outbound connections to known malicious or untrusted destinations are blocked. |
| `network.content.inbound_inspected` | IT.PRO-09.3 | Inbound content from the internet, email and removable media is inspected and unsafe content blocked. |
