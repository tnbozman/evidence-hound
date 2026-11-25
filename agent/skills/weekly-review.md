---
name: weekly-review
description: Prepare the person's weekly review session with hound review pending, so every item has a recommendation with reasons and sources, and answer the challenges they left last week. Use before the weekly compliance review, or when the person asks what is waiting on them.
---
## Prepare the weekly review

The person has a few hours a week for compliance. Everything that needs them is signed off in one session, `hound review`, which they
run in their own terminal. Your job is to have every item ready before they start, so that each one takes a minute to read and decide.

1. See the whole week first: `hound next --repo . --config hound.yaml --json` lists the review queue with collectors that failed or
   missed their schedule, manual sources due, exceptions expiring and scope changes not yet applied, most urgent first, each with its
   command and skill. Fix what you can before the session (a collector, an upload the owner has sent); the rest goes in your summary.
2. Prepare in one step: `hound review prepare --repo . --config hound.yaml --json` refreshes the queue, lists in
   `without_recommendation` each item you still have to draft, with the skill that drafts it, and saves the scope plan the person's
   session applies (`plan`: the number of changes and the file, or why there is none). Then read the items:
   `hound review pending --repo . --config hound.yaml --json`. Each item has its `kind`, `key`, `title`, `why`
   (from the requirement), `effect` (what accepting it does), `deadline`, `minutes`, `recommendation` (when one is drafted) and
   `challenge` (when the person challenged it last week). The answer's `minutes` is the total; most urgent first.
3. For each item without a recommendation, draft one with the skill for its kind. Never decide for the person; draft.
   - `capability`, `not_applicable`, `system`: the skill "onboard-a-system-with-hound" (`hound onboard <system> decide ... --reasoning ...`).
   - `supplier`, `supplier_review`: the skills "onboard-a-third-party" and "assess-a-supplier".
   - `audit`, `imported_row`: the skills "onboard-an-audit" and "import-an-audit-workbook".
   - `manual_evidence`: the skill "process-manual-evidence" (`hound manual propose`).
   Then record what you recommend for each item that is not manual evidence (its proposal is already the recommendation):
   `hound review recommend <key> --repo . --answer <accept|decline> --reason "<why>" --source "<file, page or extract>"... --json`
   (a supplier review takes `ok`, `warning` or `blocker`). Give reasons the person can check and the sources you used: a file in the
   repo, a page of the supplier's trust centre, a line of the export. Recommend `decline` when the facts say so, and say what would
   change your answer.
4. Answer each challenge. The person's note is in `review/challenges.json` under the item's key. Read it, find out what they asked, then
   change your draft with the same command, or keep it, and record your answer as the item's recommendation with
   `hound review recommend`, its reason answering their note. Do not edit `review/challenges.json` or `review/recommendations.json`:
   a challenge and a recommendation go once the person accepts the item.
5. Run `hound review prepare --repo . --config hound.yaml --json` again: `without_recommendation` is empty when every item has one,
   and the saved plan is fresh. Run it last, after any change to the scope files, or the session plans again.
6. Tell the person, in a few lines: how many items, about how long the session takes, what is most urgent and why, and how you answered
   each challenge. Then tell them to run `hound review` in their own terminal (`--for 30m` if they have less time), or, to go through the items
   with you one at a time first, use the skill "work-through-pending-decisions" and accept each with `hound review --only <key>`.

## Your part and the person's

- **You prepare; the person decides.** Never run `hound review`, `hound approve` or `hound manual approve`, never pipe answers into
  them, and never write a file under `review/sign-offs/` or `review/standing-approvals.json`.
- **Standing approvals are the person's.** In the session they can accept a source's unchanged re-attestations for 12 months. You may
  say where one would save them time; never ask them to sign one for a first approval, a not-applicable decision or a risk acceptance.
- **Ask the person** only what you cannot find out: ask once, in one short list, before the session, not during it.
