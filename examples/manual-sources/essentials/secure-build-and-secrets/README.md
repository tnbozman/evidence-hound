# Runbook: Secure build and secrets

ASD Essentials: secure system lifecycle; identity, credential and access management. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. The design review record for systems built or changed this quarter: how they give visibility, enforce authorisation and fail safely.
2. The build and deployment pipeline's controls: who can change it, required reviews, signed artifacts.
3. Where keys, secrets and tokens are stored (the approved vault), and the last secret scanning report of code, configuration and files, with what was fixed.

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every 3 months. The source is overdue 92 days after the last attested file.

## Who attests

The development or platform lead. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source secure-build-and-secrets <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `build.secure_design_reviewed` | IT.PRO-01.1 | New and changed systems had a security design review before use. |
| `build.pipeline_controls_enforced` | IT.PRO-01.2 | Build and deployment processes enforce the security controls the same way every time. |
| `secrets.stored_in_approved_vault` | IT.PRO-13.2 | Keys, secrets and tokens are kept in an approved secrets store, not in code or files. |
| `secrets.scanning_remediated` | IT.PRO-13.3 | Code, configuration and file shares are scanned for exposed secrets, and every finding was fixed. |
