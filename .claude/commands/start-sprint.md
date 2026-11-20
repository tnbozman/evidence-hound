---
description: Start a sprint (creates the sprint branch and draft PR), then check out the branch
---

1. Run `scripts/start-sprint.sh $ARGUMENTS` (for example `s01`). If it fails, show the reason and stop.
2. `git fetch origin && git switch sprint/<slug>` for the new branch.
3. Read the sprint file and list its tasks with their tests-first lists, then wait for me to run `/next-task`.
