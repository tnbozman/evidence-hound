# Evidence Hound

*Sniffs out compliance evidence from your systems and fetches it into CISO Assistant, on schedule, every time.*

Small businesses in the defence industry must reach and then sustain cyber compliance, such as Essential Eight Maturity Level 2 (DISP, Australia) or NIST SP 800-171 (CMMC, US), usually without a dedicated CISO or GRC team.

This project shows how to do that with open-source tools:

- **Self-hosted [CISO Assistant](https://github.com/intuitem/ciso-assistant-community)** on Docker is the system of record.
- **Collectors** gather evidence and control status from your systems on a schedule and update CISO Assistant audits, giving continuous compliance instead of a once-a-year scramble.
- **A broad set of CISO Assistant features** (compliance assessments, applied controls, evidence, risks, third parties, policies, exceptions, findings, recurring tasks) keeps assurance current between audits.

It is designed to be built and run by an ordinary in-house IT and software team, with AI-assisted development doing most of the build and automation doing most of the upkeep.

## What this repo provides

| Part | Purpose |
| --- | --- |
| Core library | One standard way to update a CISO Assistant audit: evidence, framework-neutral check results, applied control status. Maps to CISO Assistant's dispatcher messages (`upload_attachment`, `update_requirement_assessment`, `update_applied_control`). |
| Publisher | Sends updates to the CISO Assistant REST API (Kafka and the official dispatcher optional). |
| Framework mappings | One file per framework turns neutral checks into requirement results. Essential Eight first; NIST SP 800-171 next. |
| Collector SDK | A plugin interface, template and test kit. A collector only talks to its target system. |
| Manual-upload collector | For systems without a usable API: schedule, upload, same audit updates. |
| Reference collectors | Microsoft 365, Sophos Central, Veeam Backup & Replication. |
| Reporting | Monthly security committee report (operational) and quarterly board report (governance), generated from CISO Assistant data. |
| Reference deployment | Docker Compose for CISO Assistant plus the collector runner, with backups and hardening notes. |

Your organisation's configuration (system names, tenant IDs, audit IDs, mappings with real observations) never goes in this repo. Keep it in a private deployment repo.

## How the work is organised

- Delivery is planned as sprints in [`docs/sprints/`](docs/sprints/README.md). Each sprint file lists tasks with acceptance criteria and the tests to write first.
- A sprint starts with the **Start sprint** workflow (Actions tab) or `scripts/start-sprint.sh sNN`, which creates `sprint/sNN-<slug>` and a draft PR.
- Every task is implemented by strict test-driven development (red, green, refactor). See [`docs/engineering.md`](docs/engineering.md).
- CI, CodeQL, secret scanning and Dependabot results are the feedback loop. See [`docs/closed-loop.md`](docs/closed-loop.md).
- Claude Code implements tasks one at a time following [`CLAUDE.md`](CLAUDE.md).

## Develop

You only need Docker and VS Code (or GitHub Codespaces). Open the repo and choose **Reopen in Container**. The dev container has the SDK, test tools, debugger, GitHub CLI, Claude Code and Docker for running a local CISO Assistant.

## Try it with a local CISO Assistant

`make ca-up` starts a disposable CISO Assistant (pinned release, SQLite) and seeds it with a service-account token and the fictional Example Pty Ltd audit. Open <https://localhost:8443> and sign in as `admin@example.com` (password `HOUND_CA_ADMIN_PASSWORD`, default `change-me-local-only`). Then follow [`docs/publishing.md`](docs/publishing.md) to publish the example run. `make ca-down ARGS=-v` removes it.

## Licence

Apache-2.0. CISO Assistant itself is AGPL-3.0 and is used over its API only.
