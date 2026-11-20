# CLAUDE.md

Guidance for Claude Code working in this repository. Read this, `docs/engineering.md` and the current sprint file before writing code.

## Non-negotiable rules

1. **No organisation data, ever.** This repo is public and generic. Never write a real company name, domain, tenant ID, hostname, IP address, user name, audit ID, system inventory or assessment result into code, tests, fixtures, docs, commit messages, issues or PR text. Use the fictional "Example Pty Ltd" and `example.com`. If a task seems to need real data, stop and ask.
2. **No secrets.** Credentials only ever come from environment variables or a secret provider at runtime. Never commit `.env` files, tokens or keys. Push protection and the `org-data` CI check will block you; do not try to work around them.
3. **Strict TDD.** No production code without a failing test that requires it. Follow the cycle in "How to implement a task" exactly.
4. **One task at a time.** Finish, verify and commit one task before starting the next. Do not expand scope; note follow-ups in the sprint file under "Discovered work".
5. **Never weaken a check to get green.** Do not skip, delete or loosen tests, CodeQL queries, analyser rules or the org-data check. Fix the code.

## Where things are

- `docs/sprints/sNN-*.md`: sprint plans. The sprint with `Status: In progress` is the current one.
- `docs/engineering.md`: architecture, SOLID and functional style, testing levels.
- `docs/closed-loop.md`: how to read CI and security feedback.
- `src/`: production code. `tests/`: unit, contract and functional tests.
- `examples/`: fictional example configuration. `deploy/`: reference Docker Compose.

## How to implement a task

1. Read the task in the current sprint file: story, acceptance criteria, tests to write first.
2. **Red.** Write the listed tests (and any others the criteria need). Run them and confirm they fail for the right reason. Commit: `test(sNN-TT): <what> [red]`.
3. **Green.** Write the simplest production code that passes. Run the whole unit suite. Commit: `feat(sNN-TT): <what> [green]`.
4. **Refactor.** Remove duplication, improve names, push side effects to the edges, keep functions pure and small. Tests stay green. Commit: `refactor(sNN-TT): <what>` (skip if nothing to do).
5. **Functional check.** Run the task's "Functional check" (usually a CLI run with `--dry-run` or a functional test against the local CISO Assistant). Paste the command and a short result into the sprint file under the task.
6. Tick the task's checkbox in the sprint file and in the sprint PR description. Push.
7. Run `/check-feedback` (or follow `docs/closed-loop.md`) and fix anything CI or code scanning reports before the next task.

## Commands

The dev container provides these through the `make` targets in the root `Makefile` (to be created in s00):

| Command | Does |
| --- | --- |
| `make test` | Unit and contract tests |
| `make test-functional` | Functional tests (Testcontainers starts CISO Assistant) |
| `make lint` | Formatter check, analysers or linters, type checks |
| `make ca-up` / `make ca-down` | Start or stop a local CISO Assistant for manual checks |
| `make run ARGS="collect --dry-run"` | Run the CLI |

## Definition of done for a task

- Acceptance criteria met and covered by tests written first.
- Unit suite green, lint clean, no new analyser warnings.
- Functional check run and recorded.
- No new CodeQL, secret scanning or Dependabot alerts on the branch.
- Public docs updated if behaviour changed.

## Style in one paragraph

Functional core, imperative shell. Domain types are immutable records. Business logic is pure functions over those records, returning results rather than throwing for expected failures. Input and output (HTTP, files, clock, secrets) live behind small interfaces injected at the composition root. Each class or module has one reason to change; collectors depend on abstractions from the core, never the other way round. Prefer composition over inheritance and small, well-named functions over comments.
