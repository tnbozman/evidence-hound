#!/usr/bin/env python3
"""s20: the interactive tutorials in docs/tutorials/interactive/.

  tutorials.py check [--docs DIR]                 check every tutorial and the list of them; exit 1 on any finding
  tutorials.py audio [--docs DIR] [--only ID]     speak each tutorial's narration with Kokoro, on this machine

`check` needs only Python. `audio` needs the packages in tools/tutorials/requirements.txt and ffmpeg (`make tutorials-audio` installs
the packages). The model is downloaded once into a cache outside the repo ($HOUND_KOKORO_CACHE, default ~/.cache/evidence-hound/kokoro)
and checked against the SHA-256 sums pinned below. No text leaves the machine.
"""

import argparse
import hashlib
import io
import json
import os
import pathlib
import re
import subprocess
import sys
import urllib.request
import wave

REQUIRED = ("id", "title", "summary", "minutes", "guide", "voice", "steps")
STEP_REQUIRED = ("id", "title", "narration")
INTERACTIVE = pathlib.Path("tutorials") / "interactive"
LISTED = re.compile(r"interactive/play\.html#(?P<id>[a-z0-9-]+)")
COMPANY = re.compile(r"\b(?:[A-Z][A-Za-z&]*\s)+(?:Pty Ltd|Ltd|Inc|LLC)\b")

# Kokoro-82M (Apache-2.0) as ONNX, from kokoro-onnx's model release. Change a sum only with the file it was taken from.
MODEL = {
    "kokoro-v1.0.onnx": (
        "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/kokoro-v1.0.onnx",
        "7d5df8ecf7d4b1878015a32686053fd0eebe2bc377234608764cc0ef3636a6c5",
    ),
    "voices-v1.0.bin": (
        "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/voices-v1.0.bin",
        "bca610b8308e8d99f32e6fe4197e7ec01679264efed0cac9140fe9c29f1fbf7d",
    ),
}

# Written terms the model would misread, as they are said. Speech only: the captions keep the written words.
PRONUNCIATION = [
    (r"\bCISO\b", "seeso"),
    (r"\bGRC\b", "G R C"),
    (r"\bISM\b", "I S M"),
    (r"\bML2\b", "maturity level two"),
    (r"\bE8\b", "Essential Eight"),
    (r"\bYAML\b", "yammel"),
    (r"\bJSON\b", "jay-son"),
    (r"\bOCI\b", "O C I"),
    (r"\bVM\b", "V M"),
    (r"\bAPI\b", "A P I"),
    (r"\bCMMC\b", "C M M C"),
    (r"\bSPRS\b", "S P R S"),
    (r"\bSSP\b", "S S P"),
    (r"\bIaC\b", "infrastructure as code"),
    (r"\bMFA\b", "M F A"),
    (r"\bhound\.yaml\b", "hound dot yammel"),
]


class ModelError(Exception):
    """The speech model is missing or is not the file whose sum is pinned."""


# ---- check ---------------------------------------------------------------------------------------------------------------------


def check(tutorial, name, docs):
    """Every finding for one tutorial, each naming the file and, where there is one, the step."""
    findings = [f"{name}: missing field '{field}'" for field in REQUIRED if field not in tutorial]
    guide = tutorial.get("guide")
    if guide and not (pathlib.Path(docs) / guide).is_file():
        findings.append(f"{name}: guide {guide}: no such file")
    findings += _companies(name, "", [tutorial.get("title", ""), tutorial.get("summary", "")])
    findings += _next(tutorial, name, pathlib.Path(docs))

    seen = set()
    for step in tutorial.get("steps", []):
        where = f"{name}: step {step.get('id', '?')}"
        findings += [f"{where}: missing field '{field}'" for field in STEP_REQUIRED if field not in step]
        if step.get("id") in seen:
            findings.append(f"{where}: duplicate step id")
        seen.add(step.get("id"))
        if "narration" in step and not step["narration"]:
            findings.append(f"{where}: empty narration")
        findings += _choice(where, step.get("choice"))
        commands = [line.get("command") for line in step.get("terminal", [])]
        if "try" in step and step["try"].get("answer") not in commands:
            findings.append(f"{where}: try answer is not one of the step's commands")
        for number, line in enumerate(step.get("terminal", []), start=1):
            if line.get("who") not in ("you", "agent"):
                findings.append(f"{where}: terminal line {number}: who must be 'you' or 'agent'")
        findings += _companies(name, f" step {step.get('id', '?')}", _texts(step))
    return findings


def _next(tutorial, name, docs):
    found = []
    for link in tutorial.get("next", []):
        where = f"{name}: next '{link.get('title', '?')}'"
        if "tutorial" in link and not (docs / INTERACTIVE / f"{link['tutorial']}.json").is_file():
            found.append(f"{where}: no tutorial {link['tutorial']}")
        if "guide" in link and not (docs / link["guide"]).is_file():
            found.append(f"{where}: guide {link['guide']}: no such file")
    return found


def _choice(where, choice):
    if choice is None:
        return []
    if not any(option.get("right") for option in choice.get("options", [])):
        return [f"{where}: choice has no right answer"]
    return []


def _texts(step):
    yield step.get("title", "")
    yield from step.get("narration", [])
    for line in step.get("terminal", []):
        yield line.get("command", "")
        yield line.get("output", "")
    yield from step.get("panel", {}).get("lines", [])
    yield step.get("try", {}).get("prompt", "")
    choice = step.get("choice", {})
    yield choice.get("question", "")
    for option in choice.get("options", []):
        yield option.get("text", "")
        yield option.get("feedback", "")


def _companies(name, where, texts):
    found = []
    for text in texts:
        for match in COMPANY.finditer(text):
            if "Example" not in match.group(0).split():
                found.append(f"{name}:{where}: '{match.group(0)}' is not the fictional Example Pty Ltd")
    return found


def check_index(docs):
    """The tutorials docs/tutorials/interactive.md lists against the tutorial files."""
    docs = pathlib.Path(docs)
    listed = set(LISTED.findall((docs / "tutorials" / "interactive.md").read_text(encoding="utf-8")))
    present = {p.stem for p in (docs / INTERACTIVE).glob("*.json")}
    return sorted(
        [f"interactive.md: {i}: no such tutorial" for i in listed - present]
        + [f"interactive.md: {i}: not listed" for i in present - listed])


def check_all(docs):
    docs = pathlib.Path(docs)
    if not (docs / INTERACTIVE).is_dir():
        return ["tutorials/interactive: no such folder"]
    findings = check_index(docs)
    for path in sorted((docs / INTERACTIVE).glob("*.json")):
        try:
            tutorial = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as error:
            findings.append(f"{path.name}: not JSON: {error}")
            continue
        findings += check(tutorial, path.name, docs)
        if tutorial.get("id") != path.stem:
            findings.append(f"{path.name}: id '{tutorial.get('id')}' is not the file's name")
    return findings


# ---- narration -----------------------------------------------------------------------------------------------------------------


def spoken(text):
    """The text as the model should say it."""
    for pattern, said in PRONUNCIATION:
        text = re.sub(pattern, said, text)
    return text


def _hash(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()[:16]


def narrate(tutorial, out, speak):
    """
    Speaks every sentence that has no audio yet into `out`, one MP3 per sentence, and writes narration.json. `speak(text, voice)`
    returns (mp3 bytes, seconds). A sentence's file is named by its voice and spoken text, so an unchanged sentence is never spoken
    again; files no sentence uses any more are removed. Returns how many sentences were spoken.
    """
    out = pathlib.Path(out)
    out.mkdir(parents=True, exist_ok=True)
    voice = tutorial["voice"]
    manifest_path = out / "narration.json"
    known = {}
    if manifest_path.exists():
        old = json.loads(manifest_path.read_text(encoding="utf-8"))
        known = {line["file"]: line["seconds"] for lines in old.get("steps", {}).values() for line in lines}

    steps, used, count = {}, set(), 0
    for step in tutorial["steps"]:
        lines = []
        for text in step["narration"]:
            said = spoken(text)
            file = _hash(f"{voice}\n{said}") + ".mp3"
            if file not in known or not (out / file).exists():
                audio, seconds = speak(said, voice)
                (out / file).write_bytes(audio)
                known[file] = round(seconds, 2)
                count += 1
            used.add(file)
            lines.append({"text": text, "hash": _hash(text), "file": file, "seconds": known[file]})
        steps[step["id"]] = lines

    for stale in out.glob("*.mp3"):
        if stale.name not in used:
            stale.unlink()
    manifest = {"tutorial": tutorial["id"], "voice": voice, "model": "Kokoro-82M v1.0 (kokoro-onnx)", "steps": steps}
    manifest_path.write_text(json.dumps(manifest, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    return count


def verify(path, sha256):
    path = pathlib.Path(path)
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1 << 20), b""):
            digest.update(block)
    if digest.hexdigest() != sha256:
        raise ModelError(f"{path.name}: SHA-256 is {digest.hexdigest()}, expected {sha256}. Delete it and run again.")


def fetch_model(cache):
    """The model's files in `cache`, downloaded once and verified every time."""
    cache = pathlib.Path(cache)
    cache.mkdir(parents=True, exist_ok=True)
    for name, (url, sha256) in MODEL.items():
        target = cache / name
        if not target.exists():
            print(f"Downloading {name} (once) into {cache}", file=sys.stderr)
            partial = target.with_suffix(target.suffix + ".part")
            with urllib.request.urlopen(url) as response, partial.open("wb") as stream:  # noqa: S310 (pinned https URL)
                while block := response.read(1 << 20):
                    stream.write(block)
            verify(partial, sha256)
            partial.rename(target)
        verify(target, sha256)
    return cache


class KokoroSpeaker:
    """Kokoro on this machine: text to 24 kHz speech, encoded as mono MP3 by ffmpeg."""

    def __init__(self, cache):
        # Imported here, so `check` and the tests need no packages.
        import numpy
        from kokoro_onnx import Kokoro

        self._numpy = numpy

        self._kokoro = Kokoro(str(cache / "kokoro-v1.0.onnx"), str(cache / "voices-v1.0.bin"))

    def __call__(self, text, voice):
        lang = "en-gb" if voice.startswith("b") else "en-us"
        samples, rate = self._kokoro.create(text, voice=voice, speed=1.0, lang=lang)
        pcm = io.BytesIO()
        with wave.open(pcm, "wb") as wav:
            wav.setnchannels(1)
            wav.setsampwidth(2)
            wav.setframerate(rate)
            wav.writeframes((self._numpy.clip(samples, -1.0, 1.0) * 32767).astype("<i2").tobytes())
        mp3 = subprocess.run(
            ["ffmpeg", "-loglevel", "error", "-i", "pipe:0", "-ac", "1", "-b:a", "48k", "-f", "mp3", "pipe:1"],
            input=pcm.getvalue(), capture_output=True, check=True).stdout
        return mp3, len(samples) / rate


# ---- command line --------------------------------------------------------------------------------------------------------------


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["check", "audio"])
    parser.add_argument("--docs", default=str(pathlib.Path(__file__).resolve().parents[1] / "docs"))
    parser.add_argument("--only", help="one tutorial's id")
    parser.add_argument("--cache", default=os.environ.get("HOUND_KOKORO_CACHE", str(pathlib.Path.home() / ".cache" / "evidence-hound" / "kokoro")))
    args = parser.parse_args(argv)

    findings = check_all(args.docs)
    for finding in findings:
        print(finding, file=sys.stderr)
    if findings:
        return 1
    if args.command == "check":
        print(f"{len(list((pathlib.Path(args.docs) / INTERACTIVE).glob('*.json')))} tutorial(s), no findings.")
        return 0

    try:
        speaker = KokoroSpeaker(fetch_model(args.cache))
    except ModelError as error:
        print(f"tutorials: {error}", file=sys.stderr)
        return 1
    for path in sorted((pathlib.Path(args.docs) / INTERACTIVE).glob("*.json")):
        if args.only and path.stem != args.only:
            continue
        tutorial = json.loads(path.read_text(encoding="utf-8"))
        count = narrate(tutorial, path.parent / "audio" / tutorial["id"], speaker)
        print(f"{tutorial['id']}: {count} sentence(s) spoken")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
