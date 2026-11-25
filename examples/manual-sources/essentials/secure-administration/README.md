# Runbook: Secure administration

ASD Essentials: secure administration. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. The settings that control privileged sessions: just-in-time access, approval, session time limits and re-authentication before each administrative session.

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every 6 months. The source is overdue 183 days after the last attested file.

## Who attests

The IT lead who administers the systems. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source secure-administration <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `admin.privileged_sessions_controlled` | IT.PRO-05.2 | Privileged sessions need an explicit, authorised start (approval, time limit, re-authentication) before administration begins. |
