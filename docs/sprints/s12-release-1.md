# s12 Release 1

Status: Planned

**Goal:** a tagged, signed, documented first release that others can install and extend.

### s12-01 Packages and versioning
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

### s12-02 Release workflow
- [ ] Done

**Story:** As the maintainer, I want releases built and published from tags with no stored tokens, so that the supply chain is trustworthy.

**Acceptance criteria**
- `release.yml` on `v*` tags: build, test, pack, publish to NuGet with trusted publishing (OIDC) if available, otherwise a scoped API key in an environment with required reviewers; push container to GHCR; generate SBOM (CycloneDX); attest build provenance; create GitHub release with notes.
- Release environment requires maintainer approval.

**Tests first**
- Dry run on a `v0.0.0-test` tag to a test feed.

**Functional check**
- `gh attestation verify` succeeds on the released container image.

### s12-03 Documentation site
- [ ] Done

**Acceptance criteria**
- MkDocs Material (or DocFX) site on GitHub Pages from `docs/`: getting started, deploy, collectors, writing a collector, adopt, operate, govern, training, architecture.
- Built in CI on every PR; published on release.

**Tests first**
- Site build in strict mode (warnings fail).

**Functional check**
- Site reachable; quick start followed on a clean VM by a reviewer.

### s12-04 Release readiness review
- [ ] Done

**Acceptance criteria**
- Zero open high or critical code scanning, secret scanning or Dependabot alerts.
- OpenSSF Scorecard reviewed; findings fixed or recorded as issues.
- Org-data check passes on the full history (`git log -p` scan), not just the diff.
- README badges: CI, CodeQL, Scorecard, licence, latest release.

**Tests first**
- None (review).

**Functional check**
- Tag `v1.0.0`.

## Discovered work

## Sprint review
