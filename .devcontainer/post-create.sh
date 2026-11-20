#!/usr/bin/env bash
# Runs once after the dev container is created.
set -euo pipefail

sudo apt-get update -qq
sudo apt-get install -y -qq bats shellcheck make

# gitleaks for the pre-commit hook (pinned version; Dependabot does not track this, review quarterly).
GITLEAKS_VERSION="8.21.2"
curl -sSfL "https://github.com/gitleaks/gitleaks/releases/download/v${GITLEAKS_VERSION}/gitleaks_${GITLEAKS_VERSION}_linux_x64.tar.gz" \
  | sudo tar -xz -C /usr/local/bin gitleaks

# Pre-commit hook: format check and secret scan on staged changes.
if [ -d .git ]; then
  cat > .git/hooks/pre-commit <<'HOOK'
#!/usr/bin/env bash
set -e
if [ -f global.json ]; then
  dotnet format --verify-no-changes --no-restore || { echo "Run 'make format' and re-stage."; exit 1; }
fi
gitleaks protect --staged --redact --no-banner
HOOK
  chmod +x .git/hooks/pre-commit
fi

if [ -f global.json ]; then
  dotnet restore
fi

echo "Dev container ready. Try: make test"
