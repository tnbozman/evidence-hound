# Engineering review, November 2026

A principal engineer's review of Evidence Hound after s18 and before release 3 (s19-03). It asks one question of every part: is this the
right code for a small team to understand, change and test for years? Less code is welcome where it gives the same behaviour, but
the aim is code whose next change is obvious.

Measured with `scripts/code-measures.sh` on 2026-11-26, at the start of s19: 47,051 lines of production C# and 43,367 lines of tests,
2,195 unit tests in about 30 seconds, no analyser warnings.

## What is right, and stays

- **Dependencies point inwards.** `EvidenceHound.Core` references nothing. Collectors reference only
  `EvidenceHound.Collectors.Abstractions`. `CisoAssistant`, `Core.Yaml` and `Onboarding` reference only the core. The only I/O in the
  core is the `IClock` port and a publisher that takes its time from the run.
- **Results, not exceptions.** Expected failures return `Result<T>` with a code; the CLI's global handler turns the rest into exit codes.
- **The contract with CISO Assistant is tested at every level**: contract tests against recorded responses, a request budget for every
  read, and functional tests against a real instance in CI.
- **The agent kit is checked against the CLI** (`AgentFilesTests`, `AgentCommandTests`), so guidance cannot drift from behaviour.

## Findings

Each finding has its cost (what it makes hard), the fix, an estimate and a priority. P1 findings are fixed in s19-04 to s19-06; the
rest are under "Discovered work" in the sprint file.

### E1 (P1) Commands share helpers by reaching into each other

**Cost.** The CLI has no shared command infrastructure, so commands borrow it from whichever command wrote it first:
`ConfigCommands.LoadAsync` is called from 19 places, `OnboardAgentCommand.Indented` (the JSON settings) from 17,
`ScopeCommands.ValidateAsync` (load and check the scope) from 15 and `PublishCommand.ConnectionAsync` (connect to CISO Assistant) from
13. A change to how one command works can break another, and a new contributor cannot find the shared code by its name.

**Fix.** Move each helper to a class named for what it does: `CommandJson` (settings and writing `--json` answers), `RepoScope`
(load and check a repo's scope), `CisoAssistantConnection` (resolve URL and token and connect), `DeploymentConfig` loading where config
is loaded. Behaviour does not change. About half a day.

### E2 (P1) Every command declares the common options itself

**Cost.** `--repo` is declared 14 times and `--json` 17 times, each with its own wording (`"The config repo to work in"`, `"The config
repo"`, `"The compliance repo"`), so help reads inconsistently, and a change to a default means 14 edits.

**Fix.** `CommonOptions.Repo()`, `CommonOptions.Json()` and `CommonOptions.Config()`, one description each. The command-reference
skill regenerates from `--help`, so the agent kit follows. About two hours.

### E3 (P1) The largest command files mix several commands' work

**Cost.** `ScopeCommands.cs` (851 lines) builds the `scope` command tree and also holds scope loading and validation for the whole CLI,
saved-plan fingerprints, the prepared review plan, exports, new-audit drafting and reporting obligations. `OnboardSystemCommand.cs` (767)
and `ReviewCommand.cs` (551) are similar. Reading one behaviour means scrolling past five others.

**Fix.** One file per subcommand where the subcommand has logic of its own (`scope plan`, `scope apply`, `scope export`,
`scope validate`), with the shared parts moved out by E1. No behaviour change, checked by golden files of the `--json` output of
`hound next`, `hound status`, `hound review pending` and `hound scope plan`, written first. About a day.

### E4 (P1) `ScopeStateReader` reads every kind of object in one class

**Cost.** 886 lines: folders, users, frameworks, audits, evidence, risks, exceptions, BIA, metrics, third parties and settings are read
and turned into `ActualObject`s in one class. Adding a kind touches it in three places, and its test file is 706 lines.

**Fix.** One small reader per kind of object behind a shared `Reading` (the list cache from s18-04), composed by `ScopeStateReader`.
`RequestBudgetTests` must hold or shrink. `ScopeLoader` (699 lines) is split the same way. About a day; s19-05.

### E5 (P1) Tests repeat their setup

**Cost.** 24 test classes declare their own `RunAsync` around `HoundCli.RunAsync`, 111 tests make their own temporary folder, and
`ScopeTestInstance` repeats helpers of `ScopePlanCommandTests` (s18-D5). A change to how the CLI is invoked in tests means editing
dozens of files.

**Fix.** One `Hound` test driver (args, input, settings, terminal, returns exit, output and error) and one `TempRepo` that copies the
example repo and cleans up, shared by the CLI tests; fold the scope test instance helpers together. No test is deleted without one that
covers the same behaviour. About a day; s19-06.

### E6 (P2) `HoundCli.RunAsync` has 11 overloads

**Cost.** Every way a test or host wanted to call the CLI added an overload, so the composition root's entry point is 80 lines of
forwarding, and it is not clear which one production uses.

**Fix.** One `RunAsync(args, HoundIo io, HoundOptions? options, CancellationToken)` and `RunFromConsoleAsync`, with the test driver of
E5 building the options. Part of E5.

### E7 (P2) `Runtime` references every built-in collector

**Cost.** `EvidenceHound.Runtime` (runner, scheduler, config) references the Microsoft 365, manual, REST and scan collectors to build
the registry. The runner therefore cannot be tested or packaged without them, and adding a collector touches the runtime.

**Fix.** Register the built-in collectors in the CLI's composition root, where the concrete types belong. About two hours.

### E8 (P2) Retired options are still parsed

**Cost.** `Retired` adds hidden `--no-model`, `--show-requests` and `--config` options to six commands so pre-s09 scripts keep
working. Release 3 is the first release the maintainer treats as the product, and no script outside this repo used the built-in model.

**Fix.** Remove them in release 3, with a changelog line. Goes to the maintainer in the s19-02 list.

### E9 (P3) Two homes for agent guidance

**Cost.** `agent/skills/` (workflow skills rendered by `hound agent setup`) and `skills/` (reference skills embedded in hound and
included by the workflow skills) both hold Markdown for agents. A contributor has to learn which is which.

**Fix.** Move `skills/` to `agent/reference/`. It changes the Dockerfile, the embedded resource path and a compliance repo's own
`skills/` override folder, which people may rely on, so it waits for a release after 3 unless the maintainer wants it now.

### E10 (P3) Two older deployment configs beside the example compliance repo

**Cost.** `examples/example-pty-ltd.yaml` and `examples/example-essential-eight.yaml` predate `examples/compliance-repo/hound.yaml`.
Thirty tests and guides read them, so the example deployment lives in three files.

**Fix.** Fold them into the compliance repo once E5's shared test repo exists. Later sprint.

## Not changed, on purpose

- **One state read per audit per collector publish** (s18-D4): a shared read would report drift that is not there.
- **System.CommandLine and the one global error handler**: the right shape; the findings above are about what sits around it.
- **The dev servers**: about 2,100 lines in four projects, but they are what lets the trial and functional tests run without a real
  tenant, which the maintainer asked for.
