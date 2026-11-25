# Runbook: Security officer and governance

DISP: governance. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. The appointment letters of the chief security officer and security officer, with the training each has done.
2. The security policy and plan approved by the business, with the date of approval.
3. The register of foreign ownership, control and influence, and of changes Defence must be told about.

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every year, and when the security officer or chief security officer changes. The source is overdue 366 days after the last attested file.

## Who attests

The chief executive or the board member who appointed the security officer. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source security-officer <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `disp.security_officer_appointed` | DISP-GOV-01 | A chief security officer and a security officer are appointed and trained. |
| `disp.security_policy_approved` | DISP-GOV-02 | A security policy and plan are approved and reviewed in the last year. |
| `disp.foreign_interest_declared` | DISP-GOV-03 | Foreign ownership, control or influence and changes to it are recorded and reported to Defence. |
