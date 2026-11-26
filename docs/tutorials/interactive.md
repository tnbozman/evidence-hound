# Interactive tutorials

Watch and try each step of [getting started](../getting-started.md) in a few minutes. Each tutorial is narrated, with captions, a
terminal that shows what hound prints, and a few things for you to do: type a command, or decide what to do with an item in the weekly
review. They work with the sound off.

| Tutorial | Minutes | What you learn |
| --- | --- | --- |
| [Try it locally](interactive/play.html#local-trial) | 6 | The local trial: a fictional company, your agent drafting, you approving, collectors publishing to CISO Assistant |
| [Make your compliance repo](interactive/play.html#compliance-repo) | 6 | Install hound, `hound init`, your agent drafts a supplier and a system, you approve and apply |
| [Deploy it](interactive/play.html#first-deployment) | 7 | The stack on a VM, its secrets, the runner's token, the first apply and the schedule |
| [Your week: the review](interactive/play.html#your-week) | 7 | Your agent prepares the queue, you accept or challenge each item in one session, then check the collectors |

Each tutorial follows its written guide, which stays the reference: the [local trial](local-trial.md), [onboarding](../onboarding.md),
the [first deployment](first-deployment.md) and [your week](../operate/grc-member.md).

## How the narration is made

The voice is [Kokoro](https://github.com/hexgrad/kokoro), an open-weight text-to-speech model (Kokoro-82M, Apache-2.0), run on our
own machine through [kokoro-onnx](https://github.com/thewh1teagle/kokoro-onnx) (MIT). No cloud speech service is used and no text leaves
the machine. espeak-ng, which turns words into sounds for the model, is GPL-3.0 and is used only to make the audio.

Each tutorial is a JSON file in `docs/tutorials/interactive/`. To change one, edit its JSON, run `make test-docs` (it checks every
step, and CI checks every `hound` command and option against the CLI), then `make tutorials-audio` to speak the sentences that changed.
The model is downloaded once into `~/.cache/evidence-hound/kokoro` and checked against pinned SHA-256 sums. The audio is not committed:
the release workflow speaks it before it publishes the site. A sentence whose audio is missing or out of date is shown as a caption
and timed for reading.
