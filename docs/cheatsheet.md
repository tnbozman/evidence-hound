# hound cheat sheet

Every command takes `--help`. `--json` makes a command prompt-free for an agent. **Person** marks a command that refuses an agent or a
pipe. CISO Assistant calls need `HOUND_CA_URL` and `HOUND_CA_TOKEN`. `(req)` marks a required option. The PDF is rendered from this
file by `scripts/cheatsheet/render.sh`.

## Commands

### Set up a repo
- `hound init <folder>`: new compliance repo: config, scope, an Essential Eight audit and its mapping
  - `--org --code --url --time-zone --level --interval --owner --evidence-root --assistant -y --json`
- `hound agent setup`: set up Claude Code or Copilot in the repo
  - `--assistant --repo`
- `hound config validate <file>`: check `hound.yaml`
  - `--check-secrets`
- `hound collectors list`: collector types in this build

### Suppliers, systems and audits
- `hound supplier draft`: draft a supplier, its solution, contract and review
  - `--ref --name --solution-ref --solution-name --contract-ref --contract-name` (req)
  - `--kind msp|reseller|vendor --for --system --level --document --data --description --start --end --annual-expense --currency --json`
- `hound supplier list`: who handles a kind of sensitive data, on what authority
  - `--data cui|pii|export_controlled|classified` (req) `--repo --json`
- `hound add system`: add a system to the scope
  - `--scope` (req) `--domain --name --ref-id --type --owner --description --parents --audit -y`
- `hound add control`: add an applied control
  - `--scope` (req) `--ref-id --name --domain --owner --category --systems --audit --requirements -y`
- `hound audit draft`: draft an audit
  - `--ref --name --framework` (req) `--group --domain --perimeter --system --due --json`
- `hound programme list`: programmes with framework, groups, audit of record and requirement counts
  - `--scope --json`
- `hound ssp build`: a programme's system security plan from CISO Assistant and the repo, gaps first, each value with its source
  - `--programme` (req) `--scope --config --json --strict --snapshot-out --from-snapshot --format --out`
- `hound ssp diff <before> <after>`: what changed between two signed versions' snapshots, boundary and inventory first
- `hound ssp publish`: upload a signed version (plan, POA&M, snapshot, audit report) as a new evidence revision
  - `--programme --snapshot` (req) `--scope --config`
- `hound ssp suggest`: JSON brief for your own agent: controls with no description behind an implemented requirement (no model)
  - `--programme` (req) `--scope --config --from-snapshot`
- `hound ssp describe`: write a control description drafted with AI into controls.yaml, marked for review
  - `--control --text` (req) `--scope --config`
- `hound ssp poam`: the plan of action and milestones: requirements not yet met, with control, owner, ETA and status
  - `--programme` (req) `--scope --config --json`

### Onboard
- `hound onboard <system>`: onboard a system: supplier, applicability, files
  - `--repo --framework --group --spec`
- `hound onboard pending`: agent: what is still undecided (`hound onboard <system> pending`)
  - `--json --owner --product --solution`
- `hound onboard decide <req>`: agent: record one decision (`hound onboard <system> decide`)
  - `--kind api_check|manual|not_applicable|other_system --reasoning` (req) `--source --other-system --justification --json`
- `hound onboard environment`: the environment's capability map
  - `--signals --graph --deployment --microsoft-system`
- `hound onboard environment pending`: agent: undecided mitigations
- `hound onboard environment decide <mitigation>`: agent: record one
  - `--provided-by gap|manual|system --reason` (req) `--system --supplier --owner --evidence --route`
- `hound approve`: **Person**: approve every draft, one at a time
  - `--repo`

### Scope: CISO Assistant as config
- `hound scope validate <scope>`: check scope files offline
  - `--config --format json|text --json`
- `hound scope plan <scope>`: what would change (reads only)
  - `--config --format json|markdown --prune --show-unmanaged --detailed-exitcode --out`
- `hound explain <files>`: plain-English review of a plan or config change
  - `--repo --json`
- `hound scope apply <scope>`: write the scope to CISO Assistant
  - `--config --dry-run --prune`
- `hound scope new-audit`: write an audit file for a framework
  - `--framework --ref-id` (req) `--groups --out`
- `hound scope import <workbook>`: import an audit workbook
  - `--audit` (req) `--repo --sheet --column --json`
- `hound scope export`: export CISO Assistant to scope files
  - `--out` (req) `--root-folder --with-files`

### Collect and publish
- `hound run`: collect, store evidence, map, plan, publish
  - `--config` (req) `--only --dry-run --no-publish`
- `hound serve`: run each collector on its schedule
  - `--config` (req)
- `hound status`: latest run of each collector
  - `--config` (req)
- `hound collect`: step 1 of run: collect into the evidence store
  - `--config` (req) `--dry-run`
- `hound plan`: step 2: what a run would update
  - `--config --checks` (req) `--dry-run`
- `hound publish`: step 3: publish a run's results
  - `--config --checks --evidence-dir` (req) `--dry-run`

### Manual evidence
- `hound manual upload <file>`: drop an export into a manual source
  - `--config --source` (req)
- `hound manual propose <source> <file>`: agent: propose a result and its extract
  - `--config` (req) `--result --check --observation --supports --json`
- `hound manual approve`: **Person**: attest the proposals
  - `--config` (req) `--source`

### Collectors and REST profiles
- `hound add collector <type>`: add a collector to `hound.yaml`
  - `--config` (req) `--name --schedule --set --secrets-dir --secret-file -y`
- `hound onboard profile <system>`: draft a REST profile from an API spec
  - `--profiles --name --set --secret --sample`
- `hound profile validate <name>`: check a profile
  - `--profiles`
- `hound profile test <name>`: try a profile against recordings
  - `--profiles`
- `hound profile diff <name>`: compare with the library copy
  - `--profiles --library`

### Frameworks, mappings and skills
- `hound framework snapshot <library>`: snapshot a framework library
  - `--out`
- `hound mapping validate <file>`: check a mapping file
  - `--framework`
- `hound mapping apply <mapping> <checks>`: map check results to requirements
- `hound skill list`: onboarding skills, hound's and yours
- `hound skill new <name>`: start a skill
- `hound skill add <file>`: add a skill file
- `hound skill validate`: check the skills
- `hound skill draft`: agent: draft skills
  - `--skills` on every skill command; draft: `--only`

### Weekly review
- `hound review pending --json`: agent: everything waiting on a person
- `hound review recommend <key> --answer ... --reason ... --source ...`: agent: its recommendation
- `hound review --for 60m`: **Person**: one guided weekly session
  - `--no-apply`: stop at the files

## Typical paths

### Start a repo
1. `hound init ./compliance --org "Example Pty Ltd"`
2. `hound agent setup --assistant claude`
3. `hound config validate hound.yaml --check-secrets`
4. `hound scope validate scope --config hound.yaml`

### Onboard a supplier and a system
1. Agent: `hound supplier draft --ref ... --name ...`
2. Agent: `hound onboard <system> pending --json`
3. Agent: `hound onboard <system> decide <req> --kind ... --reasoning ...`
4. **Person**: `hound approve`
5. `hound scope validate scope --config hound.yaml`

### Write config to CISO Assistant
1. `hound scope plan scope --config hound.yaml --out plan.json`
2. `hound explain plan.json`
3. `hound scope apply scope --config hound.yaml`

### Collect evidence on a schedule
1. `hound run --config hound.yaml --dry-run`
2. `hound run --config hound.yaml`
3. `hound serve --config hound.yaml` (the runner container)
4. `hound status --config hound.yaml`

### Manual evidence each period
1. CISO Assistant emails the owner the runbook
2. `hound manual upload <file> --config hound.yaml --source <src>`
3. Agent: `hound manual propose <src> <file> --config hound.yaml`
4. **Person**: `hound manual approve --config hound.yaml`
5. The next `hound run` publishes it

### Add a REST collector
1. `hound onboard profile <system> --sample`
2. `hound profile validate <name>`
3. `hound profile test <name>`
4. `hound add collector rest --config hound.yaml`

### Move off a spreadsheet
1. `hound scope import audit.xlsx --audit <AUDIT-REF>`
2. `hound scope validate scope`
3. `hound scope plan scope`, then `hound scope apply scope`

### Weekly review
1. CISO Assistant's weekly task alerts the GRC member
2. Agent: `hound review pending --json`, drafts each recommendation
3. **Person**: `hound review --for 60m`: read, accept or challenge
4. Accepted items are applied in one batch
