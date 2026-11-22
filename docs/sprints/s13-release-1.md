# s13 Release 1

Status: Planned

**Goal:** the deployment from s07 gains tested backups and a hardening and update guide, then a tagged, signed, documented first release that others can install and extend.

Tasks s13-01 and s13-02 were s08-04 and s08-05 before the release 1 reorder.

### s13-01 Backup and restore
- [ ] Done

**Story:** As an operator, I want automated backups and a tested restore, so that the system of record survives failure and the restore test becomes evidence.

**Acceptance criteria**
- `deploy/compose/backup.sh`: `pg_dump`, CISO Assistant attachments volume, encrypted with age to an off-host target.
- `deploy/compose/restore-test.sh`: restores the latest backup into a scratch stack, checks the health endpoint and an audit count, and drops a result file into a manual-collector source so the restore test is recorded as evidence.

**Tests first**
- CI smoke job: back up the smoke stack, restore into a second project, compare audit counts.

**Functional check**
- Run both scripts on the VM; the restore test appears as evidence in CISO Assistant.

### s13-02 Hardening and update guide
- [ ] Done

**Acceptance criteria**
- `docs/deploy/hardening.md`: host patching, firewall, disk encryption, Docker daemon settings, log forwarding, certificate and uptime monitoring, MFA for all CISO Assistant users, service accounts per integration.
- `docs/deploy/updates.md`: Dependabot or Renovate for image tags, monthly window, backup before upgrade, rollback steps.

**Tests first**
- Markdown lint and link check in CI.

**Functional check**
- A reviewer applies the guide to the test VM and records the date.

### s13-03 Packages and versioning
- [ ] Done

**Story:** As a collector author, I want the abstractions and test kit on NuGet, so that I can build collectors outside this repo.

**Acceptance criteria**
- Versioning from git tags (MinVer); semantic versioning; `CHANGELOG.md` generated from Conventional Commits.
- Packages: `EvidenceHound.Core`, `EvidenceHound.Collectors.Abstractions`, `EvidenceHound.Collectors.Testing`, the template pack, and each reference collector.
- Package metadata: licence expression, repository URL, README, SourceLink, symbols.

**Tests first**
- CI job packs all packages and fails on missing metadata.

**Functional check**
- Install the template pack from a local feed and build a new collector.

### s13-04 Release workflow
- [ ] Done

**Story:** As the maintainer, I want releases built and published from tags with no stored tokens, so that the supply chain is trustworthy.

**Acceptance criteria**
- `release.yml` on `v*` tags: build, test, pack, publish to NuGet with trusted publishing (OIDC) if available, otherwise a scoped API key in an environment with required reviewers; push container to GHCR; generate SBOM (CycloneDX); attest build provenance; create GitHub release with notes.
- Release environment requires maintainer approval.

**Tests first**
- Dry run on a `v0.0.0-test` tag to a test feed.

**Functional check**
- `gh attestation verify` succeeds on the released container image.

### s13-05 Documentation site
- [ ] Done

**Acceptance criteria**
- MkDocs Material (or DocFX) site on GitHub Pages from `docs/`: getting started, deploy, collectors, writing a collector, adopt, operate, govern, training, architecture.
- Built in CI on every PR; published on release.

**Tests first**
- Site build in strict mode (warnings fail).

**Functional check**
- Site reachable; quick start followed on a clean VM by a reviewer.

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

## Discovered work

## Sprint review
