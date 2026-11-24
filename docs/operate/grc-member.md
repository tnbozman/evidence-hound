# GRC member: your week, month and quarter

You are the one person who runs compliance, with 2 to 8 hours a week and an AI agent that does most of the reading and drafting. hound
is built so that everything waiting on you arrives in one queue, with a recommendation, and nothing reaches CISO Assistant without you.

## Every week (1 to 2 hours)

1. CISO Assistant's weekly task emails you the [weekly review runbook](../../examples/compliance-repo/manual-sources/weekly-compliance-review.md).
2. Ask your agent: "Prepare this week's review." It runs `hound review pending --json` and records a recommendation, with reasons and
   sources, for each item (manual evidence, onboarding decisions, suppliers, drafted audits).
3. In your own terminal: `hound review --for 60m`. Read each item, then accept, challenge with a note, or skip. Challenged items come
   back next week with the agent's answer. Accepted items are applied in one batch at the end ([guided review](../manual-evidence.md#the-weekly-review)).
4. Look at `hound status --config hound.yaml`. A collector that failed or has not run is the one thing to chase this week.

What the agent does for you: reads every export and proposal, drafts the recommendations, answers your challenges, and keeps the queue
in order of urgency. What only you can do: accept, approve and attest.

## Every month (2 to 3 hours, first working days)

1. Ask your agent: "Take last month's snapshot and draft the committee report" ([reporting](../reporting.md)).
2. Read the draft's summary and actions; fix owners and due dates; refine the wording ([AI help](../govern/ai-assisted-reporting.md)).
3. Send it out, run the [committee meeting](../govern/security-committee.md) as secretary, and record its decisions in CISO Assistant.
4. Close the month's manual evidence tasks in CISO Assistant whose results were published.

## Every quarter (half a day)

1. Ask your agent to draft the board report for the quarter; give it to the authorising official to confirm the positions
   ([board reporting](../govern/board-reporting.md)).
2. Review exceptions and risk acceptances that expire next quarter; put renewals on the committee's agenda.
3. Supplier reviews due this quarter: the agent drafts each one; you approve in the weekly review.
4. If your audits run in periods, ask your agent to draft the next period's audit (`hound audit draft`); approve it in the review.

## Every year

- Re-read the [continuous compliance procedure](../govern/continuous-compliance-procedure.md) and the risk appetite with the board.
- Renew standing approvals that expire (the review lists them).
- Upgrade CISO Assistant and hound; snapshot new framework versions ([mapping files](../mapping-files.md#framework-snapshots)).

## When a prime asks a questionnaire

Use the [crosswalk](../adopt/answer-a-prime-questionnaire.md): one evidence set, many questionnaires.
