---
description: Read CI, code scanning and dependency feedback for the current branch and summarise what to fix
---

1. `gh pr checks --watch` for the current branch's PR (if there is no PR, use `gh run list --branch "$(git branch --show-current)" --limit 5`).
2. For each failed run: `gh run view <id> --log-failed` and identify the root cause.
3. Open code scanning alerts on this branch:
   `gh api "repos/{owner}/{repo}/code-scanning/alerts?ref=refs/heads/$(git branch --show-current)&state=open" --jq '.[] | {rule: .rule.id, severity: .rule.security_severity_level, file: .most_recent_instance.location.path, line: .most_recent_instance.location.start_line}'`
4. Open Dependabot alerts: `gh api "repos/{owner}/{repo}/dependabot/alerts?state=open" --jq '.[] | {pkg: .dependency.package.name, severity: .security_advisory.severity}'`
5. Summarise as a short table: signal, status, what to do. Propose fixes; for behavioural failures start a new red-green cycle. Do not weaken tests, analysers, CodeQL or the org-data check.
