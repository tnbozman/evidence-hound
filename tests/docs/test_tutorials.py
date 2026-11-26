"""s20: the interactive tutorials' checks and narration (scripts/tutorials.py). Run with `make test-docs`."""

import copy
import hashlib
import importlib.util
import json
import pathlib
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
FIXTURES = pathlib.Path(__file__).resolve().parent / "fixtures" / "tutorials" / "docs"

_spec = importlib.util.spec_from_file_location("tutorials", ROOT / "scripts" / "tutorials.py")
tutorials = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(tutorials)


def good():
    return json.loads((FIXTURES / "tutorials" / "interactive" / "good.json").read_text(encoding="utf-8"))


class FakeSpeaker:
    """Speaks without a model: the audio is the text's bytes, one second per word."""

    def __init__(self):
        self.spoken = []

    def __call__(self, text, voice):
        self.spoken.append(text)
        return text.encode("utf-8"), float(len(text.split()))


class CheckTests(unittest.TestCase):
    def test_a_good_tutorial_has_no_findings(self):
        self.assertEqual([], tutorials.check(good(), "good.json", FIXTURES))

    def test_each_broken_rule_is_reported_with_file_and_step(self):
        broken = good()
        del broken["summary"]
        broken["guide"] = "operate/missing.md"
        broken["steps"][1]["id"] = "prepare"
        broken["steps"][0]["narration"] = []
        broken["steps"][1]["choice"]["options"][1]["right"] = False
        broken["steps"][1]["try"]["answer"] = "hound review --only x"
        broken["steps"][0]["narration"] = ["Welcome to Acme Widgets Pty Ltd."]
        broken["steps"][0]["terminal"][0]["who"] = "robot"

        findings = tutorials.check(broken, "broken.json", FIXTURES)

        expected = [
            "broken.json: missing field 'summary'",
            "broken.json: guide operate/missing.md: no such file",
            "broken.json: step prepare: duplicate step id",
            "broken.json: step prepare: choice has no right answer",
            "broken.json: step prepare: try answer is not one of the step's commands",
            "broken.json: step prepare: 'Acme Widgets Pty Ltd' is not the fictional Example Pty Ltd",
            "broken.json: step prepare: terminal line 1: who must be 'you' or 'agent'",
        ]
        for finding in expected:
            self.assertIn(finding, findings)

    def test_an_empty_narration_is_reported(self):
        broken = good()
        broken["steps"][0]["narration"] = []
        self.assertIn("t.json: step prepare: empty narration", tutorials.check(broken, "t.json", FIXTURES))

    def test_every_tutorial_is_listed_and_every_listed_one_exists(self):
        with tempfile.TemporaryDirectory() as tmp:
            docs = pathlib.Path(tmp)
            (docs / "tutorials" / "interactive").mkdir(parents=True)
            (docs / "tutorials" / "interactive" / "good.json").write_text("{}", encoding="utf-8")
            (docs / "tutorials" / "interactive" / "unlisted.json").write_text("{}", encoding="utf-8")
            (docs / "tutorials" / "interactive.md").write_text(
                "- [Good](interactive/play.html#good)\n- [Gone](interactive/play.html#gone)\n", encoding="utf-8")

            findings = tutorials.check_index(docs)

        self.assertEqual(["interactive.md: gone: no such tutorial", "interactive.md: unlisted: not listed"], findings)

    def test_the_repo_s_tutorials_have_no_findings(self):
        self.assertEqual([], tutorials.check_all(ROOT / "docs"))


class NarrationTests(unittest.TestCase):
    def test_spoken_text_uses_the_pronunciation_list(self):
        self.assertEqual(
            "Open seeso Assistant and the G R C queue.",
            tutorials.spoken("Open CISO Assistant and the GRC queue."))
        self.assertEqual("Essential Eight, maturity level two.", tutorials.spoken("Essential Eight, ML2."))

    def test_every_sentence_is_spoken_with_a_manifest(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = pathlib.Path(tmp)
            speaker = FakeSpeaker()

            tutorials.narrate(good(), out, speaker)

            manifest = json.loads((out / "narration.json").read_text(encoding="utf-8"))
            self.assertEqual(3, len(speaker.spoken))
            line = manifest["steps"]["prepare"][1]
            self.assertEqual(hashlib.sha256("Then it prepares the queue.".encode()).hexdigest()[:16], line["hash"])
            self.assertTrue((out / line["file"]).exists())
            self.assertEqual(5.0, line["seconds"])
            self.assertEqual("af_heart", manifest["voice"])

    def test_only_changed_sentences_are_spoken_again(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = pathlib.Path(tmp)
            tutorials.narrate(good(), out, FakeSpeaker())
            changed = good()
            changed["steps"][1]["narration"] = ["Now you start your session."]
            speaker = FakeSpeaker()

            tutorials.narrate(changed, out, speaker)

            self.assertEqual(["Now you start your session."], speaker.spoken)

    def test_a_new_voice_speaks_everything_again(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = pathlib.Path(tmp)
            tutorials.narrate(good(), out, FakeSpeaker())
            other = good()
            other["voice"] = "bf_emma"
            speaker = FakeSpeaker()

            tutorials.narrate(other, out, speaker)

            self.assertEqual(3, len(speaker.spoken))

    def test_unused_audio_is_removed(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = pathlib.Path(tmp)
            tutorials.narrate(good(), out, FakeSpeaker())
            before = {p.name for p in out.glob("*.mp3")}
            shorter = copy.deepcopy(good())
            shorter["steps"] = shorter["steps"][:1]

            tutorials.narrate(shorter, out, FakeSpeaker())

            after = {p.name for p in out.glob("*.mp3")}
            self.assertEqual(2, len(after))
            self.assertTrue(after < before)

    def test_a_model_with_the_wrong_hash_is_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            model = pathlib.Path(tmp) / "model.onnx"
            model.write_bytes(b"not the model")

            with self.assertRaises(tutorials.ModelError) as error:
                tutorials.verify(model, "0" * 64)

            self.assertIn("model.onnx", str(error.exception))
            tutorials.verify(model, hashlib.sha256(b"not the model").hexdigest())


if __name__ == "__main__":
    unittest.main()
