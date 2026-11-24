# Runbook: weekly compliance review

CISO Assistant emails this to the GRC team each week. Set aside an hour; most weeks take less.

## Before the session

Ask your agent to prepare the queue. It runs `hound review pending --json`, reads each item, and writes its
recommendation and the sources it used. It also answers any challenge you left last week in `review/challenges.json`.

## The session

1. Run `hound review` at a terminal in the compliance repo (add `--for 30m` if you have less time).
2. For each item, read why it matters and what the agent recommends, then accept, challenge or skip it.
   A challenge goes back to your agent with your note; nothing changes until you accept.
3. When an unchanged export comes back from the same source, you can accept that source's future unchanged
   exports for 12 months. You can see every standing approval at the end of each session.
4. Read the plan hound shows you and confirm it. That writes your decisions to CISO Assistant.

## Record it

hound writes your sign-off to `review/sign-offs/`. Commit it, then mark this week's task done in CISO Assistant.
