# Same short commands locally and in CI. Run `make` or `make help` for the list.
SHELL := /usr/bin/env bash
.DEFAULT_GOAL := help

CONFIGURATION ?= Debug
RESULTS_DIR ?= test-results
ARGS ?=
HOUND_IMAGE ?= evidence-hound-runner:test
CA_COMPOSE := dev/ciso-assistant/compose.yml
CA_ENV := HOUND_CA_SECRET_KEY="$$(scripts/ca-secret-key.sh)"
SHELL_SCRIPTS := $(wildcard scripts/*.sh scripts/lib/*.sh deploy/compose/*.sh .devcontainer/*.sh dev/ciso-assistant/seed/*.sh) .githooks/pre-commit

.PHONY: help docs docs-serve lint-docs test-docs tutorials-audio test-packages changelog restore build test test-functional test-profiles test-template test-scripts test-terraform coverage image test-image test-compose compose-smoke lint lint-dotnet lint-shell format run m365-dev sophos-dev iac-dev scan-dev hooks ca-up ca-down trial trial-clean

help: ## List targets
	@grep -E '^[a-zA-Z_-]+:.*?## ' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  %-16s %s\n", $$1, $$2}'

restore: ## Restore NuGet packages (locked mode)
	dotnet restore --locked-mode

build: restore ## Build the solution
	dotnet build --no-restore -c $(CONFIGURATION)

test: build ## Unit and contract tests
	dotnet test --no-build -c $(CONFIGURATION) -p:UnitTestRun=true --filter-not-trait "Category=Functional"

coverage: build ## Unit and contract tests with TRX and Cobertura coverage in $(RESULTS_DIR)
	dotnet test --no-build -c $(CONFIGURATION) -p:UnitTestRun=true --results-directory $(RESULTS_DIR) \
	  --filter-not-trait "Category=Functional" \
	  --report-trx --coverage --coverage-output-format cobertura

test-functional: build ## Functional tests (Testcontainers starts CISO Assistant)
	dotnet test --no-build -c $(CONFIGURATION) --project tests/EvidenceHound.Functional.Tests \
	  --results-directory $(RESULTS_DIR) --filter-trait "Category=Functional" --report-trx

test-profiles: build ## Run every library profile's offline tests with hound profile test
	CONFIGURATION=$(CONFIGURATION) scripts/test-profiles.sh

test-template: ## Generate a collector from templates/collector, then build and test it
	scripts/test-template.sh

test-packages: ## Pack every NuGet package and check its metadata (licence, repository, README, symbols)
	scripts/check-packages.sh

changelog: ## Regenerate CHANGELOG.md from Conventional Commits, naming unreleased commits VERSION (for example VERSION=1.0.0)
	scripts/changelog.sh $(VERSION)

test-scripts: ## Shell script tests (bats)
	bats scripts/tests

TF_EXAMPLE ?= examples/onboarding-tutorial/infra/oci

test-terraform: ## The tutorial's Terraform control tests, with mock providers (no cloud account)
	terraform -chdir=$(TF_EXAMPLE) fmt -check -recursive
	terraform -chdir=$(TF_EXAMPLE) init -backend=false -input=false
	terraform -chdir=$(TF_EXAMPLE) test

image: ## Build the collector runner image (HOUND_IMAGE, default evidence-hound-runner:test)
	docker build -f src/EvidenceHound.Cli/Dockerfile -t $(HOUND_IMAGE) .

test-compose: ## Static checks of the reference Compose stack (deploy/compose)
	bats tests/compose

compose-smoke: ## Bring the reference stack up, wait for health, run the runner once with the fake collector, tear down
	deploy/compose/smoke-test.sh

test-image: image ## Build the runner image, then check it starts, runs as non-root, has no shell and runs read-only
	HOUND_IMAGE=$(HOUND_IMAGE) bats tests/image

lint: lint-dotnet lint-shell lint-docs ## All linters (analysers also run on every build)

lint-dotnet: restore ## Formatting and code-style check
	dotnet format --verify-no-changes --no-restore

lint-docs: ## Markdown lint (pinned markdownlint-cli2) and relative link check of the published docs
	npm ci --prefix tools/markdown --no-audit --no-fund --silent
	tools/markdown/node_modules/.bin/markdownlint-cli2
	python3 -I scripts/check-links.py .

DOCS_VENV ?= .venv-docs
TUTORIALS_VENV ?= .venv-tutorials

docs: ## Build the documentation site in strict mode (warnings fail) into site/
	python3 -m venv $(DOCS_VENV)
	$(DOCS_VENV)/bin/pip install -q --require-hashes -r tools/docs/requirements.txt
	$(DOCS_VENV)/bin/mkdocs build --strict

docs-serve: docs ## Serve the documentation site on http://localhost:8001 with live reload
	$(DOCS_VENV)/bin/mkdocs serve -a localhost:8001

test-docs: ## Tests of the docs tools (link checker, tutorial checks and narration, the tutorial player)
	bats tests/docs
	python3 -I tests/docs/test_tutorials.py
	node --test tests/docs/player.test.mjs
	python3 -I scripts/tutorials.py check

tutorials-audio: ## Speak the interactive tutorials' narration with Kokoro on this machine (needs ffmpeg; the model is cached outside the repo)
	python3 -m venv $(TUTORIALS_VENV)
	$(TUTORIALS_VENV)/bin/pip install -q --require-hashes -r tools/tutorials/requirements.txt
	$(TUTORIALS_VENV)/bin/python -I scripts/tutorials.py audio

lint-shell: ## ShellCheck on every script and the git hook
	shellcheck -x $(SHELL_SCRIPTS)

format: ## Apply formatting fixes
	dotnet format

run: ## Run the CLI, for example: make run ARGS="collect --dry-run --config examples/example-pty-ltd.yaml"
	dotnet run --project src/EvidenceHound.Cli -c $(CONFIGURATION) -- $(ARGS)

m365-dev: ## Serve the fictional Microsoft 365 tenant on port 5365, for example: make m365-dev ARGS="--variant gaps"
	dotnet run --project src/EvidenceHound.M365DevServer -c $(CONFIGURATION) -- $(ARGS)

sophos-dev: ## Serve the fictional Sophos Central tenant on port 5370, for example: make sophos-dev ARGS="--variant gaps"
	dotnet run --project src/EvidenceHound.SophosDevServer -c $(CONFIGURATION) -- $(ARGS)

iac-dev: ## Serve the fictional IaC pipeline (GitHub Actions) on port 5380, for example: make iac-dev ARGS="--deviation drift"
	dotnet run --project src/EvidenceHound.IacDevServer -c $(CONFIGURATION) -- $(ARGS)

scan-dev: ## Write fictional vulnerability scan exports, one per scope, into .dev/scans, for example: make scan-dev ARGS="--variant gaps"
	dotnet run --project src/EvidenceHound.ScanDev -c $(CONFIGURATION) -- $(ARGS)

hooks: ## Install the git pre-commit hook (format check and gitleaks)
	git config core.hooksPath .githooks

ca-up: ## Start and seed a local CISO Assistant (token in .dev/ca.env)
	$(CA_ENV) docker compose -f $(CA_COMPOSE) up -d --wait
	$(CA_ENV) scripts/ca-seed.sh

ca-down: ## Stop the local CISO Assistant (add ARGS=-v to delete its data)
	$(CA_ENV) docker compose -f $(CA_COMPOSE) down $(ARGS)

trial: ## Interactive local trial: hound in a container next to your CISO Assistant (docs/tutorials/local-trial.md)
	scripts/trial.sh

trial-clean: ## Remove the local trial's workspace (.dev/trial); CISO Assistant keeps running
	rm -rf .dev/trial
