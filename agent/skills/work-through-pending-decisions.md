---
name: work-through-pending-decisions
description: Walk the person through every decision waiting on them, one at a time and highest risk first, with your case and sources for each, then give them the one command that accepts or challenges it. Use when the person asks what is waiting, before or instead of a long hound review or hound approve session, or when they want help deciding.
---
## Work through pending decisions with the person

The person has a few hours a week. A long session in a terminal invites them to accept everything without reading it, so you go through the
decisions with them first, one at a time, and they then accept or challenge each by its key. You research, explain and recommend; the
person decides, in their own terminal.

1. Start from the prepared week: `hound review prepare --repo . --config hound.yaml --json` refreshes the queue and saves the plan the
   session applies; draft any item in `without_recommendation` with the skill it names. Then list what is waiting:
   `hound review pending --repo . --config hound.yaml --json`. Open with its `"summary"` (each kind and recommended answer, with counts
   and minutes) and its `"high"` count: say how many decisions there are, the time it will take, and which kinds matter most. Then take
   the items in this order: `"risk": "high"` first (a requirement ruled not applicable, a supplier let in, a finding, or an item whose
   recommendation is missing or rests on no source), then by deadline.
2. For each item, one at a time:
   - Say what it is, in plain words: the requirement or supplier, `why` it matters, and `effect` (what accepting it does).
   - Check around the edges before you recommend: read the files the item names, the requirement's text, the system's or supplier's
     documents, and any earlier challenge in `challenge`. For a not-applicable decision, check that the requirement truly cannot apply
     to the system, not only that nobody has looked. Say what you checked and what you could not.
   - Record your case: `hound review recommend <key> --repo . --answer <accept|decline> --reason "<why>" --source "<file, page or extract>"... --json`
     (a supplier review takes `ok`, `warning` or `blocker`; manual evidence already carries its proposal from `hound manual propose`).
     Recommend `decline` when the facts say so, and say what would change your answer.
   - Ask the person what they think. Answer their questions, and go back to the files when they doubt something.
   - Give them the one command to run in their own terminal when they are ready: `hound review --only <key> --repo . --no-apply`
     (repeat `--only` for items the same evidence settles: present together the items one document or export decides, but
     each is still answered by its own key).
     They accept, challenge with a note for you, or skip.
3. When the person challenges an item, their note is in `review/challenges.json` under its key. Research again, change the draft with
   the command for its kind (`hound onboard <system> decide`, `hound supplier draft`, `hound manual propose`), record your answer with
   `hound review recommend`, and take it to them again.
4. After a batch, run `hound review pending --repo . --config hound.yaml --json` again and say what is left. Once the person has accepted
   what they want, they apply it: `hound scope plan scope/ --config hound.yaml`, then `hound scope apply scope/ --config hound.yaml`
   when they have read the plan.

Never run `hound review` or `hound approve` yourself, never pipe answers into them, and never edit `review/`, `onboarding/` or a
`.attest.yaml` file to record a decision. Both commands refuse without a person at a terminal: no evidence without a person.
