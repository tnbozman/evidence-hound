# Runbook: Security awareness training

ASD Essentials: cyber security awareness training; DISP: personnel security. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. The training completion report: every staff member and contractor, the course and the date completed.
2. The course outline, with the date it was last updated and what changed (new threats, incidents, lessons).

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every year. The source is overdue 366 days after the last attested file.

## Who attests

The person who runs training, usually the security officer. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source security-awareness-training <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `training.awareness_completed` | IT.PRO-14.1, DISP-PER-04 | Everyone completed security awareness training suited to their role in the last year, including new starters. |
| `training.awareness_content_current` | IT.PRO-14.2, DISP-PER-04 | The training was updated in the last year to reflect current threats. |
