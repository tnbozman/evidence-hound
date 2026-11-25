#!/usr/bin/env bash
# Move every deployment pin to a release: the runner image in the reference Compose stack and the template's deploy.env, the
# Evidence Hound ref in the template's compliance workflow, and the image in the first deployment tutorial. Run it in the release
# commit, beside `make changelog VERSION=x.y.z` (docs/releasing.md). A unit test checks the pins agree.
#
# Usage: scripts/stamp-release.sh x.y.z [repo root]
set -euo pipefail

version="${1:-}"
root="${2:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
image="ghcr.io/tnbozman/evidence-hound"

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "stamp-release: usage: stamp-release.sh x.y.z [repo root] (got '$version')" >&2
  exit 2
fi

envs=(deploy/compose/.env.example examples/compliance-repo/deploy/deploy.env)
workflow=examples/compliance-repo/.github/workflows/compliance.yml
tutorial=docs/tutorials/first-deployment.md

for file in "${envs[@]}"; do
  sed -i -E "s#^HOUND_RUNNER_IMAGE=${image}:[0-9]+\.[0-9]+\.[0-9]+\$#HOUND_RUNNER_IMAGE=${image}:${version}#" "$root/$file"
done
sed -i -E "s#^(  EVIDENCE_HOUND_REF: )v[0-9]+\.[0-9]+\.[0-9]+\$#\1v${version}#" "$root/$workflow"
sed -i -E "s#${image}:[0-9]+\.[0-9]+\.[0-9]+#${image}:${version}#g" "$root/$tutorial"

echo "stamp-release: moved the pins in 4 files to $version: ${envs[*]} $workflow $tutorial"
