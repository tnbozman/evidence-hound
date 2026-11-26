// s20-02: the tutorial player's pure core. Run with `make test-docs` (node --test).
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  start, move, commandMatches, hint, lines, choose, saved, tutorialId, readingSeconds,
} from "../../docs/tutorials/interactive/player-core.mjs";

const tutorial = {
  id: "your-week",
  steps: [
    { id: "prepare", narration: ["Your agent prepares the week.", "It reads what needs doing."] },
    {
      id: "session",
      narration: ["Now you start the session."],
      choice: { question: "?", options: [{ text: "Accept", feedback: "No." }, { text: "Challenge", right: true, feedback: "Yes." }] },
    },
    { id: "status", narration: ["Last, check the collectors."] },
  ],
};

test("a tutorial starts at its first step, and moving stays within its steps", () => {
  let state = start(tutorial);
  assert.equal(state.step, 0);
  state = move(state, tutorial, -1);
  assert.equal(state.step, 0);
  state = move(move(move(state, tutorial, 1), tutorial, 1), tutorial, 1);
  assert.equal(state.step, 2);
});

test("progress is restored by step id, and a step no longer there starts again", () => {
  assert.equal(start(tutorial, { step: "status" }).step, 2);
  assert.equal(start(tutorial, { step: "gone" }).step, 0);
  assert.deepEqual(saved(start(tutorial, { step: "session" }), tutorial), { step: "session" });
});

test("the hash names the tutorial", () => {
  assert.equal(tutorialId("#your-week"), "your-week");
  assert.equal(tutorialId(""), null);
  assert.equal(tutorialId("#../etc"), null);
});

test("a typed command matches whatever its spacing, prompt and option order", () => {
  const answer = "hound review recommend K1 --answer accept --reason \"Current export\" --source a.csv";
  assert.ok(commandMatches("hound review recommend K1 --answer accept --reason \"Current export\" --source a.csv", answer));
  assert.ok(commandMatches("$  hound review recommend K1 --source a.csv   --answer accept --reason 'Current export'", answer));
  assert.ok(!commandMatches("hound review recommend K1 --answer reject --reason \"Current export\" --source a.csv", answer));
  assert.ok(!commandMatches("hound review K1 --answer accept --reason \"Current export\" --source a.csv", answer));
  assert.ok(!commandMatches("hound review recommend K1 --answer accept", answer));
  assert.ok(commandMatches("hound review --for 60m", "hound review --for 60m"));
  assert.ok(!commandMatches("", "hound review --for 60m"));
});

test("a hint names the first thing that is wrong", () => {
  assert.equal(hint("hound reviw --for 60m", "hound review --for 60m"), "The command starts: hound review");
  assert.equal(hint("hound review", "hound review --for 60m"), "Add --for 60m");
  assert.equal(hint("hound review --for 30m", "hound review --for 60m"), "Add --for 60m");
  assert.equal(hint("hound review --for 60m --no-apply", "hound review --for 60m"), "Leave out --no-apply");
});

test("without audio, each sentence lasts as long as it takes to read", () => {
  const timed = lines(tutorial.steps[0], null);
  assert.equal(timed.length, 2);
  assert.equal(timed[0].src, null);
  assert.equal(timed[0].seconds, readingSeconds("Your agent prepares the week."));
  assert.ok(readingSeconds("One.") >= 2);
});

test("a sentence's audio is used only while its text is the same", () => {
  const manifest = {
    steps: {
      prepare: [
        { text: "Your agent prepares the week.", file: "a.mp3", seconds: 2.5 },
        { text: "It read what needed doing.", file: "b.mp3", seconds: 2.1 },
      ],
    },
  };
  const timed = lines(tutorial.steps[0], manifest, "audio/your-week/");
  assert.deepEqual(timed[0], { text: "Your agent prepares the week.", src: "audio/your-week/a.mp3", seconds: 2.5 });
  assert.equal(timed[1].src, null);
});

test("choosing an answer gives its feedback and whether it was right", () => {
  const state = choose(start(tutorial), tutorial, "session", 1);
  assert.deepEqual(state.answers.session, { option: 1, right: true, feedback: "Yes." });
  const wrong = choose(state, tutorial, "session", 0);
  assert.deepEqual(wrong.answers.session, { option: 0, right: false, feedback: "No." });
  assert.equal(state.answers.session.option, 1, "the earlier state is unchanged");
});
