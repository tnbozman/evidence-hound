#!/usr/bin/env bash
# Runs every library profile's offline tests with `hound profile test`. Needs a build first (make build).
set -euo pipefail

configuration="${CONFIGURATION:-Debug}"
cd "$(dirname "$0")/.."

for file in profiles/*.yaml; do
  name="$(basename "$file" .yaml)"
  dotnet run --no-build --project src/EvidenceHound.Cli -c "$configuration" -- profile test "$name" --profiles profiles
done
