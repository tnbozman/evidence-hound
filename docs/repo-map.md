# Repository map

Every folder in this repository, why it is there, who uses it, and whether it stays. A test (`RepoMapTests`) fails when a folder is
added without a row here, or a row names a folder that no longer exists, so add the row in the same change as the folder.

Users: **person** (a GRC team member or contributor reads or edits it), **agent** (the person's coding agent reads it), **hound** (the
CLI reads or embeds it at run time), **CI** (a workflow or test runs it), **release** (it ships in a package or image).

Verdicts: **keep**; **merge** into the folder named; **remove**, with the reason. A verdict that removes something a person may rely on
waits for the maintainer (s19-02).

Reviewed 2026-11-26 (s19-01).

## Top level

| Folder | Purpose | Users | Verdict |
| --- | --- | --- | --- |
| `.claude/` | Slash commands for contributors' Claude Code (`/start-sprint`, `/next-task`, `/check-feedback`). | agent | keep |
| `.config/` | .NET local tool manifest. | CI | keep |
| `.devcontainer/` | The dev container and its disposable CISO Assistant. | person, CI (bats) | keep |
| `.githooks/` | Pre-commit hook: format check and gitleaks. | person | keep |
| `.github/` | CI, release, CodeQL and Dependabot workflows, PR template. | CI | keep |
| `.vscode/` | Launch and task settings for the CLI. | person | keep |
| `agent/` | Source of the Claude Code and Copilot skills, agents and rules that `hound agent setup` writes. | hound, agent | keep |
| `deploy/` | Reference Docker Compose deployment, backups and Caddy. | person, CI | keep |
| `dev/` | Local CISO Assistant for `make ca-up`, and its seed. | person, CI | keep |
| `docs/` | The guides published to the documentation site, plus sprint plans. | person, agent | keep |
| `examples/` | Fictional Example Pty Ltd configuration, repos and inputs. | person, agent, CI | keep |
| `frameworks/` | Framework libraries hound loads into CISO Assistant (fictional example and published frameworks). | hound, CI | keep |
| `mappings/` | Check-to-requirement mappings shipped with hound. | hound, CI | keep |
| `profiles/` | REST collector profiles and their offline tests. | hound, CI | keep |
| `schemas/` | JSON schemas for `hound.yaml`, scope, mappings and profiles. | hound, person, agent | keep |
| `scripts/` | Build, release, dev-environment and check scripts. | person, CI | keep |
| `skills/` | The default onboarding skills embedded in hound (applicability, supplier assessment, profile authoring); a compliance repo's own `skills/` folder adds to them. | hound, agent | keep. Two homes for agent guidance (`agent/skills/` are workflows, `skills/` are reference knowledge the workflows include) is a cost; s19-03 weighs moving these under `agent/`. |
| `src/` | Production code. | release | keep |
| `templates/` | `dotnet new` template for a new collector. | person, CI | keep |
| `tests/` | Unit, contract, functional and script tests. | CI | keep |
| `tools/` | Pinned Python and Node tools for the docs site, changelog and Markdown lint. | CI | keep. A compiled Python file committed under `tools/docs/hooks/__pycache__/` is removed and ignored. |

## `src/`

| Folder | Purpose | Users | Verdict |
| --- | --- | --- | --- |
| `src/Collectors/` | Collectors: Microsoft 365, manual evidence, the generic REST engine and vulnerability scan import. | hound, release | keep |
| `src/EvidenceHound.CisoAssistant/` | CISO Assistant REST client, publisher and scope state reader. | hound | keep |
| `src/EvidenceHound.Cli/` | The `hound` command line (composition root). | person, agent | keep |
| `src/EvidenceHound.Collectors.Abstractions/` | The collector contract third-party collectors build on. | release (NuGet) | keep |
| `src/EvidenceHound.Collectors.Testing/` | Test kit for collector authors. | release (NuGet), CI | keep |
| `src/EvidenceHound.Core/` | Domain records and pure logic. | hound | keep |
| `src/EvidenceHound.Core.Yaml/` | YAML loading and editing of config, scope and mappings. | hound | keep |
| `src/EvidenceHound.IacDevServer/` | Fictional IaC pipeline API for the trial and tests. | person, CI | keep |
| `src/EvidenceHound.M365DevServer/` | Fictional Microsoft 365 tenant for the trial and tests. | person, CI | keep |
| `src/EvidenceHound.Onboarding/` | Agent kit rendering, skills and onboarding sessions. | hound | keep |
| `src/EvidenceHound.Runtime/` | Runner, scheduler, evidence store and config. | hound | keep |
| `src/EvidenceHound.ScanDev/` | Writes fictional vulnerability scan exports. | person, CI | keep |
| `src/EvidenceHound.SophosDevServer/` | Fictional Sophos Central tenant for the trial and tests. | person, CI | keep |
| `src/EvidenceHound.Transport.Kafka/` | Sends updates to CISO Assistant's Kafka dispatcher instead of the REST API (`HOUND_TRANSPORT=kafka`). | person (if anyone runs it) | remove, waiting on the maintainer: the dispatcher cannot read state, link evidence to controls or skip unchanged rows, and a small team would have to run Kafka for it. REST does all of that. |

## `tests/`

| Folder | Purpose | Users | Verdict |
| --- | --- | --- | --- |
| `tests/EvidenceHound.CisoAssistant.Tests/` | Contract tests of the CISO Assistant client against recorded responses. | CI | keep |
| `tests/EvidenceHound.Cli.Tests/` | Command tests, example repo and agent kit checks. | CI | keep |
| `tests/EvidenceHound.Collectors.M365.Tests/` | Microsoft 365 collector tests. | CI | keep |
| `tests/EvidenceHound.Collectors.Manual.Tests/` | Manual evidence collector tests. | CI | keep |
| `tests/EvidenceHound.Collectors.Rest.Tests/` | REST engine and profile tests. | CI | keep |
| `tests/EvidenceHound.Collectors.Testing.Tests/` | Tests of the collector test kit. | CI | keep |
| `tests/EvidenceHound.Collectors.VulnScan.Tests/` | Vulnerability scan import tests. | CI | keep |
| `tests/EvidenceHound.Core.Tests/` | Domain and pure logic tests. | CI | keep |
| `tests/EvidenceHound.Core.Yaml.Tests/` | YAML loading and editing tests. | CI | keep |
| `tests/EvidenceHound.Functional.Tests/` | Functional tests against a real CISO Assistant (Testcontainers). | CI | keep |
| `tests/EvidenceHound.IacDevServer.Tests/` | IaC dev server tests. | CI | keep |
| `tests/EvidenceHound.Onboarding.Tests/` | Agent kit and onboarding tests. | CI | keep |
| `tests/EvidenceHound.Runtime.Tests/` | Runner, config and shipped mapping tests. | CI | keep |
| `tests/EvidenceHound.ScanDev.Tests/` | Scan export writer tests. | CI | keep |
| `tests/EvidenceHound.SophosDevServer.Tests/` | Sophos dev server tests. | CI | keep |
| `tests/compose/` | bats tests of the reference Compose stack and backups. | CI | keep |
| `tests/docs/` | bats tests of the link checker. | CI | keep |
| `tests/fixtures/` | Recorded API responses and sample data shared by tests. | CI | keep |
| `tests/image/` | bats tests of the runner image. | CI | keep |

## `docs/`

| Folder | Purpose | Users | Verdict |
| --- | --- | --- | --- |
| `docs/adopt/` | Guides for adopting a framework or migrating from spreadsheets. | person | keep; s19-08 places them on one route |
| `docs/collectors/` | One reference page per collector. | person, agent | keep |
| `docs/deploy/` | Backups, hardening and updates of a deployment. | person | keep |
| `docs/design/` | Design records: agent-driven onboarding, guided review, REST collector. | person (contributors) | keep. `onboarding-assistant.md` was superseded on 2026-11-23 and describes code that was removed: remove, waiting on the maintainer. |
| `docs/evidence/` | Functional check records for s00 to s09; later sprints record them in the sprint file. Not on the site. | nobody now | remove, waiting on the maintainer: history that git keeps; the sprint files link to it and would link to the commit instead. |
| `docs/examples/` | The onboarding tutorial's walk-through. | person | merge into `docs/tutorials/` (s19-08) |
| `docs/govern/` | Committee, board, SSP and the continuous compliance procedure. | person | keep |
| `docs/migrations/` | Essential Eight to ASD Essentials migration. | person | keep |
| `docs/operate/` | Role guides (GRC member, system owner, operator, committee member). | person | keep; s19-08 checks the overlap with `docs/training/` |
| `docs/reviews/` | The engineering and CISO reviews before release 3 (s19). | person (contributors) | keep |
| `docs/sprints/` | Sprint plans and reviews. Not on the site. | person, agent | keep |
| `docs/training/` | Role training (board, committee, system owners, AI governance). | person | keep; s19-08 checks the overlap with `docs/operate/` |
| `docs/tutorials/` | Trial, first deployment, dev servers, writing a collector or profile. | person | keep |

## `examples/`

| Folder | Purpose | Users | Verdict |
| --- | --- | --- | --- |
| `examples/apis/` | A fictional OpenAPI spec for the profile-writing tutorial. | person, CI | keep |
| `examples/compliance-repo/` | The fictional organisation's compliance repo: config, scope, mappings, SSP and IRP. | person, agent, CI | keep |
| `examples/manual-exports/` | One fictional firewall export used only by the s04 evidence record. | nobody now | remove with `docs/evidence/`; `examples/manual-sources/` has the live examples |
| `examples/manual-sources/` | Manual evidence templates and fictional exports. | person, agent, CI | keep |
| `examples/migration/` | CSV inputs for migrating from spreadsheets. | person, CI | keep |
| `examples/onboarding-tutorial/` | The onboarding tutorial's starting repo. | person, CI | keep |
| `examples/trial/` | Inputs the local trial uses (supplier inbox, specs). | person, CI | keep |
| `examples/workbooks/` | A fictional audit workbook for `hound audit import`. | person, CI | keep |

Two files at the top of `examples/`, `example-pty-ltd.yaml` and `example-essential-eight.yaml`, are older deployment configs beside
`examples/compliance-repo/hound.yaml`; many tests and guides read them. s19-03 decides whether to fold them into the compliance repo.

## `scripts/`

| Folder | Purpose | Users | Verdict |
| --- | --- | --- | --- |
| `scripts/cheatsheet/` | Renders the cheat sheet PDF. | person, CI | keep |
| `scripts/lib/` | Shell functions the sprint and release scripts share. | CI | keep |
| `scripts/tests/` | bats tests of the scripts. | CI | keep |

The scripts at the top of `scripts/` are kept: `ca-apply-scope.sh` and `ca-seed.sh` wrap `hound scope apply` for the dev container,
`ca-inventory.sh` probes a CISO Assistant release in the functional job, and the rest build, check and release.
