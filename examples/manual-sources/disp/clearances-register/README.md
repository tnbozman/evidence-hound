# Runbook: Clearances register review

DISP: personnel security. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. The register of staff who hold or need a security clearance: level, expiry and the work that needs it. Names and levels only: no clearance case details.
2. Pre-employment screening records for new starters (identity, right to work, references), as dates and outcomes only.
3. The off-boarding records for leavers: access removed and clearance sponsorship ended.

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every 6 months. The source is overdue 183 days after the last attested file.

## Who attests

The security officer. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source clearances-register <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `personnel.clearances_register_reviewed` | DISP-PER-01 | The clearance register was reviewed in the period, every holder needs their clearance, and none has expired. |
| `personnel.pre_employment_screened` | DISP-PER-02 | Every new starter was screened before access. |
| `personnel.leavers_offboarded` | DISP-PER-03 | Every leaver's access was removed and Defence told where a clearance was sponsored. |
