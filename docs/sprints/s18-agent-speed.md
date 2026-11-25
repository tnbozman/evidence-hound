# s18 Agent speed: fewer steps, faster runs

Status: In progress

Release: 2

Replanned Nov 25 2026. It was a web console for collectors. The maintainer asked for speed, not more clicks: the GRC member has 2 to 8
hours a week and works through their own coding agent, so a console would add a second place to look. This sprint makes the agent
faster and surer instead. CISO Assistant stays the system of record; `hound` stays prompt-free for agents, and a person still signs off
only in the one weekly `hound review` session.

**Goal:** the agent learns what needs doing from one read-only command, prepares the whole week in one step, uses skills for every
feature up to s17, and runs finish sooner. Measured by steps and requests, so the targets hold in CI.

### s18-01 One read-only "what now" for the agent
- [ ] Done

**Story:** As the GRC member's agent, I want one command that says what needs doing and in what order, so that I do not run five
commands and join their output before I can help.

**Acceptance criteria**
- `hound next --repo . --config hound.yaml --json` lists, most urgent first: items in the review queue (count and total minutes),
  collectors that failed, never ran or are overdue, manual sources due or overdue, security exceptions and risk acceptances expiring
  within 60 days or expired, scope changes not yet applied, and the CISO Assistant connection (reachable, token expiry).
- Each entry gives the exact `hound` command (or skill) that handles it, and an estimate in minutes.
- Read-only: it changes no file and writes nothing to CISO Assistant. Without a connection it says so and lists what it can offline.
- No evidence content and no secret is ever in the output.

**Tests first**
- `NextCommandTests.Lists_work_most_urgent_first_with_the_command_for_each`
- `NextCommandTests.Writes_nothing`
- `NextCommandTests.Offline_lists_what_it_can_and_says_the_connection_is_unknown`

**Functional check**
- `hound next --json` on a copy of the example repo against the local CISO Assistant lists the seeded failures and the expiring
  exception.

### s18-02 Prepare the week in one step
- [ ] Done

**Story:** As the agent, I want to prepare the weekly session with one command, so that the person's session starts with every item
ready and nothing left to look up.

**Acceptance criteria**
- `hound review prepare --repo . --config hound.yaml --json` runs the prompt-free preparation in order: refresh the queue, save a
  `scope plan` for the session to apply, and report which items still lack a recommendation and which skill drafts each.
- The person's session (`hound review`) applies accepted scope changes from that saved plan and refuses if CISO Assistant changed since.
- One summary at the end of the session: what changed in CISO Assistant, what was challenged, and the time it took.

**Tests first**
- `ReviewPrepareTests.Lists_items_without_a_recommendation_and_their_skill`
- `ReviewPrepareTests.Saves_the_plan_the_session_applies`
- `ReviewSessionTests.Refuses_a_saved_plan_older_than_the_state`

**Functional check**
- Prepare and run one session on a copy of the example repo; record the number of commands the agent ran before and after.

### s18-03 Skills for every feature, checked against the CLI
- [ ] Done

**Story:** As the agent, I want a skill for each task the person may ask for, and skills that never name a command that no longer exists.

**Acceptance criteria**
- New or updated skills in `agent/skills/`: the risk register and threats (s15-01, s15-02), security exceptions and risk acceptances
  (s15-03), BIA and recovery (s15-04, s15-05), incident reporting deadlines (s15-07), supply-chain flow-down (s15-08), governance
  metrics (s16), CMMC Level 2, SPRS and affirmations (s17).
- `weekly-review` covers the new review items, and the agent files name the skills they use.
- A test reads every `hound ...` line in `agent/` and the docs and checks the command and each flag exist in the CLI.
- `hound agent install` (and `hound init`) writes the updated skills for Claude Code and Copilot.

**Tests first**
- `AgentFilesTests.Every_hound_command_in_a_skill_exists`
- `AgentFilesTests.Every_skill_an_agent_names_exists`

**Functional check**
- Ask an agent in the GRC devcontainer to "add a risk and request an exception for it"; it follows the skills to a reviewed plan.

### s18-04 Faster runs
- [ ] Done

**Story:** As the GRC member, I want runs and plans to finish quickly, so that the agent's loop is short and a scheduled run does not
hold the instance.

**Acceptance criteria**
- `scope plan` reads only the kinds the scope declares, and each CISO Assistant list once per command.
- `hound run` runs independent collectors in parallel (bounded, configurable) and reads the current state once per audit.
- A budget test counts requests for the example repo's `scope plan` and `run` against the fake CISO Assistant and fails above the
  recorded budget; the budget is written in the test with the reason for each request.

**Tests first**
- `RequestBudgetTests.Scope_plan_stays_within_budget`
- `RequestBudgetTests.Run_reads_the_state_once_per_audit`
- `RunCommandTests.Independent_collectors_run_in_parallel`

**Functional check**
- Time `scope plan` and `run` on the example repo before and after against the local CISO Assistant and record both.

### s18-05 Health in the agent's readout
- [ ] Done

**Story:** As the agent, I want collector and integration health as JSON, so that I can tell the person what is broken without a console.

**Acceptance criteria**
- `hound status --json` (added in s16-07 with collectors, their problems, audits, standing and exceptions due) gains each collector's
  checks; CISO Assistant reachable, token expiry, recent rejected publishes; manual sources due and overdue. `hound next` uses the same data.
- No evidence content is ever returned.

**Tests first**
- `StatusCommandTests.Json_has_collectors_integration_and_manual_sources`
- `StatusCommandTests.Evidence_content_is_never_returned`

**Functional check**
- `hound status --json` on the example deployment shows the seeded failing collector.

### s18-08 The agent walks the person through pending decisions (added 2026-11-25)
- [ ] Done

Builds on s15-12 (`hound review pending --json` with risk and a summary, and `hound review --only <key>`) and s15-13 (the
`work-through-pending-decisions` skill and the `hound-decisions` agent for both assistants, and the command reference). s18-08 extends
them for what s18 adds. The maintainer's direction (2026-11-25): the
person's own agent works through every pending decision with them, one at a time.

**Story:** As the GRC member, I want my agent to take me through the waiting decisions, highest risk first, with its case and the
evidence for each, so that I approve in minutes what I understand and challenge what I do not.

**Acceptance criteria**
- The s15-13 skill `work-through-pending-decisions` reads `review pending --json`,
  opens with the grouped summary and the time it will take, then goes item by item: the requirement, the agent's case and sources,
  what it checked around the edges, and the one command for the person to run (`hound review --only <key>`).
- A challenge comes back to the agent as its note; the skill researches again and redrafts with `hound onboard decide`.
- Items the same evidence settles are presented together, but each is still accepted by its own key.
- Onboarding defaults follow the maintainer's direction on audits (s15-D8): per-system audits, a shared platform's own audit, shared
  evidence sources feeding many audits, and the organisation's audit only when an organisation-wide source feeds it.

**Tests first**
- `AgentSkillTests.Decisions_skill_orders_items_by_risk`
- `InitCommandTests.Organisation_audit_only_with_an_organisation_wide_source`

**Functional check**
- Rehearse onboarding the tutorial system with the skill; record the minutes and the commands the person ran.

### s18-06 Rehearse trial, onboarding and sustaining GRC (standing, every sprint)
- [ ] Done

As [s12-09](s12-e8-mapping-and-adoption.md#s12-09-rehearse-trial-onboarding-and-sustaining-grc-standing-every-sprint), including this
The rehearsal drives hound through the agent skills, and the sprint ends with the Claude Code and Copilot skills and agents
accurate for the CLI and its workflows (sprint lifecycle step 5 in [README.md](README.md)).
sprint's new commands and guides. Count the agent's commands and the person's minutes for one week's work, and compare with s17.

### s18-07 Improve: quality and flow (standing, about 15% of each sprint)
- [ ] Done

As [s12-10](s12-e8-mapping-and-adoption.md#s12-10-improve-quality-and-flow-standing-about-15-of-each-sprint).

## Discovered work

## Sprint review
