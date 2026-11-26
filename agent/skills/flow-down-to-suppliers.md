---
name: flow-down-to-suppliers
description: "Record the security clauses a prime's contract imposes and check that each supplier contract carries the ones that must flow down. Use when a customer contract arrives, when a prime asks about the supply chain, or when the weekly review lists contract.flow_down_missing."
---
## Flow requirements down to suppliers

The reference is [docs/scope.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/scope.md#customer-contracts-and-flow-down).

1. A prime you supply is declared like a supplier, in its own file under `scope/third-parties/` with a `CUS-` ref_id and
   `customer: true` on its contract. Its `obligations` list each clause (for example DFARS 252.204-7012 or an Essential Eight maturity
   level), the `framework` and `level` it imposes, the `data` kinds it concerns, and `flow_down: true` when your own suppliers must
   carry it too.
2. Each supplier contract lists the clauses it carries under its own `obligations`, by `clause` alone, from the contract's text.
3. Check it: `hound scope validate scope/ --config hound.yaml --json`. `contract.flow_down_missing` names each supplier contract that
   lacks a clause it owes; it blocks nothing and joins the weekly review until fixed. `contract.framework_unknown` means no programme
   tracks that framework yet: say so, and offer the skill "keep-frameworks-and-libraries".
4. Answer a prime's supply-chain questions with `hound supplier list --data cui --json` (or `pii`, `export_controlled`, `classified`):
   each supplier with its flowed-down clauses carried or missing. `hound programme list --json` names the customer that requires each
   programme.
5. A missing clause is fixed in the supplier's contract, not in the file: draft what to ask the supplier for, and record the clause
   only once the person confirms the signed contract carries it.

## Your part and the person's

- **Read the person's documents yourself, PDFs included.** The prime's contract and the supplier contracts say which clauses apply.
  Quote the clause and page you relied on.
- **Ask the person** what only they know: which contracts are signed, and what a supplier has agreed to. Ask once, in one short list.
- **Never contact a supplier or a prime.** Draft the request; the person sends it.
