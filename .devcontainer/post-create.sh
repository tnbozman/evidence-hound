#!/usr/bin/env bash
# Runs once after the dev container is created.
set -euo pipefail

sudo apt-get update -qq
sudo apt-get install -y -qq bats shellcheck make

# gitleaks for the pre-commit hook (pinned version; Dependabot does not track this, review quarterly).
GITLEAKS_VERSION="8.21.2"
curl -sSfL "https://github.com/gitleaks/gitleaks/releases/download/v${GITLEAKS_VERSION}/gitleaks_${GITLEAKS_VERSION}_linux_x64.tar.gz" \
  | sudo tar -xz -C /usr/local/bin gitleaks

# Pre-commit hook (tracked in .githooks): format check and secret scan on staged changes.
make hooks

if [ -f global.json ]; then
  make restore
fi

echo "Dev container ready. Try: make test"
