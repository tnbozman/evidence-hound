# Training: running the AI programme (30 minutes)

A 30-minute session for the Cyber GRC team, built on [the AI governance walkthrough](../tutorials/ai-governance.md). It assumes no AI
knowledge. One person runs it on the local stack while the others watch and answer the screening questions; afterwards each person
follows the walkthrough alone (45 minutes).

**By the end, each person can:** say what AI6 asks at Foundations, screen a proposed AI system with the National AI Centre's questions, register it by pull request,
and read the AI6 rows and the register line the committee sees.

**Before the session:** the presenter runs steps 1 and 2 of the walkthrough, and everyone has read the
[primer](../ai-grc-primer.md) (15 minutes).

| Minutes | Topic | What happens | Walkthrough |
| --- | --- | --- | --- |
| 0 to 5 | Why we do this | The six AI6 practices in one sentence each, and what Foundations asks of a small team (the primer's "What you do not need to do at Foundations"). | |
| 5 to 8 | The register today | Show the register line from `hound programme list` and the assets labelled `ai-system` in CISO Assistant. | Step 2 |
| 8 to 15 | Screening | The group answers the seven questions for the search assistant and agrees the outcome. Discuss: why does a general-purpose model make almost any assistant elevated, and what would make it unacceptable? | Steps 3 and 4 |
| 15 to 20 | Registering by pull request | Add the `ai` block, validate, read the plan. Show one validation problem and its hint. | Steps 5 and 6 |
| 20 to 25 | Before go-live | Impact assessment, risk scenarios, the test and the go-live date: who does each, and why the order matters. | Steps 7 and 8 |
| 25 to 30 | Results and the committee | Run `hound`, read AI6-4.1 and AI6-5.1, and the new register line. Who fixes Example proposal assistant's missing test? | Steps 9 and 10 |

**Check understanding** with three questions at the end:

1. A supplier switches on an AI feature in a product you already use. What do you do first? (Screen it with the runbook, and check the
   supplier is approved.)
2. A system is elevated. What must be true before `in_use_from`? (Its impact assessment is approved, and it has been tested.)
3. AI6-5.1 is not compliant. Where do you look, and who fixes it? (Its observation names the system; the system's owner tests it and the
   GRC team records `last_tested_on` by pull request.)

**Afterwards:** each person follows the walkthrough alone and notes anything unclear for the GRC lead, who updates the walkthrough or
the runbook.
