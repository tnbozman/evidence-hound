// s20-02: the tutorial player's shell: the page, audio, timers and browser storage. The decisions are in player-core.mjs.
import { start, move, commandMatches, hint, lines, choose, saved, tutorialId } from "./player-core.mjs";

const $ = (id) => document.getElementById(id);
const GAP_MS = 250;
const store = {
  get(key) { try { return JSON.parse(localStorage.getItem(key)); } catch { return null; } },
  set(key, value) { try { localStorage.setItem(key, JSON.stringify(value)); } catch { /* private window: progress is not kept */ } },
};

let tutorial = null;
let manifest = null;
let state = null;
let timed = [];
let line = 0;
let playing = false;
let timer = null;
let audio = null;
let speed = 1;
let sound = store.get("hound-tutorials-sound") ?? true;
let solved = {};

function el(tag, text, cls) {
  const node = document.createElement(tag);
  if (text !== undefined) node.textContent = text;
  if (cls) node.className = cls;
  return node;
}

function guideUrl(guide) {
  // A docs page's address on the site, from this page's folder (tutorials/interactive/): "operate/grc-member.md" is "operate/grc-member/".
  const page = guide.replace(/(^|\/)(index|README)\.md$/, "$1").replace(/\.md$/, "/");
  return `../../${page}`;
}

async function load() {
  const id = tutorialId(location.hash);
  if (!id) return fail("No tutorial named. Pick one from the list of tutorials.");
  try {
    const response = await fetch(`${id}.json`);
    if (!response.ok) return fail(`There is no tutorial called "${id}".`);
    tutorial = await response.json();
  } catch {
    return fail("This tutorial could not be loaded. Open it from the docs site, or run make docs-serve.");
  }
  try {
    const response = await fetch(`audio/${id}/narration.json`);
    manifest = response.ok ? await response.json() : null;
  } catch {
    manifest = null;
  }
  const progress = store.get(`hound-tutorial-${id}`);
  state = start(tutorial, progress);
  solved = {};
  document.title = `${tutorial.title}: Evidence Hound tutorial`;
  $("title").textContent = tutorial.title;
  $("summary").textContent = tutorial.summary;
  $("minutes").textContent = `About ${tutorial.minutes} minutes, ${tutorial.steps.length} steps`;
  $("resume").hidden = state.step === 0;
  $("resume").textContent = `You stopped at step ${state.step + 1}. Start carries on from there.`;
  renderSteps();
  renderTranscript();
  $("intro").hidden = false;
  $("stage").hidden = true;
  $("begin").focus();
}

function fail(message) {
  $("problem").textContent = message;
  $("problem").hidden = false;
  $("intro").hidden = true;
  $("stage").hidden = true;
}

function renderSteps() {
  const nav = $("steps");
  nav.replaceChildren();
  tutorial.steps.forEach((step, i) => {
    const button = el("button", String(i + 1));
    button.type = "button";
    button.title = step.title;
    button.setAttribute("aria-label", `Step ${i + 1}: ${step.title}`);
    button.addEventListener("click", () => go(i - state.step));
    nav.append(button);
  });
}

function renderTranscript() {
  const article = $("transcript");
  article.replaceChildren(el("h2", tutorial.title), el("p", tutorial.summary));
  tutorial.steps.forEach((step, i) => {
    article.append(el("h3", `${i + 1}. ${step.title}`));
    step.narration.forEach((text) => article.append(el("p", text)));
    for (const t of step.terminal ?? []) {
      const pre = el("pre");
      pre.append(el("span", `${t.who === "agent" ? "agent" : "you"} $ `, "who"), el("strong", t.command));
      if (t.output) pre.append(`\n${t.output}`);
      article.append(pre);
    }
    if (step.panel) {
      article.append(el("p", step.panel.title, "panel-title"));
      const list = el("ul");
      step.panel.lines.forEach((l) => list.append(el("li", l)));
      article.append(list);
    }
    if (step.choice) {
      article.append(el("p", step.choice.question, "panel-title"));
      const list = el("ul");
      step.choice.options.forEach((o) => list.append(el("li", `${o.text}${o.right ? " (the right answer)" : ""}: ${o.feedback}`)));
      article.append(list);
    }
  });
  const guide = el("a", "Read the written guide");
  guide.href = guideUrl(tutorial.guide);
  article.append(el("p")).append(guide);
}

function render() {
  const step = tutorial.steps[state.step];
  timed = lines(step, sound ? manifest : null, `audio/${tutorial.id}/`);
  line = 0;
  $("count").textContent = `Step ${state.step + 1} of ${tutorial.steps.length}`;
  $("step-title").textContent = step.title;
  [...$("steps").children].forEach((b, i) => {
    b.classList.toggle("done", i < state.step);
    if (i === state.step) b.setAttribute("aria-current", "step");
    else b.removeAttribute("aria-current");
  });

  const captions = $("captions");
  captions.replaceChildren(...step.narration.map((text) => el("span", `${text} `)));

  renderTerminal(step);
  renderPanel(step);
  renderTry(step);
  renderChoice(step);
  renderEnd();
  $("back").disabled = state.step === 0;
  $("next").disabled = state.step === tutorial.steps.length - 1;
  store.set(`hound-tutorial-${tutorial.id}`, saved(state, tutorial));
}

function renderTerminal(step) {
  const terminal = $("terminal");
  terminal.replaceChildren();
  const entries = step.terminal ?? [];
  terminal.hidden = entries.length === 0;
  for (const t of entries) {
    const hidden = step.try && step.try.answer === t.command && !solved[step.id];
    const block = el("div", undefined, "entry");
    const prompt = el("div", undefined, "prompt");
    prompt.append(el("span", t.who === "agent" ? "agent $" : "you $", `who ${t.who}`));
    prompt.append(el("code", hidden ? "…your turn" : t.command));
    if (!hidden && t.who === "you") {
      const copy = el("button", "Copy", "copy");
      copy.type = "button";
      copy.setAttribute("aria-label", `Copy ${t.command}`);
      copy.addEventListener("click", () => navigator.clipboard?.writeText(t.command).then(() => { copy.textContent = "Copied"; }, () => {}));
      prompt.append(copy);
    }
    block.append(prompt);
    if (t.output && !hidden) block.append(el("pre", t.output));
    terminal.append(block);
  }
}

function renderPanel(step) {
  $("panel").hidden = !step.panel;
  if (!step.panel) return;
  $("panel-title").textContent = step.panel.title;
  $("panel-lines").replaceChildren(...step.panel.lines.map((l) => el("li", l)));
}

function renderTry(step) {
  $("try").hidden = !step.try;
  if (!step.try) return;
  $("try-prompt").textContent = step.try.prompt;
  $("typed").value = solved[step.id] ? step.try.answer : "";
  $("typed").disabled = Boolean(solved[step.id]);
  $("try-hint").textContent = solved[step.id] ? "Right." : "";
  $("show").hidden = Boolean(solved[step.id]);
}

function renderChoice(step) {
  $("choice").hidden = !step.choice;
  if (!step.choice) return;
  const answer = state.answers[step.id];
  $("choice-question").textContent = step.choice.question;
  $("choice-options").replaceChildren(...step.choice.options.map((o, i) => {
    const button = el("button", o.text);
    button.type = "button";
    button.setAttribute("aria-pressed", String(answer?.option === i));
    if (answer?.option === i) button.classList.add(answer.right ? "right" : "wrong");
    button.addEventListener("click", () => {
      state = choose(state, tutorial, step.id, i);
      renderChoice(step);
    });
    return button;
  }));
  $("choice-feedback").textContent = answer ? `${answer.right ? "Yes. " : "Not quite. "}${answer.feedback}` : "";
}

function renderEnd() {
  const last = state.step === tutorial.steps.length - 1;
  $("end").hidden = !last;
  if (!last) return;
  const links = (tutorial.next ?? []).map((n) => {
    const a = el("a", n.title);
    a.href = n.tutorial ? `play.html#${n.tutorial}` : guideUrl(n.guide);
    const item = el("li");
    item.append(a);
    return item;
  });
  const guide = el("a", "The written guide for this tutorial");
  guide.href = guideUrl(tutorial.guide);
  const item = el("li");
  item.append(guide);
  $("end-links").replaceChildren(...links, item);
}

function highlight() {
  [...$("captions").children].forEach((span, i) => span.classList.toggle("now", playing && i === line));
}

function stop() {
  clearTimeout(timer);
  timer = null;
  if (audio) {
    audio.onended = null;
    audio.onerror = null;
    audio.pause();
    audio = null;
  }
}

function setPlaying(on) {
  playing = on;
  $("play").textContent = on ? "Pause" : "Play";
  highlight();
}

function speak() {
  stop();
  if (line >= timed.length) return finished();
  highlight();
  const current = timed[line];
  const after = () => { line += 1; timer = setTimeout(speak, GAP_MS); };
  const read = () => { timer = setTimeout(after, (current.seconds * 1000) / speed); };
  if (!current.src) return read();
  audio = new Audio(current.src);
  audio.playbackRate = speed;
  audio.onended = after;
  audio.onerror = read;
  audio.play().catch(read);
}

function finished() {
  setPlaying(false);
  const step = tutorial.steps[state.step];
  if (step.try && !solved[step.id]) return $("typed").focus();
  if (step.choice && !state.answers[step.id]) return $("choice-options").firstChild?.focus();
  $("next").focus();
}

function go(by) {
  stop();
  const wasPlaying = playing;
  state = move(state, tutorial, by);
  render();
  $("step-title").focus();
  if (wasPlaying || by > 0) {
    setPlaying(true);
    speak();
  } else {
    setPlaying(false);
  }
}

function togglePlay() {
  if (playing) {
    stop();
    setPlaying(false);
    return;
  }
  if (line >= timed.length) line = 0;
  setPlaying(true);
  speak();
}

function wire() {
  $("begin").addEventListener("click", () => {
    $("intro").hidden = true;
    $("stage").hidden = false;
    render();
    setPlaying(true);
    speak();
  });
  $("play").addEventListener("click", togglePlay);
  $("replay").addEventListener("click", () => { stop(); line = 0; setPlaying(true); speak(); });
  $("back").addEventListener("click", () => go(-1));
  $("next").addEventListener("click", () => go(1));
  $("again").addEventListener("click", () => { stop(); setPlaying(false); state = start(tutorial); solved = {}; render(); });
  $("speed").addEventListener("click", () => {
    speed = speed === 1 ? 1.25 : 1;
    $("speed").textContent = `${speed}×`;
    if (audio) audio.playbackRate = speed;
  });
  $("sound").setAttribute("aria-pressed", String(sound));
  $("sound").textContent = sound ? "Sound on" : "Sound off";
  $("sound").addEventListener("click", () => {
    sound = !sound;
    store.set("hound-tutorials-sound", sound);
    $("sound").setAttribute("aria-pressed", String(sound));
    $("sound").textContent = sound ? "Sound on" : "Sound off";
    const at = line;
    stop();
    timed = lines(tutorial.steps[state.step], sound ? manifest : null, `audio/${tutorial.id}/`);
    line = at;
    if (playing) speak();
  });
  $("try").addEventListener("submit", (event) => {
    event.preventDefault();
    const step = tutorial.steps[state.step];
    if (commandMatches($("typed").value, step.try.answer)) {
      solved[step.id] = true;
      render();
      $("next").focus();
    } else {
      $("try-hint").textContent = hint($("typed").value, step.try.answer) || "Not quite. Check the spelling.";
    }
  });
  $("show").addEventListener("click", () => {
    const step = tutorial.steps[state.step];
    solved[step.id] = true;
    render();
  });
  $("read").addEventListener("click", () => {
    const reading = $("transcript").hidden;
    stop();
    setPlaying(false);
    $("transcript").hidden = !reading;
    $("read").setAttribute("aria-pressed", String(reading));
    $("read").textContent = reading ? "Back to the tutorial" : "Read it instead";
    if (reading) {
      $("intro").hidden = true;
      $("stage").hidden = true;
    } else {
      $("stage").hidden = false;
      render();
    }
  });
  document.addEventListener("keydown", (event) => {
    if ($("stage").hidden || event.target.closest?.("input, textarea, button, a")) return;
    if (event.key === " ") { event.preventDefault(); togglePlay(); }
    if (event.key === "ArrowRight") go(1);
    if (event.key === "ArrowLeft") go(-1);
  });
  window.addEventListener("hashchange", () => { stop(); setPlaying(false); load(); });
}

wire();
load();
