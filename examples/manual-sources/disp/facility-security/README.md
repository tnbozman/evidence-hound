# Runbook: Facility and visitor controls

DISP: physical security. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. The visitor log summary for the period and the visitor procedure (sign in, escort, badge returned).
2. The access control report: who holds keys or access cards to the secure areas, reviewed against staff lists.
3. The facility security plan and its last review, and any certification of secure areas or containers.

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every 6 months. The source is overdue 183 days after the last attested file.

## Who attests

The facilities manager or office manager. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source facility-security <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `physical.visitors_controlled` | DISP-PHY-01 | Visitors are signed in, escorted and badged, and the log is kept. |
| `physical.access_reviewed` | DISP-PHY-02 | Access to the facility and its secure areas was reviewed in the period and matches current staff. |
| `physical.facility_plan_current` | DISP-PHY-03 | The facility security plan is current and secure areas meet their certification. |
