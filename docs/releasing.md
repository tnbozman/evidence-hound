# Releasing

Releases are cut from a `vX.Y.Z` tag on `main` by the release workflow ([`release.yml`](../.github/workflows/release.yml)). Nothing is
published until a maintainer approves the `release` environment, and no long-lived token is needed except an optional NuGet key.

## What a release publishes

| What | Where | Checked by |
| --- | --- | --- |
| Runner image `ghcr.io/tnbozman/evidence-hound:X.Y.Z` (and `X.Y`), amd64 and arm64 | GHCR | image tests and a vulnerability scan before approval; build provenance and an SBOM attestation in the registry |
| `EvidenceHound.Cli` (the `hound` .NET tool), `EvidenceHound.Templates`, `EvidenceHound.Core`, `EvidenceHound.Collectors.*` | GitHub release assets, and NuGet when the key is set | `scripts/check-packages.sh` (licence, repository and commit, README, symbols); hound installed from them as a test feed; build provenance |
| CycloneDX SBOM of the CLI | GitHub release asset | |
| Release notes | GitHub release | generated from the Conventional Commits since the last tag (`scripts/changelog.sh --notes`) |

The version comes from the tag (MinVer). Between releases, builds are pre-releases of the next version, such as `1.0.1-alpha.0.3`.

## Set up once

1. **The release environment.** In the repository's Settings > Environments, create `release` and add the maintainers as required
   reviewers. Allow deployments from tags matching `v*` only.
2. **NuGet (optional).** To publish to nuget.org, create an API key there scoped to *Push* for the `EvidenceHound.*` glob, with an
   expiry, and add it to the `release` environment as the secret `NUGET_API_KEY`. Without it, the packages are attached to the GitHub
   release only. When nuget.org trusted publishing is set up for this repository, the key can be removed.

## Cut a release

1. On the sprint branch, write the changelog and commit it with the release's other changes:
   `make changelog VERSION=1.0.0` (needs git-cliff: `pip install --require-hashes -r tools/changelog/requirements.txt`).
2. Merge the sprint's pull request into `main` when CI is green.
3. Tag the merge commit and push the tag:

   ```console
   $ git switch main && git pull
   $ git tag -a v1.0.0 -m "Release 1"
   $ git push origin v1.0.0
   ```

4. The release workflow's `verify` job builds and checks everything. Then approve the `release` environment in the run.
5. Check the result:

   ```console
   $ gh release view v1.0.0
   $ docker pull ghcr.io/tnbozman/evidence-hound:1.0.0
   $ gh attestation verify oci://ghcr.io/tnbozman/evidence-hound:1.0.0 --owner tnbozman
   ```

A pull request that changes the release workflow, its scripts or the Dockerfile runs `verify` as a dry run on a local `v0.0.0-test`
tag: packages into a local test feed, hound installed from it, the SBOM and notes made, the image built, tested and scanned. Nothing is
pushed.

## If a release goes wrong

Before approval, cancel the run, delete the tag (`git push origin :refs/tags/v1.0.0`), fix, and tag again. After publishing, never move
or reuse a tag: fix forward with the next patch version. A package pushed to NuGet can be unlisted but not deleted.
