# Closed-loop feedback

The public repo's automation is the reviewer that never sleeps. Every push to a sprint branch produces feedback, and the next task does not start until that feedback is clean.

## Signals

| Signal | Source | Where to read it | Blocks merge |
| --- | --- | --- | --- |
| Build, unit, contract tests | `ci.yml` | PR checks; test report artifact | Yes |
| Functional tests | `ci.yml` (functional job) | PR checks | Yes |
| Lint, format, analysers | `ci.yml` | PR checks | Yes |
| Organisation data check | `ci.yml` (`org-data` job) | PR checks | Yes |
| Code scanning | CodeQL default setup | Security tab; PR annotations | Yes (high and above) |
| Secrets | Secret scanning with push protection | Push is rejected; Security tab | Yes |
| Dependencies | Dependabot alerts and the dependency review action | Security tab; PR checks | Yes (high and above) |
| Supply chain posture | OpenSSF Scorecard (weekly) | Security tab | No; reviewed monthly |

## The loop for each task

1. Push the task's commits.
2. Wait for checks: `gh pr checks --watch`.
3. Read failures: `gh run view --log-failed`.
4. Read new code scanning alerts on the branch:
   `gh api "repos/{owner}/{repo}/code-scanning/alerts?ref=refs/heads/$(git branch --show-current)&state=open"`
5. Read Dependabot alerts: `gh api "repos/{owner}/{repo}/dependabot/alerts?state=open"`
6. Fix with a new red-green cycle when the failure is behavioural; fix directly when it is lint or configuration.
7. Record anything that needs a later task under "Discovered work" in the sprint file.

The `/check-feedback` Claude Code command runs steps 2 to 5 and summarises.

## Sprint-level review

At sprint end, before merging: all checks green, zero open high or critical alerts introduced by the sprint, sprint file updated with functional check results, and a short "Sprint review" note at the bottom of the sprint file (what shipped, what slipped, what the feedback taught us).
