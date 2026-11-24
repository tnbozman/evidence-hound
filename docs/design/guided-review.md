# Design: one guided review a week

Status: Agreed and built in s10-07 (Nov 24). Builds on [onboarding driven by the person's own coding agent](agent-driven-onboarding.md). The build is
task s10-07 in [the s10 sprint](../sprints/s10-third-parties.md); later sprints add their own items to the same queue.

## Why

The maintainer's direction (Nov 24): the Cyber GRC member at a small supplier has **2 to 8 hours a week** for all of it. AI should
help the team learn, mature, operationalise and sustain compliance, and come as close as it can to approving everything itself. Every
human review and approval should be gathered into one simple, direct process: alert the person, then a batched session in which the
agent guides them through each recommendation (read, understand, accept or challenge) and then applies what they accepted.

Today a person signs off in several places: `hound approve` for onboarding decisions and suppliers, `hound manual approve` for
evidence, reading `hound scope plan` before `apply`, and, from later sprints, an SSP version, a risk acceptance and a report. Each is
sound on its own, but together they ask the person to know where to look and when.

## Decision

1. **One queue.** `hound review pending --json` lists everything that waits on a person, from every source, in one shape: what it
   is, why it matters, the agent's recommendation with its reasons and sources, the effect of accepting it (the plan it would apply),
   the deadline it serves, and an estimate in minutes. Sources: undecided onboarding decisions and not-applicable rows, supplier
   approvals and review conclusions, manual evidence proposals, scope changes waiting to be applied, and later SSP versions (s11), POA&M
   items and risk acceptances (s15), and committee actions (s12).
2. **The agent prepares; the person decides once.** Before the session the person's agent works the queue: it drafts each
   recommendation through the existing prompt-free commands (`onboard decide`, `manual propose`, `supplier draft`, `scope plan`), and
   attaches its reasons and the extract that supports it. Nothing it prepares takes effect.
3. **One session, in order.** `hound review` (a terminal; an agent or a pipe is refused, as `hound approve` is) walks the queue
   most-urgent first and stops at the time the person gives (`--for 60m`). For each item it shows the recommendation in plain words and
   asks: accept, challenge (with a note the agent answers before the next session, the item stays queued), or skip. At the end it shows
   what will change and applies the accepted items in one batch: the same approved files, `scope apply` and the next `run` publish as
   today. One sign-off record names the person, the time and every item accepted.
4. **Alert, not chase.** A recurring CISO Assistant task assigned to the GRC team ("Weekly compliance review", from the evidence plan in
   s10-04) emails the person before each session with the queue's size, the time it needs and anything due this week. Nothing else is
   emailed. Items past their deadline are listed in the committee report (s12-05), as overdue manual evidence is.
5. **Standing approvals shrink the queue.** The person may approve a rule instead of each instance, for a year, in the same session:
   for example, "a manual source whose new export has the same checks and the same results as last time may be re-attested by its
   owner without review". The rule is written to the repo, shown in every session's summary, and expires. A rule can never cover a
   not-applicable decision, a risk acceptance or a first approval.
6. **Learning is part of the session.** Each item carries a short "why this matters" from the requirement and the mapping, so the person
   learns the framework as they work through it, and a challenge teaches the agent. The session summary says what the team has now
   operationalised (sources automated, items that needed no change) so maturity is visible.

## Rules that do not change

- No model in evidence collection. Collectors stay deterministic; the agent proposes only after collection.
- No evidence without a person: an attestation is accepted in the session (or by a standing approval the person signed), never by the
  agent alone.
- "Met by another system" stays open, and a not-applicable decision is always reviewed one by one.
- CISO Assistant stays the system of record; the queue is read from the repo and CISO Assistant, not a new store.

## Proposed tasks

- s10-07 Review queue and guided weekly session (this sprint): `hound review pending --json`, `hound review`, the sign-off record, the
  weekly task in the example evidence plan, and the skill that prepares the queue.
- s11, s12, s15: each new kind of approval (SSP version, POA&M item, committee action, risk acceptance) joins the queue rather than
  adding its own command.
- s12-03: the operating guide is built around the weekly session and the 2 to 8 hour budget.

## As built (s10-07)

- `hound review pending --json` lists what `hound approve` and `hound manual approve` would ask, in one shape. Items that become ready
  only once another is accepted (a system after its supplier and not-applicable decisions) are asked in the same session, marked
  "now ready", rather than listed beforehand.
- The agent records its recommendation for any item with `hound review recommend` (kept in `review/recommendations.json`), and answers
  a challenge the same way. A supplier review joins the queue 14 days before it is due (s10-05).
- The one standing rule is `unchanged_reattestation`, for 12 months, per manual source. It is offered after a person accepts an
  unchanged attestation.
- Scope changes waiting to be applied are not items: the session ends by showing the plan and applying it after a confirmation.
- The weekly task's email is CISO Assistant's own task reminder with the runbook; it does not yet carry the queue's size.

## Open questions

- Who may sign a standing approval when the authorising official is not the GRC member.
- Whether a challenge should reach the system owner as well as the agent.
