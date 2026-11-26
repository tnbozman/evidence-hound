# s18 Agent speed: fewer steps, faster runs

Status: Done

Release: 2

Replanned Nov 25 2026. It was a web console for collectors. The maintainer asked for speed, not more clicks: the GRC member has 2 to 8
hours a week and works through their own coding agent, so a console would add a second place to look. This sprint makes the agent
faster and surer instead. CISO Assistant stays the system of record; `hound` stays prompt-free for agents, and a person still signs off
only in the one weekly `hound review` session.

**Goal:** the agent learns what needs doing from one read-only command, prepares the whole week in one step, uses skills for every
feature up to s17, and runs finish sooner. Measured by steps and requests, so the targets hold in CI.

### s18-01 One read-only "what now" for the agent
- [x] Done

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

Notes:
- `NextWork` (Core, pure) turns the facts into entries and orders them: overdue, broken, due soon, waiting; then by date. Each entry has
  its command, skill and minutes. The review queue is one entry (count and minutes, overdue if any item is past its deadline) plus one
  for items with no recommendation yet.
- `hound next --repo --config [--json]` gathers the review queue, the run records (a collector that never ran, failed, or missed its
  cron schedule by more than 6 hours), each manual source's newest upload (listed from 14 days before it falls due; its never-run
  collector is not listed twice), exceptions expiring within 60 days, and, with a connection, one read of the folder list (reachable,
  token accepted or rejected) and the scope plan (changes and drift not yet applied).
- CISO Assistant's API does not give a token's expiry, so `token_expiry` is null and the note says where to look; a rejected token is a
  broken entry. Offline, the note says why the connection was not checked and the scope entry is left out.
- Skill `weekly-review` and the instructions start with `hound next`; guide `docs/operate/grc-member.md`; cheat sheet.
- Functional: `NextFunctionalTests` runs it on a copy of the example repo against the local CISO Assistant.

### s18-02 Prepare the week in one step
- [x] Done

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

Notes:
- `hound review prepare --repo --config [--json]`: refreshes the queue, lists `without_recommendation` (each item's key, kind and the
  skill that drafts it, `ReviewOrder.Skill`, as the skill "weekly-review" assigns them) and the challenged items, and saves the scope
  plan to `review/prepared.plan.json` (`*.plan.json` is ignored by the repo template). Reads only; offline it says why there is no plan.
- `hound review`: when a prepared plan exists and the scope files are as it read them (no accepted item changed them), the session shows
  that plan, applies it after one confirmation and deletes it, and refuses it with `scope.plan_stale` if CISO Assistant changed since;
  when the files changed it plans again, as before. It applies a prepared plan even with nothing to accept. It ends with one summary:
  accepted, challenged, what CISO Assistant got, and the minutes taken.
- Commands the agent runs to prepare a week: before, `review pending`, then `scope plan --out` and a second `review pending` to check
  (3); after, `review prepare` twice (before and after drafting). The person runs one: `hound review`.
- Skill `weekly-review`, guides `docs/manual-evidence.md` and `docs/operate/grc-member.md`, cheat sheet. A test helper,
  `ScopeTestInstance`, holds the small scope and its CISO Assistant stubs for tests outside `ScopePlanCommandTests`.
- Functional: `NextFunctionalTests.Review_prepare_plans_the_example_scope_against_the_instance`.

### s18-03 Skills for every feature, checked against the CLI
- [x] Done

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

Notes:
- New skills: `keep-the-risk-register`, `keep-exceptions-and-acceptances`, `keep-the-bia`, `flow-down-to-suppliers` and
  `keep-the-metrics`, in the `hound-operations` agent for both assistants. Incident deadlines were already in `respond-to-an-incident`
  and CMMC Level 2 and SPRS in `keep-the-sprs-score`; `AgentSkillTests.Every_feature_has_a_skill` holds what each must say.
- `hound next` sends an expiring exception to `keep-exceptions-and-acceptances` (it was `review-before-apply`); the review queue sends a
  `contract.flow_down_missing` finding to `flow-down-to-suppliers`; `weekly-review` names both and the incident reports.
- `AgentFilesTests.Every_hound_command_in_a_skill_exists` reads every `hound ...` in `agent/`, the guides and the README (inline and in
  code blocks; sprint files, designs and recorded evidence are history and left out) and checks the command and each option against
  the CLI's own help. It found nothing wrong: the s15-13 guard had kept commands right, and options had not drifted.
  `Every_skill_an_agent_names_exists` checks the agents' skill lists, skills named in the kit, and the skills `hound next` and the
  review queue name; each skill belongs to an agent.
- Functional: no GRC devcontainer here, so the agent's part was rehearsed by following the two skills on a copy of the example repo:
  a scenario `R-09` with `treatment: accept` and exception `EXC-02` naming it, then `hound scope validate scope --json`: no problems,
  about one second. The plan needs CISO Assistant and runs in the functional tests.

### s18-04 Faster runs
- [x] Done

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

Notes:
- `scope plan` read the folder and user lists twice, once for the state and once for the access review. The reader now keeps the lists
  of its last state read for the access read that follows, so each list is read once per command; a later state read starts afresh.
  For the example repo against an empty instance: 30 requests before, 28 after, all reads.
- `RequestBudgetTests` holds the budget: every read `scope plan` makes for the example scope, by path, with the reason for each. A new
  read fails the test until it is added with its reason. A second test holds `run` to one read of each audit and one listing of its
  rows per collector, with no row-by-row lookups.
- `hound run --parallel <n>` (default 4): collectors collect at the same time, up to n; each then plans and publishes in config order.
  A collector's publish still reads the current state of its audits itself, so a later collector sees what an earlier one wrote.
  Reading the state once for the whole run was not done: it would compare later collectors with rows an earlier collector had just
  changed, and report drift that is not there.
- Functional: no local CISO Assistant in this environment, so the timings are the CI functional job's (see the sprint review). The
  request counts above are the measure that holds in CI.

### s18-05 Health in the agent's readout
- [x] Done

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

Notes:
- `HoundHealth` (CLI) is the one read both commands share: the connection (one read of the folder list: reachable, token accepted or
  rejected) and each manual source's newest upload, due date and state. `hound next` was refactored onto it.
- `hound status --json` gains each collector's latest `checks` (from `_latest/<collector>.json`: check and outcome only, never the
  observation or evidence) and `publish_failed`, `ciso_assistant` (`reachable`, `token`, `token_expiry` null, `rejected_publishes`) and
  `manual_sources`. Text output is unchanged and makes no network call.
- Guide `docs/running.md`, skill `operate-collectors`, cheat sheet.
- Functional: `NextFunctionalTests.Status_json_shows_the_failed_collector_and_the_connection`.

### s18-08 The agent walks the person through pending decisions (added 2026-11-25)
- [x] Done

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

Notes:
- `hound init` writes no organisation-wide audit by default. It names the series each system's audit joins in `scope/setup.yaml`
  (`organisation.audit_series: {ref_id: EX-E8, groups, cycle}`), so `hound onboard` still gives each system's audit its periods
  (`EX-E8-Q2-2027FY-<SYSTEM>`), and `hound.yaml` starts with `targets: []` (now valid: no audit yet). `--organisation-audit` writes the
  organisation's audit as before, for when an organisation-wide source feeds it; `--json` answers `audits` (a list) in place of `audit`.
- Approving an audit (`hound approve` on an audit draft, or a system's onboarding) adds its target to `hound.yaml` when the repo has the
  system's own mapping (`mappings/<system>-<framework>.yaml`). It never uses the framework's general mapping for a system's audit: that
  maps every collector's checks, so another system's results would land in it. Without one it says what to add.
- The trial keeps the organisation's audit (`--organisation-audit`): Microsoft 365 covers every account and laptop, an
  organisation-wide source. Its script, guide and the guides for init, onboarding, scope and first deployment say when to use it.
- `work-through-pending-decisions` starts from `hound review prepare`, says the time the session will take, takes high-risk items first
  and then by deadline, presents together what one document or export settles (each still answered by its own key), and redrafts a
  challenge with `hound onboard <system> decide`.
- Functional: `hound init` (defaults), `hound add system`, `hound audit draft` for Example 365, its mapping copied in, `hound approve`
  (two answers), `hound scope validate`: the audit and its target written, valid, about three seconds of commands. The person ran one
  command (`hound approve`). The onboarding tutorial's system path is covered by
  `OnboardSystemCommandTests.System_audit_joins_the_series_setup_names_without_an_organisation_audit`.

### s18-06 Rehearse trial, onboarding and sustaining GRC (standing, every sprint)
- [x] Done

As [s12-09](s12-e8-mapping-and-adoption.md#s12-09-rehearse-trial-onboarding-and-sustaining-grc-standing-every-sprint), including this
sprint's new commands and guides. The rehearsal drives hound through the agent skills, and the sprint ends with the Claude Code and
Copilot skills and agents accurate for the CLI and its workflows (sprint lifecycle step 5 in [README.md](README.md)). Count the
agent's commands and the person's minutes for one week's work, and compare with s17.

Notes:
- The week, following `weekly-review` on a copy of the example repo, offline: `hound next --json` (0.6 s: 7 items, 95 minutes,
  most urgent first, each with its command and skill), `hound review prepare --json` (0.6 s) and `hound review pending --json`
  (0.6 s). Three read commands. In s17 the agent gathered the same with `hound status`, `hound status --scope`,
  `hound review pending`, `hound manual list` per source and `hound scope plan --out`, and joined their output itself: five or more.
  The person's part is unchanged: one `hound review` session, its length the queue's minutes.
- Onboarding a system's audit: see s18-08's functional check (the person runs `hound approve` once; the target is added for them).
- Adding a risk and an exception for it: see s18-03's functional check.
- Agent kit: `hound agent setup --assistant both` writes 26 skills and 4 agents; `AgentFilesTests` and `AgentCommandTests` check every
  command, option and skill they name against the CLI.

### s18-07 Improve: quality and flow (standing, about 15% of each sprint)
- [x] Done

As [s12-10](s12-e8-mapping-and-adoption.md#s12-10-improve-quality-and-flow-standing-about-15-of-each-sprint).

Notes:
- Guards that keep the agent fast and right without a person checking: `RequestBudgetTests` (every read `scope plan` makes, with its
  reason), `AgentFilesTests` (every command and option the kit and guides name exists; every named skill exists and has an agent).
- Fixed: every scope warning in the review queue said it was about sensitive data, a missing flow-down clause included. Each kind now
  says why for itself (`ReviewOrder.FindingWhy`).
- Fixed: the s18-06 task text had its sentences out of order.

## Discovered work

- **s18-D1 CISO Assistant's API gives no token expiry.** `hound next` and `hound status --json` say so and where to look; a rejected
  token is listed as broken. Watch for an API that gives it.
- **s18-D2 `hound audit draft` writes an audit without the series.** `hound onboard` joins the series `setup.yaml` names; an audit drafted
  for named systems has a year and a due date instead. Offer the series there too.
- **s18-D3 A run searches each uploaded evidence item twice.** The state read finds it, then the publisher's find-or-create searches
  again. Passing the known ID to the upload would save one request per file.
- **s18-D4 Each collector's publish reads its audits' state itself.** Deliberate (s18-04): one read for the whole run would compare
  later collectors with rows an earlier one had just changed.
- **s18-D5 `ScopeTestInstance` repeats helpers of `ScopePlanCommandTests`.** Fold them together when either changes next.

## Sprint review

**2026-11-25.** The agent now learns the week from one read-only command and prepares it in one more. `hound next --json` lists the
review queue, failed or overdue collectors, manual sources due, expiring exceptions and acceptances, unapplied scope changes and the
CISO Assistant connection, most urgent first, each with its command, skill and minutes (s18-01). `hound review prepare` refreshes the
queue, names each item still without a recommendation and the skill that drafts it, and saves the scope plan the person's `hound review`
session applies; a plan older than what CISO Assistant now holds is refused (s18-02). Every feature up to s17 has a skill, and
`AgentFilesTests` fails when a skill or guide names a command, option or skill that does not exist (s18-03). `hound scope plan` reads
each list once, guarded by a request budget with a reason for every read, and `hound run --parallel` (default 4) collects independent
collectors together and still publishes in config order (s18-04). `hound status --json` gives the deployment's health (s18-05).
`hound init` writes an organisation audit only with `--organisation-audit`, for an organisation-wide source; otherwise the setup names
the audit series and each system's audit joins it, its target added when the person approves it (s18-08). Each review-queue warning
says why for its own kind (s18-07). The week's agent work fell from five or more commands joined by hand to three (s18-06).

Functional results, CI run 570: the functional job took 28 minutes 46 seconds with `NextFunctionalTests` (`hound next`, `hound status
--json` and `hound review prepare` against the local CISO Assistant) passing, and the trial ran unattended twice, the second run
skipping finished steps and planning nothing new. The unit suite has 2193 tests. s19 was replanned at the maintainer's request as a
principal engineer and CISO review, with the document generators as a droppable part B.
