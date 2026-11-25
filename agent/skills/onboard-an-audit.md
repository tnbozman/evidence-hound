---
name: onboard-an-audit
description: Draft an audit of a framework for named systems. Use when the person wants a new audit (a new year, a new framework or a new group of systems) in the scope.
---
## Onboard an audit

1. Draft one audit per system, and one for each shared platform (a cloud tenancy or hosting platform other systems run on), not one
   audit for the whole organisation: each audit then shows that system's gaps. A system that relies on the platform for a requirement
   names it in that row's `provided_by`. Propose an organisation-wide audit only when organisation-wide sources, such as policies,
   feed it. Shared evidence is answered once: a document links into every audit of its framework that shares a system, and one
   `hound.yaml` target per audit lets one run feed them all.
2. Check what the scope holds: the frameworks, domains, perimeters and systems in `scope/setup.yaml`, and the audits in `scope/audits/`.
3. Draft the audit with one command:
   `hound audit draft --repo . --ref <ref_id> --name "<name>" --framework <framework URN> --group <group>... --domain "<domain>" --perimeter "<perimeter>" --system "<system>"... --due <yyyy-MM-dd> --json`
   hound refuses a framework, domain, perimeter or system the scope does not hold, and a ref_id already used.
4. The draft is written to `onboarding/drafts/audits/`. It is not in the scope, and no plan shows it, until the person approves it with `hound approve`, which moves it into `scope/audits/`.
5. To bring results from an earlier audit, use the skill "import-an-audit-workbook" once the audit is approved.

## Your part and the person's

- **Read the person's documents yourself, PDFs included.** An earlier audit report or a statement of applicability says which framework, maturity level and systems an audit covered.
- **Ask the person** what only they know: which systems this audit covers, which maturity level or groups, and when it is due. Ask once, in one short list, and wait for the answer.
- **Never approve.** What you record is a draft. The person approves it with `hound approve` in their own terminal.
