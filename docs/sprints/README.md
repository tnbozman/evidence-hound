# Sprints

Release 1 is delivered in one-week sprints. Each sprint has its own file, `sNN-<slug>.md`, with a goal, tasks, acceptance criteria and the tests to write first.

| Sprint | Goal | Release |
| --- | --- | --- |
| [s00](s00-foundations.md) | Repo, dev container, CI, security and a walking skeleton | 1 |
| [s01](s01-domain-and-mapping.md) | Domain model, framework-neutral checks and mapping engine | 1 |
| [s02](s02-ciso-assistant-publisher.md) | CISO Assistant client and publisher | 1 |
| [s03](s03-runner-and-evidence.md) | Runner, configuration, evidence packaging and run status | 1 |
| [s04](s04-collector-sdk-and-manual.md) | Collector SDK, contract test kit, manual-upload collector | 1 |
| [s05](s05-m365-collector.md) | Microsoft 365 reference collector | 1 |
| [s06](s06-sophos-veeam-collectors.md) | Sophos Central and Veeam reference collectors | 1 |
| [s07](s07-reference-deployment.md) | Reference Docker deployment, scheduling, secrets, backups | 1 |
| [s08](s08-e8-mapping-and-adoption.md) | Essential Eight mapping and adoption guides | 1 |
| [s09](s09-release-1.md) | Packaging, release pipeline, docs site | 1 |
| [s10](s10-nist-800-171.md) | NIST SP 800-171 mapping | 2 |
| [s11](s11-collector-console.md) | Collector console (optional) | 2 |

## Lifecycle

1. **Start.** Run the *Start sprint* workflow (Actions tab, input `sNN`) or `scripts/start-sprint.sh sNN`. It creates `sprint/sNN-<slug>` from `main`, sets the sprint file to `Status: In progress`, and opens a draft PR whose description is the task checklist.
2. **Implement.** In the dev container, open Claude Code and run `/next-task`. Each task follows red, green, refactor, functional check, as in `CLAUDE.md`.
3. **Feedback.** After each task, `/check-feedback` reads CI and security results; fix before moving on.
4. **Review.** When all tasks are ticked and checks are green, add the sprint review note, mark the PR ready, review and merge.
5. **Close.** In the last commit before merging, set `Status: Done`. Unfinished tasks move to the next sprint file under "Carried over".

## Task format

```markdown
### sNN-TT Short title
- [ ] Done

**Story:** As a <role>, I want <capability>, so that <outcome>.

**Acceptance criteria**
- Observable, testable statements.

**Tests first**
- `TestClass.Given_..._When_..._Then_...`

**Functional check**
- Command to run and the expected result.

**Notes**
- Design hints, constraints, links.
```

## Status values

`Planned`, `In progress`, `Done`. Only one sprint is `In progress` at a time.
