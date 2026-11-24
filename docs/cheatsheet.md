# hound cheat sheet

Every command takes `--help`. `--json` makes a command prompt-free for an agent. Commands marked **person** refuse an agent or a pipe.
CISO Assistant calls need `HOUND_CA_URL` and `HOUND_CA_TOKEN`.

## Typical paths

| Goal | Steps |
| --- | --- |
| **Start a repo** | `hound init ./compliance --org "Example Pty Ltd"` → `hound agent setup` → `hound config validate hound.yaml --check-secrets` |
| **Onboard a supplier and system** | `hound supplier draft ...` → `hound onboard <system> pending --json` → `hound onboard <system> decide <req> --kind ...` (agent) → `hound approve` (**person**) → `hound scope validate scope --config hound.yaml` |
| **Write config to CISO Assistant** | `hound scope plan scope --config hound.yaml` → `hound explain <plan>` → `hound scope apply scope --config hound.yaml` |
| **Collect evidence** | `hound run --config hound.yaml --dry-run` once, then `hound serve --config hound.yaml` (scheduled) and `hound status --config hound.yaml` |
| **Manual evidence** | owner sends the export → `hound manual upload <file> --config hound.yaml --source <src>` → `hound manual propose <src> <file> --config hound.yaml` (agent) → `hound manual approve --config hound.yaml` (**person**) → next `run` publishes |
| **New REST collector** | `hound onboard profile <system>` → `hound profile validate <name>` → `hound profile test <name>` → `hound add collector rest --config hound.yaml` |
| **From a spreadsheet** | `hound scope import <workbook.xlsx> --audit <AUD-REF>` → `hound scope validate` → `plan` → `apply` |

## Commands

| Command | Does | Main options |
| --- | --- | --- |
| `hound init <folder>` | New compliance repo: config, scope, an E8 audit and mapping | `--org --code --url --time-zone --level --interval --owner --evidence-root --assistant -y --json` |
| `hound agent setup` | Set up Claude Code or Copilot in the repo | `--assistant --repo` |
| `hound add system` / `hound add control` | Add a system or control to the scope | `--scope` (req), `--domain --name --ref-id --owner --audit -y`; system: `--type --parents`; control: `--category --systems --requirements` |
| `hound add collector <type>` | Add a collector to `hound.yaml` | `--config` (req), `--name --schedule --set --secrets-dir --secret-file -y` |
| `hound supplier draft` | Draft a supplier, solution, contract and review | `--ref --name --solution-ref --solution-name --contract-ref --contract-name` (req), `--kind msp\|reseller\|vendor --for --system --level --document --start --end --annual-expense --currency --json` |
| `hound onboard <system>` | Onboard a system interactively | `--repo --framework --group --spec` |
| `hound onboard pending` / `hound onboard decide <req>` (after `<system>`) | Agent: what is undecided / record one decision | `--json`; decide: `--kind api_check\|manual\|not_applicable\|other_system --reasoning` (req), `--source --other-system --justification` |
| `hound onboard environment`, `hound onboard environment pending`, `hound onboard environment decide <mitigation>` | The environment's capability map | `--signals --graph --deployment --microsoft-system`; decide: `--provided-by gap\|manual\|system --reason` (req), `--system --supplier --owner --evidence --route` |
| `hound onboard profile <system>` | Draft a REST profile from an API spec | `--profiles --name --set --secret --sample` |
| `hound approve` | **Person**: approve drafts one at a time | `--repo` |
| `hound audit draft` | Draft an audit | `--ref --name --framework` (req), `--group --domain --perimeter --system --due --json` |
| `hound scope validate <scope>` | Check scope files offline | `--config --format json\|text --json` |
| `hound scope plan <scope>` | What would change in CISO Assistant (reads only) | `--config --format json\|markdown --prune --show-unmanaged --detailed-exitcode --out` |
| `hound scope apply <scope>` | Write the scope to CISO Assistant | `--config --dry-run --prune` |
| `hound scope new-audit` | Write an audit file for a framework | `--framework --ref-id` (req), `--groups --out` |
| `hound scope export` / `hound scope import <workbook>` | Export CISO Assistant to files / import an audit workbook | export: `--out` (req), `--root-folder --with-files`; import: `--audit` (req), `--repo --sheet --column --json` |
| `hound explain <files>` | Plain-English review of a plan or config change | `--repo --json` |
| `hound run` | Collect, store, map, plan and publish | `--config` (req), `--only --dry-run --no-publish` |
| `hound serve` | Run each collector on its schedule | `--config` (req) |
| `hound status` | Latest run of each collector | `--config` (req) |
| `hound collect` / `hound plan` / `hound publish` | The steps of `run`, one at a time | `--config` (req), `--dry-run`; plan, publish: `--checks` (req); publish: `--evidence-dir` (req) |
| `hound manual upload <file>` | Drop a file into a manual source | `--config --source` (req) |
| `hound manual propose <source> <file>` | Agent: propose a result with supporting extract | `--config` (req), `--result --check --observation --supports --json` |
| `hound manual approve` | **Person**: attest proposals | `--config` (req), `--source` |
| `hound config validate <file>` | Check `hound.yaml` | `--check-secrets` |
| `hound collectors list` | Collector types in this build | |
| `hound profile validate` / `hound profile test` / `hound profile diff <name>` | Check, try or compare a REST profile | `--profiles`; diff: `--library` |
| `hound mapping validate <file>` / `hound mapping apply <mapping> <checks>` | Check a mapping / map check results | validate: `--framework` |
| `hound framework snapshot <library>` | Snapshot a framework library | `--out` |
| `hound skill list` / `hound skill new` / `hound skill add` / `hound skill validate` / `hound skill draft` | Onboarding skills | `--skills`; draft: `--only` |

Coming in s10-07: `hound review pending --json` and `hound review --for 60m`, one guided weekly session for every approval.
