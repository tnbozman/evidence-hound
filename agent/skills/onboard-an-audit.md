---
name: onboard-an-audit
description: Draft an audit of a framework for named systems. Use when the person wants a new audit (a new year, a new framework or a new group of systems) in the scope.
---
## Onboard an audit

1. Check what the scope holds: the frameworks, domains, perimeters and systems in `scope/setup.yaml`, and the audits in `scope/audits/`.
2. Draft the audit with one command:
   `hound audit draft --repo . --ref <ref_id> --name "<name>" --framework <framework URN> --group <group>... --domain "<domain>" --perimeter "<perimeter>" --system "<system>"... --due <yyyy-MM-dd> --json`
   hound refuses a framework, domain, perimeter or system the scope does not hold, and a ref_id already used.
3. The draft is written to `onboarding/drafts/audits/`. It is not in the scope, and no plan shows it, until the person approves it with `hound approve`, which moves it into `scope/audits/`.
4. To bring results from an earlier audit, use the skill "import-an-audit-workbook" once the audit is approved.

## Your part and the person's

- **Read the person's documents yourself, PDFs included.** An earlier audit report or a statement of applicability says which framework, maturity level and systems an audit covered.
- **Ask the person** what only they know: which systems this audit covers, which maturity level or groups, and when it is due. Ask once, in one short list, and wait for the answer.
- **Never approve.** What you record is a draft. The person approves it with `hound approve` in their own terminal.
