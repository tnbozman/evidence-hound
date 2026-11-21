# Same short commands locally and in CI. Run `make help` for the list.
SHELL := /usr/bin/env bash
.DEFAULT_GOAL := help
CONFIGURATION ?= Debug

.PHONY: help restore build test

help: ## List targets
	@grep -E '^[a-zA-Z_-]+:.*?## ' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  %-16s %s\n", $$1, $$2}'

restore: ## Restore NuGet packages (locked mode)
	dotnet restore --locked-mode

build: restore ## Build the solution
	dotnet build --no-restore -c $(CONFIGURATION)

test: build ## Unit and contract tests
	dotnet test --no-build -c $(CONFIGURATION) --filter "Category!=Functional"
