# Runbook: Event logging and analysis

Essential Eight: Multi-factor authentication, Restrict administrative privileges, Application control, User application hardening. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. Export the central logging service's sources list, showing which event types arrive from which systems.
2. Export its retention and access settings, showing who can change or delete logs.
3. Export the quarter's triage records: alerts reviewed, by whom and when, including events from internet-facing servers.

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every 3 months. The source is overdue 92 days after the last attested file.

## Who attests

The security owner, who runs the central logging service. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source event-logging <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `logging.event_logs.protected` | ISM-1815 | Event logs are protected from unauthorised change and deletion. |
| `logging.internet_facing.analysed` | ISM-1906 | Internet-facing servers' event logs are analysed in a timely manner. |
| `logging.events.triaged` | ISM-1228 | Cyber security events are analysed in a timely manner. |
| `logging.mfa_events.centrally_logged` | ISM-1683 | Successful and unsuccessful MFA events are centrally logged. |
| `logging.privileged_access.centrally_logged` | ISM-1509 | Privileged access events are centrally logged. |
| `logging.privileged_accounts.changes_logged` | ISM-1650 | Privileged account and security group changes are centrally logged. |
| `logging.powershell.centrally_logged` | ISM-1623 | PowerShell module, script block and transcription events are centrally logged. |
| `logging.command_line.centrally_logged` | ISM-1889 | Command line process creation events are centrally logged. |
