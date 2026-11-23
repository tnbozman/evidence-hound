# Runbook: Backups

Essential Eight: Regular backups. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. Export the backup service's job report for the quarter: what is backed up, how often, and whether each job succeeded.
2. Export its retention and immutability settings, and show backups are synchronised so they can be restored to a common point.
3. Export who can read, change or delete backups: ordinary users only their own, administrators other than backup administrators none.

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every 3 months. The source is overdue 92 days after the last attested file.

## Who attests

The backup owner, who runs the backup service. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source backups <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `backup.performed_and_retained` | ISM-1511 | Backups are performed and retained as the business continuity plan requires. |
| `backup.synchronised` | ISM-1810 | Backups are synchronised to restore to a common point in time. |
| `backup.retained_securely` | ISM-1811 | Backups are retained in a secure and resilient way. |
| `backup.unprivileged_cannot_read_others` | ISM-1812 | Unprivileged accounts cannot access other users' backups. |
| `backup.privileged_cannot_read` | ISM-1705 | Privileged accounts other than backup administrators cannot access other accounts' backups. |
| `backup.unprivileged_cannot_change` | ISM-1814 | Unprivileged accounts cannot change or delete backups. |
| `backup.privileged_cannot_change` | ISM-1707 | Privileged accounts other than backup administrators cannot change or delete backups. |
