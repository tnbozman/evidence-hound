---
description: Implement the next unticked task in the current sprint with strict TDD
---

1. Find the sprint file in `docs/sprints/` with `Status: In progress`. Confirm the current branch is `sprint/<that file's slug>`; if not, stop and tell me.
2. Pick the first task whose `- [ ] Done` is unticked, unless I named one: $ARGUMENTS
3. Restate the task's story and acceptance criteria in two or three lines and list the tests you will write first.
4. Follow "How to implement a task" in `CLAUDE.md` exactly: red (run tests, show they fail, commit), green (simplest code, full unit suite, commit), refactor (commit if anything changed), functional check (run it, record command and result in the sprint file).
5. Tick the task in the sprint file and in the sprint PR body (`gh pr edit`), push, then run `/check-feedback`.
6. Stop after this one task and summarise: what changed, tests added, functional check result, any discovered work.

Never put organisation-specific names or data in code, tests, fixtures, docs or commits.
