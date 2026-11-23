---
name: assess-a-supplier
description: Judge a third party's dependency, penetration, maturity, trust and criticality levels from its documents, each with a reason. Use with the skill onboard-a-third-party when drafting a supplier.
includes: [supplier-assessment, supplier-assurance-documents]
---
## Assess a supplier

1. Look in `scope/third-parties/` first. If the supplier is there, do not draft it again.
2. Gather the facts from what the person gives you and from the supplier's public assurance pages. Give each level a reason a reviewer can check. Leave out a level you cannot support; a person decides it later.
3. Draft the file with one command. It never overwrites a file:
   `hound supplier draft --repo . --system <system> --ref <SUP-...> --name "<supplier>" --solution-ref <SOL-...> --solution-name "<what it provides>" --contract-ref <CTR-...> --contract-name "<contract>" --level "<name>=<value>:<reason>" --json`
   Levels: `dependency` and `penetration` 0 to 4; `maturity`, `trust` and `criticality` 1 to 4. Repeat `--level` for each.
4. The contract is a draft and the review is open. A person approves the supplier and concludes its review with `hound approve` in a terminal. Tell them so, and what you could not find out.
