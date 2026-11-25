# Getting started

Evidence Hound has two parts: `hound`, a command line tool, and a reference deployment that runs CISO Assistant and `hound` side by
side with Docker Compose. Start with the trial on your own machine, then deploy.

## 1. Try it locally

You need Docker and an hour or two. The [local trial](tutorials/local-trial.md) starts a local CISO Assistant, sets up a fictional
organisation with `hound init`, onboards its suppliers and systems with your coding agent, runs every collector against built-in dev
servers (no real tenant needed) and walks you through a weekly review. `make trial` from a clone of this repository runs it step by
step.

## 2. Install hound

Pick one:

- **The runner image**, for scheduled runs next to CISO Assistant: `ghcr.io/tnbozman/evidence-hound:<version>`. Check what you pull:
  `gh attestation verify oci://ghcr.io/tnbozman/evidence-hound:<version> --owner tnbozman`.
- **The .NET tool**, for your laptop and your agent (needs the .NET 10 SDK). Download the `.nupkg` files from the
  [release](https://github.com/tnbozman/evidence-hound/releases) into a folder, then install with the release's version:

  ```console
  $ dotnet tool install --global EvidenceHound.Cli --version 1.0.0 --add-source ~/hound-packages
  $ hound --version
  ```

  When the packages are on nuget.org, `dotnet tool install --global EvidenceHound.Cli --version 1.0.0` is enough. Give the version
  either way: without it, `dotnet tool install` looks only for the newest version on nuget.org.

## 3. Make your compliance repo

Everything about your organisation lives in a private git repository of your own: the deployment config, the scope that becomes
CISO Assistant's audits, mappings, supplier files and manual evidence runbooks. Make it once, in one of two ways:

- **Small and guided:** `hound init` asks about your organisation and writes a repo you grow with `hound add`. Best for a first
  look, or with your agent driving.
- **The full template:** the [first deployment tutorial](tutorials/first-deployment.md#1-create-the-private-compliance-repo) copies
  `examples/compliance-repo`, with access, settings, the deployment files and a pull request workflow already in place. Best when
  you deploy straight away.

The guided way:

```console
$ hound init my-compliance --org "Example Pty Ltd"
$ cd my-compliance
$ git init -b main && git add . && git commit -m "Start from hound init"
$ hound agent setup --assistant claude
```

Push it to a private repository on your git host. When you deploy, copy the template's `deploy/`, `scope/settings.yaml` and
`.github/` into it, as the tutorial says.

[Guided setup](guided-setup.md) explains each question `hound init` asks, and [onboarding](onboarding.md) how your agent drafts the
rest for you to approve.

## 4. Deploy

Follow the [first deployment tutorial](tutorials/first-deployment.md) from an empty VM, with the repo you made above: the Compose stack, the runner's token, your
compliance repo mounted read-only, the first scope apply and the schedule. Then:

1. [Back it up and test a restore](deploy/backups.md).
2. [Harden the host](deploy/hardening.md).
3. [Plan monthly updates](deploy/updates.md).

## 5. Run it each week

The [GRC member guide](operate/grc-member.md) is the weekly routine: your agent prepares the queue, you run `hound review` for one
session, and the next run publishes what you accepted. Reports for the committee and board come from
[`hound report`](reporting.md) each month and quarter.
