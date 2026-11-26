# s20 Narrated interactive tutorials

Status: Done

Release: 4

Asked for by the maintainer on 2026-11-26, after release 3. A small-business GRC person with 2 to 8 hours a week learns hound fastest
by watching and doing, not by reading 300 lines of guide. This sprint adds short interactive tutorials to the docs site, one for each
step of the [getting started](../getting-started.md) route, each with a spoken narration. The narration is generated on a machine of
our own with [Kokoro](https://github.com/hexgrad/kokoro), an open-weight text-to-speech model (Apache-2.0), run through
[kokoro-onnx](https://github.com/thewh1teagle/kokoro-onnx). No cloud speech service is used and no text leaves the machine.

The tutorials teach the one path s19 settled on; the written guides stay the reference. Each tutorial is a JSON file of steps. A
step has a narration, what the terminal shows, and, where it helps, something to do: type the command, or choose what to do with an
item in the weekly review. One static player page shows them, with captions, so a tutorial works with the sound off and with no audio
at all.

**Goal:** a new GRC person can watch and try trial, onboarding, deployment and the weekly review in under half an hour, hear it read
by a local model, and every `hound` command a tutorial teaches is checked against the CLI in CI, so the tutorials cannot drift.

Part B of s19 ([security document generators](s19b-security-documents.md)) is unchanged and still follows release 3.

### s20-01 A tutorial format the CI can check
- [x] Done

**Story:** As a maintainer, I want each tutorial to be data with one shape, so that a test catches a broken step or a command hound no
longer has before a learner does.

**Acceptance criteria**
- A tutorial is `docs/tutorials/interactive/<id>.json`: its title, summary, minutes, the guide it follows, the voice, and its steps.
  A step has an id, a title, a narration (a list of sentences), and optionally terminal lines (who types it, the command, its output),
  a panel (what the learner would see elsewhere, such as CISO Assistant), a "try it" (type a command) and a choice (pick an answer,
  with feedback for each).
- `scripts/tutorials.py check` reports, with the file and step: a missing field, a duplicate step id, an empty narration, a guide that
  does not exist, a choice with no right answer, a "try it" whose answer is not one of the step's commands, and an organisation name
  that is not the fictional Example Pty Ltd. It exits 1 on any finding.
- Every tutorial is listed on `docs/tutorials/interactive.md`, and every listed tutorial exists.
- Every `hound ...` command a tutorial shows or asks for exists, and every option it uses exists on that command (the same check as the
  cheat sheet and the agent kit).

**Tests first**
- `tests/docs/test_tutorials.py`: `test_a_good_tutorial_has_no_findings`, `test_each_broken_rule_is_reported_with_file_and_step`,
  `test_every_tutorial_is_listed_and_every_listed_one_exists`
- `AgentFilesTests.Every_hound_command_in_a_tutorial_exists` (each command, and each option on its command)

**Functional check**
- `python3 -I scripts/tutorials.py check` on the repo: no findings.

### s20-02 The player
- [x] Done

**Story:** As a GRC person new to hound, I want to watch a tutorial step by step, hear it, read along and try each command, so that I
know what to do before I do it for real.

**Acceptance criteria**
- `docs/tutorials/interactive/play.html#<id>` plays a tutorial. Plain HTML, CSS and JavaScript in the repo; no framework, no CDN, no
  cookies, no tracking.
- Each step shows its title, its captions (the sentence being read is highlighted), the terminal, and its panel. Play, pause, back,
  next, replay, and a speed of 1 or 1.25. Keyboard: space plays or pauses, left and right arrows move between steps.
- "Try it" checks what the learner typed against the command, ignoring spacing and option order, gives a hint after a wrong try, and
  shows the output once it is right or the learner asks to see it. A choice shows the feedback for the answer picked.
- Without audio (none generated, or the file missing) the captions advance at reading speed and the tutorial works the same.
- Progress is remembered in the browser only, and a learner can start again. Works on a phone, in light and dark, and with reduced
  motion.
- "Read it instead" shows the whole tutorial as text on one page.

**Tests first**
- `tests/docs/player.test.mjs` (Node's test runner) for the pure core in `player-core.mjs`: moving between steps, matching a typed
  command, timing captions without audio, choosing an answer, restoring progress that names a step no longer there.

**Functional check**
- `make docs`, then Playwright opens each tutorial, plays it to the end with the sound off, types each "try it" and picks each
  choice, with no console errors.

**Result:** `node --test tests/docs/player.test.mjs`: 8 pass. Playwright played all four tutorials at desktop and phone width: no console errors, no horizontal scroll; captions, try-it, choices and saved progress work with and without audio.

### s20-03 Narration with Kokoro, on our own machine
- [x] Done

**Story:** As a maintainer, I want the narration spoken by a local open-weight model, so that the tutorials have a voice without a cloud
service, a subscription or text leaving the machine.

**Acceptance criteria**
- `make tutorials-audio` writes one MP3 per sentence to `docs/tutorials/interactive/audio/<id>/` with a `narration.json` that names
  each sentence's file, length and the hash of its text. Only new or changed sentences are spoken again; files no sentence uses are
  removed.
- The speech model and voices are downloaded once into a cache outside the repo and checked against pinned SHA-256 sums; a mismatch
  stops the run. The Python packages are pinned with hashes, as the docs tools are.
- A short pronunciation list turns written terms into spoken ones (CISO, GRC, ISM, YAML, ML2), applied to speech only: the captions
  keep the written words.
- Audio is not committed. The release workflow speaks it before it builds the docs site; CI builds the site without it.
- The player uses a sentence's audio only when its text hash matches, so a sentence changed after the audio was made is shown, not
  mis-spoken.

**Tests first**
- `tests/docs/test_tutorials.py`: `test_spoken_text_uses_the_pronunciation_list`, `test_only_changed_sentences_are_spoken_again`,
  `test_unused_audio_is_removed`, `test_a_model_with_the_wrong_hash_is_refused` (the speaker is a fake; no model in unit tests)

**Functional check**
- `make tutorials-audio` from a clean cache: the model is fetched and verified; every sentence has audio; a second run speaks nothing.

**Result:** `make tutorials-audio`: 124 sentences, about 3.8 MB of MP3, 2 to 3 minutes of narration per tutorial. Model files are fetched from the kokoro-onnx GitHub release and checked against pinned SHA-256 sums. Audio is not committed; the release workflow speaks it.

### s20-04 Four tutorials on the one path
- [x] Done

**Story:** As a GRC person with a few hours a week, I want a short tutorial for each step of getting started, so that I can learn the
whole routine in one sitting.

**Acceptance criteria**
- Four tutorials, each 5 to 8 minutes, following [getting started](../getting-started.md):
  1. `local-trial`: try it locally with `make trial`.
  2. `compliance-repo`: make your compliance repo with `hound init`, then onboard a supplier and a system with your agent and approve.
  3. `first-deployment`: deploy the stack, apply the scope, turn on the schedule.
  4. `your-week`: the weekly review: the agent prepares, you accept or challenge, the run publishes; then the month's report.
- The scene is the fictional Example Pty Ltd of the local trial. Every command, option, file and message matches the guide it
  follows and hound as it is; outputs are shortened only by leaving lines out.
- Each tutorial ends with what to do next and a link to the written guide.

**Tests first**
- The s20-01 checks over the four tutorials (`AgentFilesTests`, `test_tutorials.py`).

**Functional check**
- The s20-02 Playwright check on all four.

**Result:** `python3 -I scripts/tutorials.py check`: 4 tutorial(s), no findings. `AgentFilesTests.Every_hound_command_in_a_tutorial_exists` passes in CI.

### s20-05 Publish with the docs
- [x] Done

**Story:** As a GRC person reading the docs, I want the tutorials where I start, so that I find them without looking.

**Acceptance criteria**
- `docs/tutorials/interactive.md` lists the tutorials with their length and what each covers, in the site's navigation under "Start
  here". Each step of getting started, and each guide a tutorial follows, links to its tutorial.
- `make docs` copies the player and tutorials into the site; the release workflow runs `make tutorials-audio` first.
- CI's docs job runs the tutorial checks and the player's tests.
- The docs say how the audio is made, that Kokoro runs locally, and the licences (Kokoro-82M Apache-2.0, kokoro-onnx MIT; espeak-ng,
  which turns text into sounds, is GPL-3.0 and is used only as a build tool).

**Tests first**
- `test_every_tutorial_is_listed_and_every_listed_one_exists` (s20-01); the link checker covers the new links.

**Functional check**
- `make docs` in strict mode; the site's `tutorials/interactive/` plays each tutorial.

**Result:** `make docs` (strict) and `make lint-docs` pass; the hub page is under Start here and getting started links each step's tutorial. The release workflow speaks the narration before publishing the docs, and carries on without audio if Kokoro fails.

### s20-06 Agent kit (standing, every sprint)
- [x] Done

**Story:** As a GRC person, I want my agent to know the tutorials exist and to keep them right when it changes a command, so that I am
pointed to them when I am new and they never teach an old command.

**Acceptance criteria**
- The onboarding skills point a new person to the matching tutorial. The contributor guide says that changing a command a tutorial uses
  means changing the tutorial, which `AgentFilesTests` enforces. `AgentCommandTests` and `AgentFilesTests` stay green for Claude Code and
  Copilot.

**Tests first**
- `AgentSkillTests` for the tutorial link in the onboarding skill.

**Functional check**
- `hound agent setup --assistant both` in a new repo: the skill names the tutorial.

**Result:** `AgentSkillTests.A_new_person_is_pointed_to_the_tutorial_for_their_step` passes in CI: CLAUDE.md, copilot-instructions.md and the onboard-the-organisation and weekly-review skills name their tutorial.

### s20-07 Rehearse trial, onboarding and sustaining GRC (standing, every sprint)
- [x] Done

As [s12-09](s12-e8-mapping-and-adoption.md#s12-09-rehearse-trial-onboarding-and-sustaining-grc-standing-every-sprint). This sprint the
rehearsal follows each tutorial as a learner would: watch it, then do the same on the trial stack and the example repo, and record
anything the tutorial says that hound does not do.

**Result:** each tutorial's commands and output were checked against the CI trial job's real hound output and hound's own messages (init prompts, supplier draft, approve, onboard pending and decide, review prepare and recommend, status). Several invented outputs were replaced; outputs that nothing in CI shows (scope apply, runner token, committee report) are left blank rather than guessed.

### s20-08 Improve: quality and flow (standing, about 15% of each sprint)
- [x] Done

As [s12-10](s12-e8-mapping-and-adoption.md#s12-10-improve-quality-and-flow-standing-about-15-of-each-sprint). Candidates: the release
workflow tolerates a release that already exists for the tag (release 3 needed a manual one deleted first), and s19-D7 (the generated
changelog lists features removed before release).

**Result:** the release workflow edits and re-uploads to a release that already exists (`--clobber`), and creates it with `--verify-tag` otherwise; docs/releasing.md says so. The link checker and markdownlint skip the docs and tutorials virtual environments. s19-D7 was not done and moves to Discovered work.

## Discovered work

- s20-D1: s19-D7 (the generated changelog lists features removed before release) is still open.
- s20-D2: the deployment tutorial's output for `runner-token.sh` and the first `scope apply` is not exercised by any CI job; a
  deployment rehearsal on a fresh VM would let it show real output.
- s20-D3: the voice (`af_heart`, American English) is a placeholder; an Australian or British English Kokoro voice may suit the
  audience better. The voice is one field per tutorial, and changing it re-speaks everything.

## Sprint review

Four narrated tutorials (local trial, compliance repo, deployment, weekly review) are on the docs site, played by one static page with
captions, so they work with the sound off. The narration is spoken by Kokoro on our own machine and by the release workflow, never by
a cloud service. CI checks every tutorial's structure, links and fictional names, and every `hound` command and option it teaches, so
a tutorial fails the build when the CLI changes under it. The agent kit points a new person to the tutorial for their step.
