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

.PHONY: help restore build test test-functional test-profiles test-template test-scripts coverage image test-image test-compose compose-smoke lint lint-dotnet lint-shell format run m365-dev sophos-dev hooks ca-up ca-up-kafka ca-down trial trial-clean

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

test-scripts: ## Shell script tests (bats)
	bats scripts/tests

image: ## Build the collector runner image (HOUND_IMAGE, default evidence-hound-runner:test)
	docker build -f src/EvidenceHound.Cli/Dockerfile -t $(HOUND_IMAGE) .

test-compose: ## Static checks of the reference Compose stack (deploy/compose)
	bats tests/compose

compose-smoke: ## Bring the reference stack up, wait for health, run the runner once with the fake collector, tear down
	deploy/compose/smoke-test.sh

test-image: image ## Build the runner image, then check it starts, runs as non-root, has no shell and runs read-only
	HOUND_IMAGE=$(HOUND_IMAGE) bats tests/image

lint: lint-dotnet lint-shell ## All linters (analysers also run on every build)

lint-dotnet: restore ## Formatting and code-style check
	dotnet format --verify-no-changes --no-restore

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

hooks: ## Install the git pre-commit hook (format check and gitleaks)
	git config core.hooksPath .githooks

ca-up: ## Start and seed a local CISO Assistant (token in .dev/ca.env)
	$(CA_ENV) docker compose -f $(CA_COMPOSE) up -d --wait
	$(CA_ENV) scripts/ca-seed.sh

ca-up-kafka: ca-up ## Also start Redpanda and the CISO Assistant dispatcher (compose profile kafka)
	set -a && . ./.dev/ca.env && set +a && $(CA_ENV) docker compose -f $(CA_COMPOSE) --profile kafka up -d --build

ca-down: ## Stop the local CISO Assistant (add ARGS=-v to delete its data)
	$(CA_ENV) docker compose -f $(CA_COMPOSE) --profile kafka down $(ARGS)

trial: ## Interactive local trial: hound in a container next to your CISO Assistant (docs/tutorials/local-trial.md)
	scripts/trial.sh

trial-clean: ## Remove the local trial's workspace (.dev/trial); CISO Assistant keeps running
	rm -rf .dev/trial
