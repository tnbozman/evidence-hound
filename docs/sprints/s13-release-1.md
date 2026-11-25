# s13 Release 1

Status: In progress

**Goal:** the deployment from s07 gains tested backups and a hardening and update guide, then a tagged, signed, documented first release that others can install and extend.

Tasks s13-01 and s13-02 were s08-04 and s08-05 before the release 1 reorder.

### s13-01 Backup and restore
- [x] Done

**Story:** As an operator, I want automated backups and a tested restore, so that the system of record survives failure and the restore test becomes evidence.

**Acceptance criteria**
- `deploy/compose/backup.sh`: `pg_dump`, CISO Assistant attachments volume, encrypted with age to an off-host target.
- `deploy/compose/restore-test.sh`: restores the latest backup into a scratch stack, checks the health endpoint and an audit count, and drops a result file into a manual-collector source so the restore test is recorded as evidence.

**Tests first**
- CI smoke job: back up the smoke stack, restore into a second project, compare audit counts.

**Functional check**
- Run both scripts on the VM; the restore test appears as evidence in CISO Assistant.

**Done (2026-11-25)**
- `deploy/compose/backup.sh` (pg_dump, the CISO Assistant and runner data volumes, a manifest with the audit count and SHA256SUMS, age
  to recipients, keep the newest N) and `restore-test.sh` (decrypt, check sums, restore into a scratch Compose project, health and
  audit count, drop the result into a manual source's drop folder). `backup-lib.sh` holds the pure parts, covered by
  `tests/compose/backup.bats` (12 tests, no Docker). A restore that stops part way still drops a failed result (`Failed at:`), found by
  the rehearsal.
- CI's compose job runs `make compose-smoke`, which now backs up the smoke stack, restores it into a second project and compares
  audit counts. Guide: `docs/deploy/backups.md`.
- Carried: running both scripts on a real VM and seeing the restore test as evidence in CISO Assistant needs a host.

### s13-02 Hardening and update guide
- [x] Done

**Acceptance criteria**
- `docs/deploy/hardening.md`: host patching, firewall, disk encryption, Docker daemon settings, log forwarding, certificate and uptime monitoring, MFA for all CISO Assistant users, service accounts per integration.
- `docs/deploy/updates.md`: Dependabot or Renovate for image tags, monthly window, backup before upgrade, rollback steps.

**Tests first**
- Markdown lint and link check in CI.

**Functional check**
- A reviewer applies the guide to the test VM and records the date.

**Done (2026-11-25)**
- `docs/deploy/hardening.md` and `docs/deploy/updates.md`. Markdown lint (markdownlint-cli2, `.markdownlint-cli2.jsonc`) and a link
  checker (`scripts/check-links.py`, tested by `tests/docs/links.bats`) run in CI's docs job and in `make lint-docs`.
- Carried: a reviewer applying the guide to a VM needs a person and a host.

### s13-03 Packages and versioning
- [x] Done

**Story:** As a collector author, I want the abstractions and test kit on NuGet, so that I can build collectors outside this repo.

**Acceptance criteria**
- Versioning from git tags (MinVer); semantic versioning; `CHANGELOG.md` generated from Conventional Commits.
- Packages: `EvidenceHound.Core`, `EvidenceHound.Collectors.Abstractions`, `EvidenceHound.Collectors.Testing`, the template pack, and each reference collector.
- Package metadata: licence expression, repository URL, README, SourceLink, symbols.

**Tests first**
- CI job packs all packages and fails on missing metadata.

**Functional check**
- Install the template pack from a local feed and build a new collector.

**Done (2026-11-25)**
- MinVer versions from `v*` tags (`MinVerMinimumMajorMinor` 1.0, so untagged builds are 1.0.0 pre-releases). Nine packages with
  licence, repository and commit (SourceLink), README and symbols; `scripts/check-packages.sh` packs them and fails on missing
  metadata (CI's template job). The CLI is the `hound` .NET tool. `CHANGELOG.md` from Conventional Commits (git-cliff, `cliff.toml`,
  `scripts/changelog.sh`).
- Functional check: `scripts/test-template.sh` installs the template pack from the local feed, creates a collector and runs its 8 tests:
  passed. The rehearsal did the same from the installed packages as a customer.

### s13-04 Release workflow
- [x] Done

**Story:** As the maintainer, I want releases built and published from tags with no stored tokens, so that the supply chain is trustworthy.

**Acceptance criteria**
- `release.yml` on `v*` tags: build, test, pack, publish to NuGet with trusted publishing (OIDC) if available, otherwise a scoped API key in an environment with required reviewers; push container to GHCR; generate SBOM (CycloneDX); attest build provenance; create GitHub release with notes.
- Release environment requires maintainer approval.

**Tests first**
- Dry run on a local `v1.0.0-dry-run` tag to a test feed. (Planned as `v0.0.0-test`; a 0.x version fails the profiles' `requires hound >=0.1` check.)

**Functional check**
- `gh attestation verify` succeeds on the released container image.

**Done (2026-11-25)**
- `.github/workflows/release.yml`: `verify` scans the full history for organisation data (`check-org-data.sh --history`), builds,
  tests, packs into a test feed, installs hound from it, makes the CycloneDX SBOM and notes, and builds, tests and scans the image.
  `publish` waits for the `release` environment's approval, then pushes the multi-arch image with provenance and SBOM, attests the
  packages, pushes them to NuGet only when `NUGET_API_KEY` is set, publishes the docs site and creates the GitHub release.
- NuGet trusted publishing (OIDC) was not available as a pinnable action, so the optional scoped key in the approved environment is used.
- Functional check (dry run): the Release workflow on the sprint PR passed `verify` on `v1.0.0-dry-run`. `gh attestation verify` on the
  released image is checked when v1.0.0 is published (s13-06).

### s13-05 Documentation site
- [x] Done

**Acceptance criteria**
- MkDocs Material (or DocFX) site on GitHub Pages from `docs/`: getting started, deploy, collectors, writing a collector, adopt, operate, govern, training, architecture.
- Built in CI on every PR; published on release.

**Tests first**
- Site build in strict mode (warnings fail).

**Functional check**
- Site reachable; quick start followed on a clean VM by a reviewer.

**Done (2026-11-25)**
- MkDocs Material (`mkdocs.yml`, pinned with hashes in `tools/docs/requirements.txt`), strict build in CI's docs job (`make docs`), a hook
  that links files outside `docs/` to GitHub. New pages: `docs/index.md`, `docs/getting-started.md`, `docs/collectors/README.md`.
  Published to the `gh-pages` branch by the release.
- Carried: the site is reachable once v1.0.0 is published and Pages serves `gh-pages`; a reviewer following the quick start on a clean
  VM needs a person.

### s13-06 Release readiness review
- [ ] Done

**Acceptance criteria**
- Zero open high or critical code scanning, secret scanning or Dependabot alerts.
- OpenSSF Scorecard reviewed; findings fixed or recorded as issues.
- Org-data check passes on the full history (`git log -p` scan), not just the diff.
- README badges: CI, CodeQL, Scorecard, licence, latest release.
- **Release 1 is version 1.0.0:** the annotated git tag `v1.0.0` on `main`, and a published (not draft or pre-release) GitHub release named `v1.0.0`, made by `release.yml` (s13-04), with release notes from `CHANGELOG.md`, the runner image `ghcr.io/tnbozman/evidence-hound:1.0.0`, the CLI and template packages, the SBOM and the provenance attestations.

**Tests first**
- None (review).

**Functional check**
- Tag `v1.0.0` (`git tag -a v1.0.0 -m "Release 1"` and push it), approve the release environment, then check that GitHub shows release `v1.0.0` as the latest release with its assets, and that `ghcr.io/tnbozman/evidence-hound:1.0.0` pulls.

### s13-07 Rehearse trial, onboarding and sustaining GRC (standing, every sprint)
- [x] Done

As [s12-09](s12-e8-mapping-and-adoption.md#s12-09-rehearse-trial-onboarding-and-sustaining-grc-standing-every-sprint), against the release candidate: follow the install, trial, onboarding and upkeep guides from the release notes and the docs site as a new user would.

**Rehearsal log (2026-11-25)**

A subagent, as the agent of a small defence-industry Cyber GRC team, installed the release candidate as a customer would
(`MinVerVersionOverride=1.0.0-rc.1 scripts/check-packages.sh`, then `dotnet tool install` and `dotnet new install` from that folder:
`hound --version` 1.0.0-rc.1), then followed getting started, guided setup and onboarding with the skills, manual evidence and
running, the AI governance walkthrough offline, and this sprint's deploy, release and collector guides. CISO Assistant and Docker
could not run in the session. It never approved anything for a person.

| # | Where | Problem | Fix |
| --- | --- | --- | --- |
| 1 | Getting started | `dotnet tool install --global EvidenceHound.Cli --add-source <folder>` fails without `--version` | Install from the release assets with the version |
| 2 | Write a collector | The new project's `dotnet test` cannot restore the SDK packages before they are on nuget.org | `dotnet restore` with both sources |
| 3 | First deployment, smaller route | Copying the template's `access.yaml` over `hound init`'s broke the evidence plan's GRC team | Keep init's `access.yaml` |
| 4 | Reference deployment and template | Runner image `:dev` and `EVIDENCE_HOUND_REF: main` under "pin a release" | Pinned to 1.0.0; a test keeps the pins in step; releasing.md says to move them |
| 5 | Getting started, `hound init` | Two repo routes, and no `git init` anywhere | One choice of route; `git init` in the guide, init's README and its output |
| 6 | Onboarding guide | `hound onboard environment --no-model` (removed option) then a prompt an agent hangs on | Says when it asks nothing |
| 7 | Guided setup, `hound init`'s config comment | `hound collectors` needs a subcommand | `hound collectors list` |
| 8 | `hound manual upload` | A successful unattested upload printed `error:` | Logged as info |
| 9 | `hound add control --systems` | Took names only, while other commands take ref IDs | Takes either, writes the name |
| 10 | `restore-test.sh` | A decrypt or restore failure exited before dropping a result | Records the step and drops a failed result |
| 11 | Backups guide | `/root/restore` not created; cron comment said collectors run at 02:00 | Folder created; times aligned with the 03:00 default |
| 12 | Write a collector | Said the contract kit checks read-only and `unable_to_assess` | Lists the kit's real contract |
| 13 | AI governance walkthrough | Evidence folder is the run time to the millisecond, not the run ID | Doc says so |
| 14 | Index, getting started, trial | Three different trial times; "Connect a system" linked the M365 page | Aligned; links the collectors index |

All 14 were fixed in this sprint (`fix(s13-07)` and `docs(s13-07)` commits, red then green for code).

Not proven here (CI, a VM or a real tenant): `make trial`, the Compose steps of the first deployment, `scope plan`/`apply` and real runs,
the backup and restore scripts on a host (CI's smoke covers them), hardening and update steps, and `hound run` as a non-root user with
the default evidence root.

### s13-08 Improve: quality and flow (standing, about 15% of each sprint)
- [x] Done

As [s12-10](s12-e8-mapping-and-adoption.md#s12-10-improve-quality-and-flow-standing-about-15-of-each-sprint), with the legacy-file cleanup done before the release is tagged.

**Done (2026-11-25)**
- Triage of s12's Discovered work and carried items:
  - Skills for migration, reporting and prime questionnaires: done (`agent/skills/move-registers-from-spreadsheets.md`,
    `prepare-the-reports.md`, `answer-a-prime-questionnaire.md`; fifteen skills).
  - The crosswalk's error names `--offline`; the `drop_folder` GUID secret false positive; `run --dry-run` warns when it read no
    CISO Assistant state; parent node titles in snapshots, so reports show names instead of codes: all fixed (red, green).
  - Placeholder AI policy in the example AI6 audit (from s11): done (`documents/ai-policy.pdf`, listed for AI6-1.4 and AI6-3.1).
  - `hound ssp build` with an audit series (from s11): it read rows under the series' ref_id, which has none. Fixed: it reads, names
    and downloads the period open today.
  - The board report reading the committee's escalations: stays in s15 (incident response and governance).
  - Crosswalk from a loaded library (needs CISO Assistant to list requirement links) and the committee report's monthly success rate
    (needs run history): stay as discovered work.
- Legacy cleanup before the tag: stale Veeam references dropped from the plan and README; `docs/plan.md` brought up to date; ShellCheck
  finding in `scripts/ca-apply-scope.sh` fixed. No unused files were found under `src/`, `scripts/` or `examples/`.
- Quality: Markdown lint and a link checker now run on every PR, the docs site builds strictly, and the org-data check can scan the
  full history.

## Discovered work

- s13-04: nuget.org trusted publishing (OIDC) would remove the optional `NUGET_API_KEY`. Revisit when an official action can be pinned.
- s13-07: `hound run` as a non-root laptop user with the default evidence root `/var/lib/evidence-hound/evidence` is not proven; check
  in the next rehearsal on a machine with a normal user.
- s13-07: the reference deployment pins (`HOUND_RUNNER_IMAGE`, `EVIDENCE_HOUND_REF`) are moved by hand at each release; a release script
  could stamp them.

## Sprint review
