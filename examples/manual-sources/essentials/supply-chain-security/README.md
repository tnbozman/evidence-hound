# Runbook: Supply chain security

ASD Essentials: cyber supply chain security. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. How software, components and updates are checked before use: signatures, hashes, trusted sources and approved stores.
2. The vendor advisories and threat feeds you follow for the products you use, and the last advisory you acted on.

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every 3 months. The source is overdue 92 days after the last attested file.

## Who attests

The IT lead who installs software and updates. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source supply-chain-security <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `supply_chain.software_integrity_verified` | IT.PRO-16.1 | Software, components and updates are checked for integrity and provenance before use. |
| `supply_chain.advisories_monitored` | IT.PRO-16.2 | Vendor advisories and threat intelligence are followed for the suppliers and products in use, and acted on. |
