# s19 Principal engineer and CISO review: release 3

Status: In progress

Release: 3

Replanned 2026-11-25 at the maintainer's request. Release 3 is the first release the maintainer treats as the product (the MVP). Before
it ships, two reviewers look at everything built in s00 to s18:

- A **principal software engineer** asks whether each folder, artefact, project and class is still needed, and whether the code is the
  right code to maintain for years. Less code for the same outcome is welcome; code a small team can understand, change and test
  safely is the aim.
- A **CISO** asks whether what hound does is what a small business needs to reach and keep Essential Eight Maturity Level 2 or
  NIST SP 800-171 (CMMC Level 2) with 2 to 8 hours a week, and whether an assessor would accept what it produces.

The agent plays both roles and writes down what it finds. Anything that removes a feature, a command, a supported file or a document a
person relies on goes to the maintainer as one batched list before it is removed. Everything else is done under the usual rules:
strict TDD, behaviour kept or deliberately changed with a test, nothing weakened to get green.

The security document generators planned for s19 are now [part B](s19b-security-documents.md), separate and droppable. Part B starts
only when part A is done, and only if the maintainer keeps it in this sprint.

**Goal:** release 3 ships with nothing in the repo that no one needs, with code organised so the next change is easy, and with a CISO's
judgement recorded on whether the product does its job.

## Part A: review

### s19-01 Inventory every folder and artefact
- [x] Done

**Story:** As a maintainer, I want one list of what is in the repo, why it is there and who uses it, so that I can see what to keep and
nothing new appears without a reason.

**Acceptance criteria**
- `docs/repo-map.md` lists every top-level folder and every folder under `src/`, `tests/`, `docs/`, `examples/` and `scripts/`, each with
  its purpose, who uses it (a person, the agent, CI, the release, a test) and a verdict: keep, merge into another, or remove.
- Candidates the planning pass already found, each to be decided with evidence:
  - The top-level `skills/` folder beside `agent/skills/` (two homes for agent guidance).
  - `scripts/ca-apply-scope.sh` and `scripts/ca-inventory.sh` beside `hound scope plan` and `hound scope apply`.
  - `examples/example-pty-ltd.yaml` and `examples/example-essential-eight.yaml` beside `examples/compliance-repo/`.
  - `docs/evidence/` (functional check records for s00 to s09 only; later sprints record them in the sprint file).
  - `docs/design/onboarding-assistant.md` and other design notes a later design superseded.
  - `src/EvidenceHound.Transport.Kafka` (is anyone running it?).
  - A compiled Python file under `tools/docs/hooks/__pycache__/` that is committed to the repo.
  - The four dev servers and `templates/`: kept, but each named with the tests and guides that need it.
- A test fails when a top-level folder, or a folder under `src/` or `tests/`, is missing from the map, so the map stays true.

**Tests first**
- `RepoMapTests.Every_folder_is_in_the_map`
- `RepoMapTests.No_build_output_is_committed`

**Functional check**
- `git ls-files` compared with the map: every path belongs to a listed folder.

Notes:
- [docs/repo-map.md](../repo-map.md) lists 74 folders. `RepoMapTests` reads `git ls-files`, so the map is checked against what is
  committed; the functional check is the test itself (2 of 2 pass; unit suite 2195 green).
- Removed now: a compiled Python file committed under `tools/docs/hooks/__pycache__/`; `__pycache__/` and `*.pyc` are now ignored.
- The planning pass was wrong about two candidates. The `ca-*.sh` scripts wrap `hound scope apply` for the dev container and probe a
  CISO Assistant release in CI, and `skills/` holds the default onboarding skills hound embeds, which a compliance repo's own
  `skills/` adds to. Both stay; s19-03 weighs moving `skills/` under `agent/`.
- Waiting on the maintainer (s19-02): the Kafka transport, `docs/evidence/` with `examples/manual-exports/`, and the superseded
  onboarding assistant design.

### s19-02 Remove what is no longer needed
- [ ] Done

**Story:** As a maintainer, I want what nobody uses gone, so that a new contributor or agent reads only what matters.

**Acceptance criteria**
- Every item marked "remove" or "merge" in s19-01 is gone or merged, with the guides, the cheat sheet, the agent kit and CI updated.
- Before removing anything a person may rely on (a command, an option, a file format, a guide, an example), the list goes to the
  maintainer once, in one batch. A removed command or option is listed in the changelog with what replaces it.
- Dead code: public types and members with no caller outside their own tests are removed, or kept with a recorded reason. The
  analysers' unused-code rules are turned on where they are off, without suppressions.

**Tests first**
- `RepoMapTests.Every_folder_is_in_the_map` (still green after the removals)
- A test for each replacement behaviour, where a removed script's job moves into `hound`.

**Functional check**
- `make test`, `make lint`, `make docs` and the trial script pass after the removals.

Notes (in progress):
- The maintainer approved all three removals on 2026-11-26. Removed: the Kafka transport (project, package, compose profiles, `make
  ca-up-kafka`, guides); a config with `transport: kafka` or `kafka_bootstrap_servers`, or `HOUND_TRANSPORT=kafka`, now stops with
  `config.transport_removed` and says to publish over REST (`ConfigTests.Bad_transport_is_rejected`,
  `PublishCommandTests.Kafka_transport_is_removed_and_says_so`). `docs/evidence/` (s00 to s09), `examples/manual-exports/` and the
  superseded onboarding assistant design: removed, with the 51 links to them pointing at the last commit that held them
  (`ExampleRepoTests` caught the one the first pass missed).
- Dead code: IDE0051, IDE0052 and IDE0060 are now build errors. They found two unused parameters in the core and one unused test
  helper, which are removed, and three M365 checks that take the options they do not need because every check in the table has one
  signature; those parameters are named `_`. A scan for public and internal types with no reference outside their own declaration
  found none.
- Still to send the maintainer, with the CISO review's removals: the hidden retired model options (engineering review E8).

### s19-03 Principal engineer code review
- [x] Done

**Story:** As the next developer, I want the code reviewed as a whole by someone who will maintain it, so that the refactoring in this
sprint goes where it pays most.

**Acceptance criteria**
- `docs/reviews/2026-engineering-review.md` reviews the code against [engineering.md](../engineering.md): dependencies point inwards,
  the core is pure, side effects sit at the edges, and each class has one reason to change.
- It covers at least: the largest files (today `ScopeStateReader`, `ScopeCommands`, `OnboardSystemCommand`, `ScopeLoader`,
  `ReviewCommand`, `ApproveCommand`, `ProfileParser`), how CLI commands load config, read options and write JSON, duplicated
  logic across commands and projects, how errors are reported, and the test suite's shape and speed.
- Each finding has a cost (what it makes hard), a fix, an estimate, and a priority. The top findings become s19-04 to s19-06; the
  rest go under "Discovered work".
- Measures recorded before and after the sprint: lines of production and test code per project, the largest files, the unit suite's
  duration, and the analysers' warning count (zero).

**Tests first**
- None: this task writes a review. Its findings are fixed under TDD in s19-04 to s19-06.

**Functional check**
- The review's measures are reproduced by a script in `scripts/` and recorded in the sprint file.

Notes:
- [docs/reviews/2026-engineering-review.md](../reviews/2026-engineering-review.md): dependencies point inwards and the core is pure;
  ten findings. P1, fixed in this sprint: E1 commands borrow shared helpers from each other (s19-04), E2 common options declared in
  every command (s19-04), E3 large command files (s19-04), E4 `ScopeStateReader` and `ScopeLoader` read every kind in one class
  (s19-05), E5 repeated test setup (s19-06, with E6, the 11 `HoundCli.RunAsync` overloads). The rest are under "Discovered work".
- `scripts/code-measures.sh` (tested by `scripts/tests/code-measures.bats`), at the start of s19: 47,051 production lines, 43,367 test
  lines; largest files `ScopeStateReader.cs` 886, `ScopeCommands.cs` 851, `OnboardSystemCommand.cs` 767, `ScopeLoader.cs` 699. Unit
  suite 2,195 tests in 29 s; no analyser warnings.

### s19-04 Refactor the command-line shell
- [x] Done

**Story:** As a developer adding a command, I want every command built the same way, so that a new one takes a page of code and
behaves like the rest.

**Acceptance criteria**
- One way to load `hound.yaml` and the scope, resolve `--repo` and `--config`, connect to CISO Assistant, write `--json` output and
  report errors with their codes, used by every command. Each command keeps only what is its own.
- The output of every command is unchanged, checked by the existing command tests and by golden files for the `--json` output of
  `hound next`, `hound status`, `hound review pending` and `hound scope plan`.
- The largest command files the review names are split along their responsibilities.

**Tests first**
- Golden-file tests for the `--json` output above, written before the change.

**Functional check**
- The functional suite passes unchanged.

Notes:
- Golden files first (`JsonGoldenTests`): the `--json` answers of `hound next`, `hound status`, `hound review pending` and `hound scope
  plan` are pinned, byte for byte, with temporary paths and ports normalised. They stayed green through every refactor below.
- Shared helpers now live under their own names (engineering review E1): `CommandJson` (the two JSON settings, replacing
  `OnboardAgentCommand.Indented` and nine private copies), `CisoAssistantConnection` (from `PublishCommand`), `DeploymentFile` (from
  `ConfigCommands`) and `RepoScope` (scope loading and checking, from `ScopeCommands`, 15 callers).
- `CommonOptions.Repo()` and `CommonOptions.Json()` replace 14 and 17 declarations (E2); `--repo` now reads "The compliance repo to work
  in", as the guides say. `--config` keeps its per-command descriptions, which differ in meaning.
- Large files split by responsibility (E3): `ScopeCommands.cs` 851 to 392 lines (`RepoScope.cs`, `ScopeCommands.SavedPlans.cs`),
  `OnboardSystemCommand.cs` 767 to 553 (`OnboardSystemCommand.Files.cs`), `ReviewCommand.cs` 551 to 334 (`ReviewSession.cs`).
- Functional: CI's functional job on this branch (see the sprint review). Unit suite 2,200 green, lint clean.
- Found on the way: the agent answers use relaxed JSON escaping and older reports the default one (s19-D5).

### s19-05 Refactor the scope pipeline
- [x] Done

**Story:** As a developer changing what hound keeps in CISO Assistant, I want loading, desired state, reading and planning to be
separate, small and pure where they can be, so that a new kind of object touches each in one obvious place.

**Acceptance criteria**
- Adding a kind of scope object needs a change in one place per stage (load, desired, read, plan), shown by a short guide in
  `docs/engineering.md`.
- `ScopeStateReader` and `ScopeLoader` are split by kind of object; the request budget (`RequestBudgetTests`) holds or shrinks.
- s18-D3 (evidence searched twice per upload) and s18-D5 (duplicated scope test helpers) are fixed here.

**Tests first**
- `RequestBudgetTests` unchanged and green; a test that the upload uses the evidence ID the state read found (s18-D3).

**Functional check**
- `hound scope plan` and `hound run` against the example scope on the local CISO Assistant give the same plan and results as before.

Notes:
- One file per kind and stage: `ScopeStateReader.cs` 886 lines to 491, with risks, BIA, metrics, third parties and frameworks in
  `ScopeStateReader.<Kind>.cs`; `ScopeLoader.cs` 699 to 445, with access, settings and audits joining the kinds that already had their own
  file. [engineering.md](../engineering.md#adding-a-kind-of-scope-object) says where each stage of a new kind goes.
- `RequestBudgetTests` unchanged and green: no read added.
- s18-D3 fixed: changed evidence carries the ID the state read found (`PlanTests.Changed_evidence_carries_the_id_the_state_read_found`),
  and its upload sends the revision without searching again (`PublisherTests.Upload_with_a_known_id_sends_a_revision_without_searching`):
  one search per evidence item in a run instead of two.
- s18-D5 (scope test helpers) moves to s19-06, which folds test setup together.
- Functional: the CI functional job, which plans, applies and runs against a real CISO Assistant (see the sprint review).

### s19-06 Make the tests cheaper to keep
- [x] Done

**Story:** As a developer, I want tests that say what behaviour they protect and share their setup, so that a refactor changes code,
not a hundred tests.

**Acceptance criteria**
- Shared builders for config, scope and a fake CISO Assistant replace copies across test projects.
- Tests that check implementation details instead of behaviour are rewritten against behaviour. No test is deleted without a test
  that covers the same behaviour.
- The unit suite's duration does not grow; the slowest ten tests are listed with why they are slow.

**Tests first**
- The suite itself: every test still passes, and coverage of the core does not fall.

**Functional check**
- `make test` duration before and after, recorded.

Notes:
- One driver for command tests, `tests/EvidenceHound.Cli.Tests/Hound.cs` (`Hound.RunAsync` returns exit, output and error;
  `Hound.At` gives a home folder and a fixed day). Five private copies of the same wrapper now call it.
- `ScopeTestInstance` owns the scope files and the CISO Assistant stub (`WriteScopeAsync`, `HeldStructure`, `List`, `Object`,
  `Accept`), and `ScopePlanCommandTests` uses them on its own stub instead of keeping copies (closes s18-D5). 134 lines out, 57 in.
- Implementation-detail check: no test verifies mock calls or reads private members; the one reflection test
  (`FrameworkNeutralityTests`) enforces an architecture rule, which is behaviour this repo wants. No test deleted.
- E6 is only partly done: `HoundCli` keeps its overloads because 46 test files call the ones that add logging or services. Recorded as
  s19-D6.
- Slowest ten: the Sophos and IaC dev server tests (7.4 s, 6.8 s, 3.1 s, 2.6 s, 1.8 s) start a real Kestrel server per deviation;
  `ServeCommandTests` waits 2 s for a scheduler tick by design; `RestCollectorTests` saves every query of the example profile (1.9 s);
  `AgentFilesTests.Every_hound_command_in_a_skill_exists` parses every skill against the command tree (1.7 s); `RunManifestTests` hashes
  profiles (1.6 s); `ReviewPrepareTests.Saves_the_plan_the_session_applies` runs a whole review session (1.5 s). Each runs in parallel
  with the other projects, so none lengthens the suite.
- `make test`: 2,203 tests in 28.6 s after, 29 s before.

### s19-07 CISO review
- [x] Done

**Story:** As a small business's cyber GRC lead with a few hours a week, I want a CISO to judge whether hound gets me to Essential Eight
Maturity Level 2 or CMMC Level 2 and keeps me there, so that I know what it does not do before an assessor tells me.

**Acceptance criteria**
- `docs/reviews/2026-ciso-review.md` walks a fictional small business (Example Pty Ltd) through a year: onboarding, the weekly review,
  a quarterly board report, an incident, a supplier change, an annual affirmation and an assessment. For each step it records what
  hound does, what the person must do, the time it takes, and what an assessor would ask for that hound does not give.
- It checks the mappings' claims: each Essential Eight ML2 control and each NIST SP 800-171 requirement marked as automated really is
  answered by a check, and each `covers: part` names what the person adds.
- It lists features that a small business does not need, and features it needs that are missing, ranked. Removals go to the
  maintainer in the s19-02 batch. The verdict on part B's document generators (keep, cut or reorder) is recorded in
  [s19b-security-documents.md](s19b-security-documents.md).
- Every gap that would fail an assessment is fixed in this sprint or recorded as a known limitation in the release notes.

**Tests first**
- A test for each mapping or behaviour the review corrects.

**Functional check**
- The year's walk-through followed on the example repo against the local CISO Assistant, with the time each step took.

Notes:
- [docs/reviews/2026-ciso-review.md](../reviews/2026-ciso-review.md): fit for release 3 for a Microsoft 365, Intune, Sophos Central and
  scanner estate; the example year takes the GRC member a little over 2 hours a week.
- Mapping check: 60 Essential Eight rows answered by collectors and the 32 CMMC `covers: part` rows read against each check's code.
  Fixed under TDD: critical findings now use the 48-hour window (ISM-1876, ISM-1877); the Office ASR rules pass only for all users or
  all devices (ISM-1667 to ISM-1673); ISM-1657 includes the yearly ruleset review for the three file types no collector reads; a
  `covers: part` row's `rest:` says what the person assesses and the observation names it, on all 32 CMMC rows. Also fixed: the cheat
  sheet listed options `hound irp build` does not take (now a test checks every option the sheet names), and `docs/third-parties.md`
  said suppliers could not record CUI.
- Known limitations for the release notes (s19-09): practices, not the 320 NIST SP 800-171A objectives; configuration evidence, not
  control tests; collectors see only Microsoft 365, Intune Windows, Sophos Central and the scanner; ISM-1883 is answered by counts plus
  the yearly privileged access review; `hound` does not post to SPRS.
- Part B verdict recorded in [s19b-security-documents.md](s19b-security-documents.md): move after release 3, reordered, four tasks cut.
  Sent to the maintainer with the second removal list (retired model options).
- Functional check: the walk-through ran offline on a copy of the example repo with `HOUND_TODAY=2027-02-15`; every command answered in
  under a second (times in the review). The local CISO Assistant could not start here: the environment's network policy blocks its
  image registry. The steps that read CISO Assistant are covered by the functional suite in CI, recorded in the sprint review.

### s19-08 One path through the docs
- [ ] Done

**Story:** As a new GRC member, I want one route from "what is this" to my first weekly review, so that I am not choosing between
guides that overlap.

**Acceptance criteria**
- Every guide in `docs/` is either on that route, a reference page it links to, or removed. Overlapping guides are merged.
- The cheat sheet, README and `mkdocs.yml` navigation follow the route.
- `AgentFilesTests` and the link checker stay green.

**Tests first**
- `AgentFilesTests` and `make lint-docs` (existing): every command and link the docs name exists.

**Functional check**
- A rehearsal that follows only the route, from install to the first `hound review`.

### s19-09 Release 3
- [ ] Done

**Story:** As a small business adopting hound, I want release 3 to say what it does, what it does not, and how to upgrade, so that I can
trust it.

**Acceptance criteria**
- Release notes with what changed since release 2, the removals and their replacements, the known limitations from s19-07, and the
  upgrade steps for an existing compliance repo.
- The changelog, version stamp and container image are ready. The tag waits for the maintainer.

**Tests first**
- Existing release and package tests (`make test-packages`, `make test-image`).

**Functional check**
- `scripts/release-build.sh` builds release 3's artefacts on the branch.

### s19-10 Rehearse trial, onboarding and sustaining GRC (standing, every sprint)
- [ ] Done

As [s12-09](s12-e8-mapping-and-adoption.md#s12-09-rehearse-trial-onboarding-and-sustaining-grc-standing-every-sprint), on the code after
the removals and refactoring. The rehearsal drives hound through the agent skills, and the sprint ends with the Claude Code and Copilot
skills and agents accurate for the CLI and its workflows (sprint lifecycle step 5 in [README.md](README.md)).

### s19-11 Improve: quality and flow (standing)
- [ ] Done

As [s12-10](s12-e8-mapping-and-adoption.md#s12-10-improve-quality-and-flow-standing-about-15-of-each-sprint). This sprint the whole of
part A is quality work, so this task only collects what the other tasks did not.

## Part B: security document generators (droppable)

See [s19b-security-documents.md](s19b-security-documents.md). Starts after part A, if the maintainer keeps it in s19.

## Discovered work

- **s19-D1 `Runtime` references every built-in collector** (engineering review E7). Register them in the CLI's composition root.
- **s19-D2 Retired model options are still parsed** (E8). Removing them goes to the maintainer with the s19-02 list.
- **s19-D3 Two homes for agent guidance** (E9): move `skills/` under `agent/` in a release after 3; a compliance repo's own `skills/`
  folder depends on the name.
- **s19-D5 Two JSON escaping settings.** Newer agent answers leave `'`, `<` and `+` as they are; older reports and kept files escape
  them. One setting for all would change the bytes of older outputs, so it waits for a release that can say so.
- **s19-D6 `HoundCli.RunAsync` still has 11 overloads** (E6, the rest). Tests now share `Hound.RunAsync`, but 46 test files call the
  overloads that add logging or test services. Replace them with one options record when those tests next change.
- **s19-D4 Two older deployment configs beside the example compliance repo** (E10): fold them in once the shared test repo exists.

## Sprint review
