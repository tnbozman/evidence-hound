# Third parties

Your supplier register lives in the compliance repo, one file per supplier, and `hound` applies it to CISO Assistant. It replaces the
supplier spreadsheet: a new supplier, a renewed contract or a concluded review is a small change a person reviews, and CISO Assistant's
third-party features (entities, solutions, contracts, entity assessments, tiers) show the result.

This guide is the how-to. The file format, every field and every validation code are in the scope reference:
[third-parties/&lt;entity&gt;.yaml](scope.md#third-partiesentityyaml), [Supplier reviews and tiers](scope.md#supplier-reviews-and-tiers)
and [Supplier approval](scope.md#supplier-approval).

## Why it matters

Frameworks ask the same questions about suppliers in different words: the Essential Eight and the ASD Essential Series through the
systems a supplier runs for you, CMMC through the service providers that handle or protect your controlled information, and every prime
contractor's supplier questionnaire through who you depend on. A small team cannot keep three registers. One register in the repo, with
each supplier's reasons and evidence beside it, answers all of them, and the systems a supplier provides are registered only once a
person has approved it.

## Moving the supplier spreadsheet with your agent

A small team does not need to type this up. `hound agent setup` gives your coding agent (Claude Code or GitHub Copilot) the skills
`onboard-a-third-party` and `assess-a-supplier`; it drafts, you decide.

1. **Give the agent what you have.** The spreadsheet exported as CSV, each supplier's contract or order form, and what the supplier
   publishes about its security: its trust centre page, certificates (such as ISO 27001), independent audit reports and its security
   contact. Text, PDF and web pages all work; nothing has to be retyped.
2. **The agent drafts each supplier** with `hound supplier draft`: the entity, the solution you buy, the contract as a draft, and an
   open review. Each level (dependency, penetration, maturity, trust, criticality) carries its reason, and the agent leaves maturity
   and trust undecided when no document backs them. A document you agree to keep goes under `documents/suppliers/<supplier>/`, so the
   reason can point at it. The contract's dates and cost come from the spreadsheet as options (`--start`, `--end`,
   `--annual-expense`, `--currency`), and `--description` says what the supplier does for you. A managed service provider that runs your
   own laptops and network is drafted with `--kind msp` and no `--for`.
3. **Check the draft.** `hound scope validate scope --config hound.yaml` reports any problem with a file, a line and a hint; the agent
   fixes them.
4. **You approve.** `hound approve`, in your own terminal, shows each supplier's levels and reasons, then asks you to make its
   contract active and conclude its review (`ok`, `warning` or `blocker`). An agent or a pipe is refused: approval is a person's step.
5. **Plan and apply.** `hound scope plan scope --config hound.yaml` shows what CISO Assistant will gain, and `hound scope apply` writes
   exactly that plan. Commit the change as a pull request, with the plan as its summary.

For worked examples, see the onboarding tutorial's four fictional suppliers in `examples/onboarding-tutorial/scope/third-parties/` (in the
Evidence Hound repo): a cloud host, a firewall vendor and two open-source projects, each with its levels, reasons, contract and review.
The [onboarding tutorial](examples/onboarding-tutorial.md) shows them being drafted and approved.

For CMMC Level 2, the register is where you show a prime which service providers handle or protect your controlled unclassified
information (CUI). Record it in each solution's `data` with the authority that allows it, such as a FedRAMP Moderate equivalent, its
evidence under `documents/suppliers/` and its expiry ([Sensitive data](scope.md#sensitive-data)). `hound supplier list --data cui` then
answers the prime, and the CMMC Level 2 audit ([CMMC Level 2](adopt/cmmc-level-2.md)) assesses the practices.

## Keeping it current

- **Reviews come due.** With `review_every`, the plan opens the next review a cycle after the last one's due date, and a review past due
  without a conclusion means the supplier is no longer approved: the plan lists its systems under **Suppliers** for the security
  committee. Ask your agent to re-read the supplier's trust centre and documents before the due date and update the reasons; you
  conclude the review with `hound approve`.
- **Contracts renew.** An `active` contract whose end date has passed is reported (`supplier.contract_lapsed`). Record the new end
  date, or mark it `expired` or `terminated`.
- **Tiers.** CISO Assistant computes each supplier's tier from its levels. If you decide a tier yourself, set `forced_tier` with a
  reason, and the plan tells you when CISO Assistant's tier differs.

## Sensitive data and who may hold it

A prime, an auditor or the security committee will ask which suppliers handle CUI, personal information, export-controlled data or
classified information, and on what authority. Record it on each solution's `data` (and on each system that holds it), with the
authority and its expiry, and `hound scope validate` warns of a system holding data through a supplier that is not approved for it, and
of an authority that has lapsed: a warning blocks nothing, and the weekly review lists it until it is fixed. `hound supplier list --data <kind>` prints the list to send. The keys, the four kinds with an Australian and
a US example of each, and what `hound` leaves to the owner and the prime are in
[scope.md](scope.md#sensitive-data).

An agent drafting a supplier records the kinds a trust centre or contract names with `hound supplier draft --data <kind>:<role>[:<detail>]`
and leaves the authority empty: a person adds it once the certificate, assessment or licence is in the repo.

## Representatives are personal data

`representatives` (people at the supplier, by email) are optional. They are personal data, so keep them in CISO Assistant instead
unless the compliance repo is private and only the GRC team can read it. Never add a representative from a public source without a
reason to contact them, and remove one who leaves the supplier. `hound` never gives a representative a login.

## What stays in CISO Assistant

`hound` sets up the register and the reviews; the assessment work happens in CISO Assistant, and `hound` never writes it:

- questionnaire answers and the audit CISO Assistant creates for a review with a `questionnaire`;
- tiering results, the tier itself and its history;
- campaigns sent to suppliers;
- the expiry date of a review's result.

A review's **conclusion** is the exception: you record it in the repo with `hound approve`, so the reason a supplier is approved sits
beside the supplier, and `hound scope apply` writes it to CISO Assistant.
