# Runbook: Cyber governance

ASD Essentials: governance and culture. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. The board or executive minute or charter that names who is accountable for cyber security and for the use of AI, and the risk appetite they set.
2. The approved risk management method (how risks are found, rated and treated) and the date it was last approved.
3. The latest risk assessment, showing which threats and attacker techniques were considered (for example from ASD's annual cyber threat report).
4. Who does cyber security work and how many hours a week, with the last review of whether that is enough.

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every year, and after any change to the board, executive or risk appetite. The source is overdue 366 days after the last attested file.

## Who attests

The executive accountable for cyber security (or the person the board names). An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source cyber-governance <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `governance.ai_accountability_defined` | IT.GOV-08.1 | An executive is accountable for the use, governance and risk of AI, and the accountability is recorded. |
| `governance.cyber_resourcing_assigned` | IT.GOV-04.1 | Named people with the needed skills run and maintain the security controls, and their role descriptions say so. |
| `governance.cyber_resourcing_reviewed` | IT.GOV-04.2 | Cyber security resourcing was reviewed in the last year against threats, incidents and changes in the business. |
| `governance.risk_method_approved` | IT.GOV-03.1 | An approved risk management method and plan exist and are used to assess and treat cyber security risk. |
| `governance.threats_considered_in_risk` | IT.IDE-04.1 | Risk assessments consider relevant weaknesses, attacker techniques and threat behaviour. |
