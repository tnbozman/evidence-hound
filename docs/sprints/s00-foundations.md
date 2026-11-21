# s00 Foundations

Status: In progress

**Goal:** a public repo where a contributor opens the dev container, runs one command and sees a green build, with CI, security scanning and the sprint workflow working end to end.

**Note:** this sprint is bootstrapped by hand (there is no Start sprint workflow until s00-04 lands). Create `sprint/s00-foundations` manually.

### s00-01 Solution skeleton
- [x] Done

**Story:** As a developer, I want a standard .NET solution layout, so that every project builds, tests and versions the same way.

**Acceptance criteria**
- `global.json` pins the .NET 10 SDK feature band.
- `EvidenceHound.slnx` contains `src/EvidenceHound.Core`, `src/EvidenceHound.Cli` and `tests/EvidenceHound.Core.Tests`.
- `Directory.Build.props` sets: `Nullable=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`, `ImplicitUsings=enable`, `Deterministic=true`, `ContinuousIntegrationBuild` in CI.
- `Directory.Packages.props` enables Central Package Management; no `Version` attributes in project files.
- `.editorconfig` with C# style rules, file-scoped namespaces, `var` preferences, and analyser severities.
- `make build` and `make test` succeed on a clean clone.

**Tests first**
- `SmokeTests.Core_assembly_loads` (one trivial test so the pipeline is proven red then green).

**Functional check**
- `make build && make test` in the dev container: build succeeds, 1 test passes.

**Result (2026-11-21)**
- Fresh `git clone` then `make build test`: `Build succeeded`, `total: 1, failed: 0, succeeded: 1`.
- Tests run on Microsoft.Testing.Platform (xUnit v3 4.x no longer supports VSTest on the .NET 10 SDK), opted in through `global.json`. CI test steps use the MTP options (`--filter-not-trait`, `--report-trx`, `--coverage`).

### s00-02 Dev container
- [ ] Done

**Story:** As a developer who keeps no tooling on their own machine, I want a dev container, so that build, test, debug and run all work inside it.

**Acceptance criteria**
- `.devcontainer/devcontainer.json` (drafted) opens in VS Code and Codespaces.
- Contains: .NET 10 SDK, Docker-in-Docker (for Testcontainers and local CISO Assistant), GitHub CLI, Node LTS, Claude Code CLI.
- Extensions: C# Dev Kit, GitHub Actions, GitHub Pull Requests, Docker, YAML, EditorConfig, markdownlint, Coverage Gutters, Claude Code.
- `.vscode/launch.json` debugs the CLI with `collect --dry-run` against the example config; `.vscode/tasks.json` exposes build, test, functional test, CISO Assistant up and down.
- `postCreateCommand` restores packages and installs the pre-commit hook.

**Tests first**
- None (infrastructure). The functional check is the test.

**Functional check**
- Rebuild container from scratch; run task "test"; set a breakpoint in `Program.cs` and hit it with the "Debug CLI (dry run)" launch configuration.

### s00-03 CI pipeline
- [ ] Done

**Story:** As a maintainer, I want every push and PR checked automatically, so that problems surface before review.

**Acceptance criteria**
- `.github/workflows/ci.yml` (drafted) runs on push to `sprint/**` and on PRs to `main`: restore, format check (`dotnet format --verify-no-changes`), build, unit tests with coverage, test report uploaded.
- An `org-data` job fails if any term from the `ORG_DENYLIST` Actions secret appears in the diff or tree.
- A `secrets` job runs gitleaks on the diff.
- All actions are pinned to a full commit SHA; workflow `permissions` default to `contents: read`.

**Tests first**
- Add a deliberately failing unit test on a scratch commit, confirm CI fails, then revert (record the run URLs in this file).

**Functional check**
- Push a commit containing a fake denylisted word on a scratch branch: `org-data` job fails. Remove it: job passes.

### s00-04 Start sprint automation
- [ ] Done

**Story:** As the product owner, I want to start a sprint with one action, so that the branch, draft PR and checklist are created consistently.

**Acceptance criteria**
- `.github/workflows/start-sprint.yml` (drafted) runs from the Actions tab with input `sprint` (for example `s01`).
- It fails clearly if no `docs/sprints/<sprint>-*.md` exists, if another sprint is `In progress`, or if the branch already exists.
- It creates `sprint/<file-slug>` from `main`, sets `Status: In progress`, commits, pushes and opens a draft PR titled `Sprint sNN: <title>` with every task as a checkbox.
- `scripts/start-sprint.sh` does the same locally using `git` and `gh`.

**Tests first**
- `scripts/tests/start-sprint.bats`: extracts title, slug and task list from a fixture sprint file; rejects a missing sprint; rejects when another sprint is in progress. (Run with `bats` in the dev container.)

**Functional check**
- Run the workflow for `s01`; branch and draft PR exist; PR body lists all s01 tasks. Close the PR and delete the branch afterwards if s01 is not starting yet.

### s00-05 Repository security settings
- [ ] Done

**Story:** As the maintainer of a public security tool, I want GitHub's advanced security features on, so that the repo itself meets the standard it promotes.

**Acceptance criteria**
- Enabled: secret scanning, push protection, private vulnerability reporting, Dependabot alerts and security updates, CodeQL default setup (C#, GitHub Actions).
- `.github/dependabot.yml` (drafted) covers NuGet, GitHub Actions, Docker and dev containers weekly.
- Branch ruleset on `main`: PR required, 1 approval, required checks (`build-test`, `org-data`, `secrets`, CodeQL), no force push, no deletion.
- `ORG_DENYLIST` secret set by the maintainer (never committed).
- `SECURITY.md`, `CONTRIBUTING.md` and `CODE_OF_CONDUCT.md` present; `LICENSE` is Apache-2.0.
- Actions setting "Allow GitHub Actions to create and approve pull requests" is on (needed by s00-04).

**Tests first**
- None (settings). Record a screenshot-free checklist of each setting with the date in this file.

**Functional check**
- Attempt to push a fake token in the format of a GitHub PAT on a scratch branch: push is blocked.

### s00-06 Walking skeleton CLI
- [x] Done

**Story:** As an operator, I want a `hound` command with `--version` and `collect --dry-run`, so that the end-to-end shape exists before any real logic.

**Acceptance criteria**
- `src/EvidenceHound.Cli` builds an executable named `hound` (`AssemblyName`) and uses `System.CommandLine` and `Microsoft.Extensions.Hosting` for DI, configuration and logging.
- `hound --version` prints the informational version.
- `hound collect --dry-run --config <file>` loads the config file, logs "0 collectors configured" and exits 0.
- Unknown arguments exit with code 2 and a helpful message.

**Tests first**
- `CliTests.Version_prints_semantic_version`
- `CliTests.Collect_dry_run_with_empty_config_exits_zero`
- `CliTests.Unknown_option_exits_two`

**Functional check**
- `make run ARGS="collect --dry-run --config examples/example-pty-ltd.yaml"` prints the expected log line.

**Result (2026-11-21)**
- `make run ARGS="collect --dry-run --config examples/example-pty-ltd.yaml"` logs `0 collectors configured`, exit 0.
- `make run ARGS="--version"` prints `0.1.0+<commit>`.
- `make run ARGS="collect --bogus"` prints the unknown argument, the missing `--config` and `Run 'hound --help' for usage.`, exit 2.
- A missing config file exits 3 (configuration error), matching the exit codes planned in s03.

### s00-07 Makefile and pre-commit
- [ ] Done

**Story:** As a developer, I want the same short commands locally and in CI, so that "works on my container" means "works in CI".

**Acceptance criteria**
- `Makefile` targets: `build`, `test`, `test-functional`, `lint`, `format`, `run`, `ca-up`, `ca-down`, `coverage`.
- CI calls the same targets.
- A git pre-commit hook runs `dotnet format --verify-no-changes` and gitleaks on staged files.

**Tests first**
- None. Functional check covers it.

**Functional check**
- Stage a badly formatted file: commit is refused with the format message.

## Discovered work

- s03-01: replace the walking-skeleton `ConfigFile` and `HoundConfig` in `src/EvidenceHound.Cli` with the full config schema in `EvidenceHound.Runtime`, and return `Result<T>` (s01-01) instead of the local `ConfigLoad` union.
- The `functional` CI job passes `--ignore-exit-code 8` (no tests ran) because there are no functional tests yet. Remove it when s02 adds the first functional test.

## Sprint review
