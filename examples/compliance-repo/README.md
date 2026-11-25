# Example Pty Ltd compliance repo

A complete, fictional compliance repo for [Evidence Hound](../../README.md). Copy this folder to a new **private** repository to start your own: everything about your organisation lives there, never in the public Evidence Hound repo. The [first deployment tutorial](../../docs/tutorials/first-deployment.md) walks through it, from an empty VM to a scheduled run.

| Path | What it holds |
| --- | --- |
| [`deploy/`](deploy/README.md) | The deployment settings for Evidence Hound's Compose stack (`deploy.env`) and your additions to it (`compose.override.yml`). No secrets: those are files on the host. |
| [`hound.yaml`](hound.yaml) | The deployment config: how to reach CISO Assistant, the collectors and their schedules, and the audit results are published to. |
| [`scope/`](scope/) | What CISO Assistant should hold: the domains, perimeters and systems (`setup.yaml`), the applied controls (`controls.yaml`), one file per audit (`audits/`), who can do what (`access.yaml`) and CISO Assistant's feature flags and settings (`settings.yaml`). |
| [`mappings/`](mappings/) | Which checks satisfy which requirements, per framework. |
| [`frameworks/`](frameworks/) | Framework snapshots, so the scope validates offline, and custom framework libraries. |
| [`documents/`](documents/) | Policies and procedures uploaded as evidence. |
| [`manual-sources/`](manual-sources/) | The sources people export by hand, and where their exports go (outside this repo). |
| [`.devcontainer/`](.devcontainer/devcontainer.json) | The GRC team's workspace: released `hound` and the Claude Code and GitHub Copilot extensions. |
| [`.gitignore`](.gitignore) | Keeps secrets, keys, certificates, `.env` files and evidence out of the repo. |
| [`CLAUDE.md`](CLAUDE.md) | Instructions for an AI assistant working in this repo. |
| [`.github/workflows/compliance.yml`](.github/workflows/compliance.yml) | Scans every change for secrets, validates every pull request, comments the plan, and applies on merge after approval. |

Every YAML file starts with a `# yaml-language-server: $schema=` comment, so editors check it as you type. In your own repo, point those at the schemas of the Evidence Hound version you run.

## Work in the devcontainer

Open the repo in VS Code with the Dev Containers extension and choose **Reopen in Container**. Your machine needs only Docker: the
container has `hound` from the release your deployment runs (the runner image pinned in `.devcontainer/Dockerfile`, the same as
`deploy/deploy.env`), the .NET runtime it needs, the GitHub CLI and the Claude Code and GitHub Copilot extensions. No SDK or build tools.

- Set `HOUND_CA_TOKEN` in your own environment before you open it; the container reads it from there, never from the repo.
- `hound` writes evidence to `/var/lib/evidence-hound` inside the container, which is only for trying things. Scheduled runs and their
  evidence stay on the server.
- When you upgrade the deployment, move the image in `.devcontainer/Dockerfile` to the same release and rebuild the container.

## How a change goes in

1. Edit the files on a branch, or ask an assistant to (it follows [`CLAUDE.md`](CLAUDE.md)).
2. Check locally: `hound scope validate scope/ --config hound.yaml` and `hound mapping validate mappings/<file>.yaml`.
3. Open a pull request. The workflow validates it and comments what `hound scope plan` would change in CISO Assistant.
4. A reviewer approves the pull request; merging runs `hound scope apply` behind the protected `ciso-assistant` environment, which needs its own approval.

The full guide is Evidence Hound's [scope files guide](../../docs/scope.md).
