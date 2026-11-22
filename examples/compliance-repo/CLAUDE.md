# CLAUDE.md

Instructions for an AI assistant working in this compliance repo. The repo says what CISO Assistant should hold for Example Pty Ltd; Evidence Hound (`hound`) checks it and applies it. People review every change as a pull request.

## Layout

- `hound.yaml`: the deployment config (collectors, schedules, the target audit and its mapping). Schema: `config.schema.json`.
- `scope/setup.yaml`: root folder, frameworks (with snapshots in `frameworks/`), domains with their perimeters and systems.
- `scope/controls.yaml`: every applied control, declared once.
- `scope/access.yaml`: users, teams and the roles they hold on each domain.
- `scope/audits/<audit>.yaml`: one audit each: framework, groups, systems, documents, controls with the requirements they support, hand-written `assessments`, `unlisted`, and optionally `cycle` and `sign_offs`.
- `mappings/<framework>.yaml`: which checks satisfy which requirements.
- `frameworks/`: snapshots (`hound framework snapshot`) and custom framework libraries.
- `documents/`: policies and procedures, referenced by audit `documents`.
- `manual-sources/README.md`: the hand-exported sources and their owners.

## After every change

Run these and fix every problem they report before you finish:

```sh
hound scope validate scope/ --config hound.yaml --format json
hound mapping validate mappings/<each changed mapping>.yaml
hound config validate hound.yaml
```

Problems come with a file, a line, a code and a hint. Never weaken a rule to make a problem go away: fix the file it points at.

## Rules

- **No secrets.** Credentials are references only (`env:NAME`, `file:/run/secrets/name`). Never write a token, password, key or certificate into any file.
- **Access is a person's decision.** Change `scope/access.yaml` only when asked, never add a password or token to it, and keep at least one active approver on every domain.
- **No real evidence.** Never add exports, screenshots, logs or other evidence files to the repo. Collectors gather evidence on the server; manual exports go to the drop folders listed in `manual-sources/README.md`, which are outside the repo. Only policies and procedures go in `documents/`.
- **Never write a result a collector computes.** If a mapping computes a requirement, do not add a hand-written assessment for it (`scope.result_conflict`). Add hand-written results only for requirements no check covers.
- **Mark nothing not applicable silently.** `unlisted` must say `not_assessed`, or `not_applicable` with a reason.
- **Sign-offs are a person's decision.** Never add a `sign_offs` entry unless the person who signs asked for it in the conversation; it locks the period for good.
- Keep changes small and on one subject, so a reviewer can follow them.

## The pull request

Use the `hound scope plan` output as the summary of what the change does in CISO Assistant. With access to CISO Assistant, run `hound scope plan scope/ --config hound.yaml` and paste its Markdown; without it, say which objects you expect to change, and the workflow's plan comment will confirm it. Call out anything the plan lists as drift.
