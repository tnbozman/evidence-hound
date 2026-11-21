# Same short commands locally and in CI. Run `make` or `make help` for the list.
SHELL := /usr/bin/env bash
.DEFAULT_GOAL := help

CONFIGURATION ?= Debug
RESULTS_DIR ?= test-results
ARGS ?=
CA_COMPOSE := dev/ciso-assistant/compose.yml
SHELL_SCRIPTS := $(wildcard scripts/*.sh scripts/lib/*.sh .devcontainer/*.sh dev/ciso-assistant/seed/*.sh) .githooks/pre-commit

.PHONY: help restore build test test-functional test-scripts coverage lint lint-dotnet lint-shell format run hooks ca-up ca-up-kafka ca-down

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

test-scripts: ## Shell script tests (bats)
	bats scripts/tests

lint: lint-dotnet lint-shell ## All linters (analysers also run on every build)

lint-dotnet: restore ## Formatting and code-style check
	dotnet format --verify-no-changes --no-restore

lint-shell: ## ShellCheck on every script and the git hook
	shellcheck -x $(SHELL_SCRIPTS)

format: ## Apply formatting fixes
	dotnet format

run: ## Run the CLI, for example: make run ARGS="collect --dry-run --config examples/example-pty-ltd.yaml"
	dotnet run --project src/EvidenceHound.Cli -c $(CONFIGURATION) -- $(ARGS)

hooks: ## Install the git pre-commit hook (format check and gitleaks)
	git config core.hooksPath .githooks

ca-up: ## Start and seed a local CISO Assistant (token in .dev/ca.env)
	docker compose -f $(CA_COMPOSE) up -d --wait
	scripts/ca-seed.sh

ca-up-kafka: ca-up ## Also start Redpanda and the CISO Assistant dispatcher (compose profile kafka)
	set -a && . ./.dev/ca.env && set +a && docker compose -f $(CA_COMPOSE) --profile kafka up -d --build

ca-down: ## Stop the local CISO Assistant (add ARGS=-v to delete its data)
	docker compose -f $(CA_COMPOSE) --profile kafka down $(ARGS)
