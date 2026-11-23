# Runbook: Application control review

Essential Eight: Application control. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. Export the application control policies (Intune, AppLocker or the endpoint product) as XML or PDF.
2. Show the locations they cover: user profiles, temporary folders, and every other location, on workstations and internet-facing servers.
3. Export the record of this year's ruleset review, and show Microsoft's recommended block rules are in the policy.

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every 12 months. The source is overdue 366 days after the last attested file.

## Who attests

The endpoint owner, who manages the application control policies. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source application-control-review <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `appcontrol.rulesets_validated` | ISM-1582 | Application control rulesets are validated at least yearly. |
| `appcontrol.internet_facing_servers` | ISM-1490 | Application control is in place on internet-facing servers. |
| `appcontrol.user_profiles_and_temp` | ISM-1870 | Application control covers user profiles and temporary folders. |
| `appcontrol.all_other_locations` | ISM-1871 | Application control covers every other location. |
| `appcontrol.microsoft_blocklist` | ISM-1544 | Microsoft's recommended application blocklist is in place. |
