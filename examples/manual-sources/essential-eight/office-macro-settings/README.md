# Runbook: Microsoft Office macro settings

Essential Eight: Restrict Microsoft Office macros. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. Export the Office macro policy (Group Policy or Intune) and the list of people with a demonstrated business need for macros.
2. Export the antivirus policy showing Office macro scanning (AMSI) is on.
3. Show that users cannot change the macro settings (the policy is enforced, not a default).

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every 6 months. The source is overdue 183 days after the last attested file.

## Who attests

The endpoint owner, who manages Office policies. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source office-macro-settings <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `office.macros.disabled_without_need` | ISM-1671 | Macros are disabled for users without a demonstrated business need. |
| `office.macros.antivirus_scanning` | ISM-1672 | Office macro antivirus scanning is enabled. |
| `office.macros.settings_locked` | ISM-1489 | Users cannot change macro security settings. |
