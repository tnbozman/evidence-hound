// s20-02: the tutorial player's pure core. No DOM, no audio, no storage: player.mjs does those and calls these.

/** The tutorial a page hash names, or null. Only a plain id, so the hash can never name a path. */
export function tutorialId(hash) {
  const id = (hash || "").replace(/^#/, "");
  return /^[a-z0-9-]+$/.test(id) ? id : null;
}

/** A new state, at the step `progress` names when that step is still in the tutorial. */
export function start(tutorial, progress) {
  const index = tutorial.steps.findIndex((s) => s.id === progress?.step);
  return { step: Math.max(0, index), answers: {} };
}

/** The state `by` steps on, kept within the tutorial. */
export function move(state, tutorial, by) {
  const step = Math.min(tutorial.steps.length - 1, Math.max(0, state.step + by));
  return { ...state, step };
}

/** What to remember between visits: the step, by id. */
export function saved(state, tutorial) {
  return { step: tutorial.steps[state.step].id };
}

/** The state with the learner's answer to a step's choice, and its feedback. */
export function choose(state, tutorial, stepId, option) {
  const step = tutorial.steps.find((s) => s.id === stepId);
  const picked = step.choice.options[option];
  return { ...state, answers: { ...state.answers, [stepId]: { option, right: Boolean(picked.right), feedback: picked.feedback } } };
}

/** Seconds to read a sentence at about 160 words a minute, and never under two. */
export function readingSeconds(text) {
  const words = text.split(/\s+/).filter(Boolean).length;
  return Math.max(2, Math.round((words / 2.7) * 10) / 10);
}

/**
 * Each sentence of a step's narration with its audio and length. A sentence uses its audio only when the narration manifest has the
 * same text for it, so a sentence changed after the audio was made is read, not mis-spoken.
 */
export function lines(step, manifest, base = "") {
  const spoken = manifest?.steps?.[step.id] ?? [];
  return step.narration.map((text, i) => {
    const audio = spoken[i];
    return audio && audio.text === text
      ? { text, src: base + audio.file, seconds: audio.seconds }
      : { text, src: null, seconds: readingSeconds(text) };
  });
}

/** Words as a shell would split them: spaces separate, quotes group. */
function words(command) {
  const found = [];
  const pattern = /"([^"]*)"|'([^']*)'|(\S+)/g;
  for (const m of command.trim().replace(/^\$\s*/, "").matchAll(pattern)) {
    found.push(m[1] ?? m[2] ?? m[3]);
  }
  return found;
}

/** A command as its words before the first option, and each option with its value, sorted. */
function parts(command) {
  const all = words(command);
  const first = all.findIndex((w) => w.startsWith("-"));
  const head = first < 0 ? all : all.slice(0, first);
  const options = [];
  for (const w of first < 0 ? [] : all.slice(first)) {
    if (w.startsWith("-")) options.push(w);
    else options[options.length - 1] += ` ${w}`;
  }
  return { head, options: options.sort() };
}

/** True when `typed` is `answer`, whatever the spacing, quoting, prompt sign or order of options. */
export function commandMatches(typed, answer) {
  const a = parts(typed);
  const b = parts(answer);
  return a.head.length > 0 && a.head.join(" ") === b.head.join(" ") && a.options.join("\n") === b.options.join("\n");
}

/** The first thing that is wrong with `typed`, as a short hint. */
export function hint(typed, answer) {
  const a = parts(typed);
  const b = parts(answer);
  if (a.head.join(" ") !== b.head.join(" ")) {
    const command = b.head.filter((w) => /^[a-z][a-z0-9-]*$/.test(w));
    return `The command starts: ${command.join(" ")}`;
  }
  const missing = b.options.filter((o) => !a.options.includes(o));
  if (missing.length > 0) return `Add ${missing[0]}`;
  const extra = a.options.filter((o) => !b.options.includes(o));
  return extra.length > 0 ? `Leave out ${extra[0].split(" ")[0]}` : "";
}
