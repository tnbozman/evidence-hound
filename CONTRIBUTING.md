# Contributing

Thanks for helping. This project is built in small sprints with strict test-driven development.

1. Open the repo in the dev container (VS Code "Reopen in Container" or Codespaces). No local tooling is needed.
2. Read [`docs/engineering.md`](docs/engineering.md) and the current sprint in [`docs/sprints/`](docs/sprints/README.md).
3. Write the failing test first, then the simplest code to pass, then refactor. Commit each step with the task ID, for example `test(s02-03): ... [red]`.
4. Run `make test` and `make lint` before pushing. CI must be green and introduce no new code scanning alerts.
5. Use only fictional data ("Example Pty Ltd", `example.com`). Contributions containing real organisation data or credentials will be removed and the history rewritten.

## Writing a new collector

Start from the template and make the shared contract tests pass. Collectors must be read-only and emit framework-neutral check IDs.

```bash
dotnet new install ./templates/collector      # once, from a clone of this repo
dotnet new hound-collector -n Example.Widget  # a collector of type "widget", its tests, a README and a recorded response
```

The generated README has the setup steps. The contract kit is documented in [`src/EvidenceHound.Collectors.Testing`](src/EvidenceHound.Collectors.Testing/README.md). `make test-template` (also a CI job) generates a collector against freshly packed SDK packages and checks that it builds and its tests pass unmodified.

## AI-assisted contributions

AI-assisted contributions are welcome. You are responsible for every line you submit: review it, test it, and make sure it follows `CLAUDE.md`.
