---
name: onboard-a-third-party
description: Draft a third party in the scope - a vendor, a reseller or a managed service provider - with its solution, contract, levels and the documents read. Use before onboarding a system, for each party in its supply chain.
---
## Onboard a third party

Every party in a system's supply chain is its own third party: the vendor that makes the product, the reseller it was bought through, the managed service provider that runs it.

1. Look in `scope/third-parties/` first. Do not draft a party that is already there.
2. Draft the vendor first, then each reseller or managed service provider, naming the vendor it stands for:
   `hound supplier draft --repo . --kind <vendor|reseller|msp> [--for <vendor ref_id>] --ref <SUP-...> --name "<name>" --solution-ref <SOL-...> --solution-name "<what it provides>" --contract-ref <CTR-...> --contract-name "<contract>" --level "<name>=<value>:<reason>"... --document <path in the repo>... [--system <system>] --json`
3. Levels follow the skill "assess-a-supplier": each needs a reason a reviewer can check, and a level you cannot support is left out for a person to decide.
4. Keep each document you relied on in the repo under `documents/suppliers/` and name it with `--document`; hound records it with its hash. Do not keep a document the person has not agreed to keep.
5. The command never overwrites a file. To change a draft, edit its file, then run `hound scope validate scope/ --config hound.yaml --json`.
6. A system bought through a reseller or run by a provider names that party as `supplied_through` in `scope/setup.yaml`; say so when you onboard the system.

## Your part and the person's

- **Read the person's documents yourself, PDFs included.** Contracts, order forms, assurance reports (SOC 2, ISO 27001 certificates, IRAP letters) and the vendor's trust pages say who the party is, what it provides and how far it can be trusted.
- **Ask the person** what only they know: who they bought from, who runs the product day to day, when the contract started and ends, and whether a document may be kept in the repo. Ask once, in one short list, and wait for the answer.
- **Never approve.** What you record is a draft. The person approves it with `hound approve` in their own terminal.
