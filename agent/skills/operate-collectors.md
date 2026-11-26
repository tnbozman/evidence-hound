---
name: operate-collectors
description: Set up, check and run hound's collectors, read a run's results and errors, and keep evidence flowing to CISO Assistant. Use when adding a collector, after a collector fails, when the person asks whether evidence is current, or before a run.
---
## Operate the collectors

The guides are [docs/configuration.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/configuration.md),
[docs/running.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/running.md) and
[docs/publishing.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/publishing.md).

1. See what hound can collect: `hound collectors list`. Each type lists its check IDs, settings and secrets.
2. Add a collector only when the person asks: `hound add collector <type> --config hound.yaml --name <name> --schedule "<cron>" --set key=value`.
   Never type a secret's value; ask the person to run it with `--secrets-dir` or to give the reference (`env:NAME` or `file:/path`).
3. Check the config: `hound config validate hound.yaml`, then `hound config validate hound.yaml --check-secrets`, which resolves every
   secret reference without printing any. Fix every problem it reports at the file and line it names.
4. Try it without sending anything: `hound collect --dry-run --config hound.yaml`, then `hound run --dry-run --config hound.yaml --only <name>`,
   which collects, stores the evidence and plans the updates. `hound plan --config hound.yaml --checks <results.json>` shows what a run's
   results would change in CISO Assistant; `hound publish --dry-run --config hound.yaml --checks <results.json>` shows what it would send.
5. Read how the collectors are doing: `hound status --config hound.yaml --scope scope/ --json` gives each collector's last run, outcome
   and problems, each programme's standing from the latest posture snapshot (level met, compliant of applicable, not assessed), and the
   security exceptions near expiry; it also gives each collector's checks and outcomes, whether CISO Assistant is reachable and
   accepts the token, publishes it refused, and each manual source's due date. Quote the standing as it is; never round it up or call a programme compliant while rows are not assessed. A collector in error, or with no run for a week, is a gap: its rows go to CISO Assistant as
   non-compliant with "No evidence yet" until a run succeeds. Tell the person which check failed and why, from the run's problems; never
   guess a cause.
6. Real runs belong to the runner: `hound serve --config hound.yaml` runs each collector on its schedule (the runner container's default);
   `hound run --config hound.yaml` runs them once and publishes, collecting up to four at once (`--parallel <n>` changes that; each
   still publishes in config order). Run either only when the person asks.

Never copy evidence, an export or a token into the repo, a message or a web search.
