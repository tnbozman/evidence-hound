---
name: answer-a-prime-questionnaire
description: Draft answers to a prime's or customer's security questionnaire (for example NIST SP 800-171 questions) from the Essential Eight evidence already in CISO Assistant, using hound mapping crosswalk. Use when the person receives a questionnaire.
---
## Answer a prime's questionnaire

The guide is [docs/adopt/answer-a-prime-questionnaire.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/adopt/answer-a-prime-questionnaire.md).

1. Get today's crosswalk: `hound mapping crosswalk --from essential-eight --config hound.yaml --json > crosswalk.json`. When CISO
   Assistant is not reachable from here, add `--offline`: the rows are the same, without today's results, so say every answer is unchecked.
2. The crosswalk is advisory: drafted with AI and not reviewed unless its `reviewed` field says who and when. Tell the person so.
3. For each question, find the requirement it asks about, if any. Draft an answer from the evidence and today's result named there:
   - **answered**: point at the Essential Eight evidence named;
   - **partly answered**: say what the evidence covers, and ask the person what else they do;
   - **open**, or a result that is not compliant: say so plainly. Never claim more than the evidence shows.
   Name the requirement and result each answer rests on, and mark every answer you are unsure of.
4. For questions about suppliers handling CUI, use `hound supplier list --data cui --json`.
5. Offer verifiable proof instead of screenshots: the audit export, the month's posture snapshot, and the evidence revision for a
   requirement the prime cares about.
6. The person reads every answer and sends the questionnaire. Suggest they save the completed questionnaire as evidence in CISO
   Assistant, so the next one starts from it.
