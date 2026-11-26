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

### s19-03 Principal engineer code review
- [ ] Done

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

### s19-04 Refactor the command-line shell
- [ ] Done

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

### s19-05 Refactor the scope pipeline
- [ ] Done

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

### s19-06 Make the tests cheaper to keep
- [ ] Done

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

### s19-07 CISO review
- [ ] Done

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

## Sprint review
