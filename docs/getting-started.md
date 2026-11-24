# Getting started

Evidence Hound has two parts: `hound`, a command line tool, and a reference deployment that runs CISO Assistant and `hound` side by
side with Docker Compose. Start with the trial on your own machine, then deploy.

## 1. Try it locally

You need Docker and a few hours. The [local trial](tutorials/local-trial.md) starts a local CISO Assistant, sets up a fictional
organisation with `hound init`, onboards its suppliers and systems with your coding agent, runs every collector against built-in dev
servers (no real tenant needed) and walks you through a weekly review. `make trial` from a clone of this repository runs it step by
step.

## 2. Install hound

Pick one:

- **The runner image**, for scheduled runs next to CISO Assistant: `ghcr.io/tnbozman/evidence-hound:<version>`. Check what you pull:
  `gh attestation verify oci://ghcr.io/tnbozman/evidence-hound:<version> --owner tnbozman`.
- **The .NET tool**, for your laptop and your agent (needs the .NET 10 runtime):
  `dotnet tool install --global EvidenceHound.Cli`, then `hound --help`. Each [release](https://github.com/tnbozman/evidence-hound/releases)
  also carries the packages as files.

## 3. Make your compliance repo

Everything about your organisation lives in a private git repository of your own: the deployment config, the scope that becomes
CISO Assistant's audits, mappings, supplier files and manual evidence runbooks.

```console
$ hound init my-compliance --org "Example Pty Ltd"
$ cd my-compliance
$ hound agent setup --assistant claude
```

[Guided setup](guided-setup.md) explains each question `hound init` asks, and [onboarding](onboarding.md) how your agent drafts the
rest for you to approve.

## 4. Deploy

Follow the [first deployment tutorial](tutorials/first-deployment.md) from an empty VM: the Compose stack, the runner's token, your
compliance repo mounted read-only, the first scope apply and the schedule. Then:

1. [Back it up and test a restore](deploy/backups.md).
2. [Harden the host](deploy/hardening.md).
3. [Plan monthly updates](deploy/updates.md).

## 5. Run it each week

The [GRC member guide](operate/grc-member.md) is the weekly routine: your agent prepares the queue, you run `hound review` for one
session, and the next run publishes what you accepted. Reports for the committee and board come from
[`hound report`](reporting.md) each month and quarter.
