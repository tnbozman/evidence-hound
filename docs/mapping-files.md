# Framework mapping files

A mapping file says which framework-neutral checks satisfy each requirement of one framework, and how their outcomes combine. Adding a framework means adding a mapping file; collectors do not change.

Files live in `mappings/` (your real ones belong in your private deployment repo). The JSON Schema is [`schemas/mapping.schema.json`](../schemas/mapping.schema.json); add `# yaml-language-server: $schema=../schemas/mapping.schema.json` at the top of a file for editor help.

```yaml
framework_urn: urn:example:risk:library:example-baseline   # library URN in CISO Assistant
description: Example baseline used in tests and docs.        # optional
requirements:
  - ref: "EX-1.1"            # requirement reference in the library
    rule: all_pass           # every check must pass
    checks: [identity.mfa.admins, identity.mfa.users]
  - ref: "EX-1.2"
    rule: any_pass           # at least one check must pass
    checks: [identity.mfa.phishing_resistant, identity.mfa.number_matching]
  - ref: "EX-2.1"
    rule: threshold          # at least `threshold` checks must pass
    threshold: 2
    checks: [patching.os.critical_48h, patching.apps.critical_48h, patching.os.supported]
```

Rules:

- `framework_urn` starts with `urn:`. Each requirement is named once in a file.
- Name each requirement with exactly one of `ref` (its `ref_id`) or `urn` (the URN of its requirement node).
- Check IDs are lowercase dot-separated segments that start with a letter (`identity.mfa.admins`, `backup.restore_tested`), at most 128 characters, and are listed once per requirement.
- `threshold` is required with `rule: threshold`, is between 1 and the number of checks, and is not allowed with the other rules.
- `draft: true` marks a row `hound mapping derive` wrote and nobody has reviewed yet. Runs leave draft rows out and report each one as `publish.draft_row`; delete the line once a person has checked the row.
- `covers: part` marks a row whose checks answer only part of the requirement, such as Essential Eight evidence for a NIST SP 800-171 requirement that is wider in scope. When every check passes, the row is partially compliant, not compliant, and its observation says a person must assess the rest. Leave it out, or write `covers: whole`, when the checks answer all of the requirement.
- Unknown keys are errors.

## Requirements a framework repeats

Some frameworks use the same `ref_id` in more than one place. The Essential Eight (November 2023) lists an ISM control under every mitigation strategy it applies to, and the ASD Essentials rates each action in every domain it applies to. Each place is its own row in a CISO Assistant audit.

- `urn:` names exactly one row. Use it when each row needs its own result, as in the Essentials, where each domain is rated separately.
- `ref:` with `rows: all` writes the same result to every row that has the ref. Use it when the rows really are the same requirement, as with an ISM control repeated across Essential Eight strategies.
- `ref:` on its own must match one row. A ref that matches several is an error (`mapping.ref_ambiguous`) that lists the matching URNs.

```yaml
framework_urn: urn:intuitem:risk:framework:asd-essentials-enterprise-it
requirements:
  - urn: urn:intuitem:risk:req_node:asd-essentials-enterprise-it:boundary-services:pro-01:it.pro-01.1
    rule: all_pass
    checks: [network.boundary.secure_design]
  - urn: urn:intuitem:risk:req_node:asd-essentials-enterprise-it:identity-services:pro-01:it.pro-01.1
    rule: all_pass
    checks: [identity.secure_design]
---
framework_urn: urn:intuitem:risk:framework:essential-eight-2023
requirements:
  - ref: ISM-0123          # listed under four strategies, same text in each
    rows: all
    rule: all_pass
    checks: [incident.reporting_to_ciso]
```

When publishing, rows outside the audit's selected implementation groups (such as ML3 rows in an ML2 audit) are left alone and reported as `skipped` with the note `outside selected groups`, not as errors.

## Requirements no check covers

A requirement that no collector can check is usually filled by a manual source: a person exports a record, attests a check with `hound manual approve`, and the next run publishes it ([manual-evidence.md](manual-evidence.md)). Map the requirement to that check like any other. The Essential Eight and AI6 mappings work this way, with their manual sources in `examples/manual-sources/`.

When no manual source covers a requirement either, list it under `manual_only`, with the manual source you suggest for it:

```yaml
manual_only:
  - ref: AI6-3.4
    source: incident register, reviewed by the AI governance owner
```

A `manual_only` entry names a requirement by `ref` or `urn`, as in `requirements`; `hound mapping validate --framework` checks that it exists in the framework, as for mapped requirements. hound writes nothing to its row: CISO Assistant keeps whatever the GRC team records there. `manual_only: []` says none is left.

## Coverage

`hound mapping coverage` shows how an audit's requirements are answered, per parent node (an Essential Eight strategy, an AI6 practice) and implementation group (a maturity level), so you can see that no row is left without an owner:

```console
$ hound mapping coverage mappings/essential-eight-2023.yaml --framework frameworks/essential-eight-2023.json --groups 1,2 \
    --manual examples/manual-sources/essential-eight --manual examples/example-essential-eight.yaml
| Parent | Group | Automated | Manual | Draft | Uncovered |
| --- | --- | --- | --- | --- | --- |
| pa | 1 | 7 | 2 | 0 | 0 |
...
Total: 46 automated, 109 manual, 0 draft, 0 uncovered (one per requirement row and group).
```

- **Automated**: at least one of the requirement's checks comes from a collector.
- **Manual**: every check is attested by a person through a manual source, or the requirement is under `manual_only`.
- **Draft**: only `draft: true` rows answer it, and runs publish none of them until a person reviews the row.
- **Uncovered**: nothing answers it. Map it to a check or a manual source.

For a framework with more than one level above its requirements, such as the ASD Essentials (domain, then principle, then action), a second table counts per top-level node, so you see each domain and priority phase:

```console
$ hound mapping coverage mappings/asd-essentials-enterprise-it.yaml --framework frameworks/asd-essentials-enterprise-it.json --groups 1 \
    --manual examples/manual-sources --manual examples/example-essential-eight.yaml
...
| Top level | Group | Automated | Manual | Draft | Uncovered |
| --- | --- | --- | --- | --- | --- |
| Governance and culture | 1 | 3 | 6 | 0 | 0 |
| Visibility and response | 1 | 1 | 11 | 0 | 0 |
| Boundary services | 1 | 11 | 26 | 0 | 0 |
| Identity services | 1 | 11 | 24 | 0 | 0 |

Total: 26 automated, 67 manual, 0 draft, 0 uncovered (one per requirement row and group).
```

`--manual` takes your hound config, a manual source template or a folder of them; the checks of every `type: manual` collector in them count as manual. Without `--manual`, every mapped requirement counts as automated, and a `--manual` path with no manual source in it is reported. In your own repo, pass `--manual hound.yaml`; the paths above are in the Evidence Hound repo. `--groups` is the audit's selected groups (default: all of the framework's), and `--json` prints the same counts for an agent.

## Deriving a mapping for a new framework

When a framework replaces another, such as the ASD Essentials replacing the Essential Eight, `hound mapping derive` drafts the new mapping from your existing one and a CISO Assistant transition mapping library (a library with a `requirement_mapping_set`). Each row of the new framework gets the union of the checks of the old rows the library links to it, with `rule: all_pass`, named by URN and marked `draft: true`:

```console
$ hound mapping derive mappings/essential-eight-2023.yaml --from frameworks/essential-eight-2023.json \
    --to frameworks/asd-essentials-enterprise-it.json \
    --links frameworks/mapping-essential-eight-2023-and-asd-essentials-enterprise-it.yaml --groups 1 --out draft.yaml
wrote 55 draft rows to draft.yaml; 38 rows got no checks and are listed at its end. Runs publish no draft row: review each one, then delete its 'draft: true' line.
```

- Each row's comment names its source requirements. A link that only overlaps (`intersect`, as every Essential Eight to Essentials link is) may not cover the whole new requirement, so a person decides whether the checks are enough, adds checks where the new requirement asks for more, then deletes `draft: true`.
- Rows the old mapping gives no checks are listed as comments at the end. Map each to a manual source's checks, or list it under `manual_only`.
- `--groups` limits the draft to the new framework's implementation groups (for the Essentials, a priority phase). Without `--out` the draft is printed, and an existing `--out` file is never replaced unless you add `--force`.
- hound carries the Essential Eight 2023 to Essentials library from CISO Assistant v4.3.4 in `frameworks/`, and a reviewed phase 1 mapping made this way in [`mappings/asd-essentials-enterprise-it.yaml`](../mappings/asd-essentials-enterprise-it.yaml).

## Crosswalks to another framework

A crosswalk says which requirements of another framework your evidence for one programme already answers. hound carries one from the Essential Eight (ML1 and ML2) to NIST SP 800-171 Rev 2, for suppliers who hold CUI, in [`mappings/crosswalks/`](../mappings/crosswalks/). It is advisory: it was drafted with AI from the requirement texts and nobody has reviewed it yet, and hound writes nothing to CISO Assistant from it. Full CMMC Level 2 comes later, with its own mapping and audit.

```console
$ hound mapping crosswalk --from essential-eight --config hound.yaml
# Crosswalk: Essential Eight to urn:intuitem:risk:framework:nist-sp-800-171-rev2
...
Total: 1 answered, 30 partly answered, 79 open, of 110 requirements.

| Requirement | Status | Answered by: today |
| --- | --- | --- |
| 3.1.6 Use non-privileged accounts or roles for non-security functions | answered | ISM-0445 (equal, 1 check): compliant; ISM-1175 (subset, 1 check): compliant |
...
```

- `--from` is a programme id from `scope/setup.yaml`. Its checks come from the config target for its audit; today's results from that audit in CISO Assistant. `--offline` skips CISO Assistant and shows only what would answer each requirement. `--json` prints the same for an agent.
- **Answered**: an Essential Eight requirement that says the same (`equal`) or asks for more (`superset`) has checks behind it. **Partly answered**: only overlapping (`intersect`) or narrower (`subset`) requirements link to it, or the linked one has no checks. **Open**: nothing in the Essential Eight answers it; you need other evidence.
- hound uses `--file` if you give one, else a crosswalk in `mappings/crosswalks/` next to `hound.yaml`, else its own. Copy its file there to correct links, and once a person has checked every link, add `reviewed: {by: <name or role>, on: YYYY-MM-DD}`. The schema is [`schemas/crosswalk.schema.json`](../schemas/crosswalk.schema.json).
- CISO Assistant v4.3.4 has no API that lists a mapping library's links, so hound cannot read one yet; its file is used.

## Framework snapshots

A framework snapshot lets you check a mapping without CISO Assistant. It lists the framework's URN, version and implementation groups, and each assessable requirement's URN, `ref_id`, parent, groups and a short title, and the name of each parent node (an Essential Eight strategy), which reports show. Make one from a CISO Assistant library file and commit it under `frameworks/`:

```console
$ hound framework snapshot essential-eight-2023.yaml --out frameworks/essential-eight-2023.json
wrote frameworks/essential-eight-2023.json: urn:intuitem:risk:framework:essential-eight-2023 version 3, 152 assessable requirements
```

Library files ship with CISO Assistant under `backend/library/libraries/` in its source repository. Snapshots of the Essential Eight (November 2023) and the ASD Essentials for enterprise IT, from CISO Assistant v4.3.4, are in [`frameworks/`](../frameworks/). Make a new snapshot when you upgrade CISO Assistant and the library version changes.

## How results are worked out

`hound mapping apply <mapping> <checks.json>` (and later the runner) turns check results into one result per requirement, in file order:

1. Only the latest result of each check counts (by `collected_at`).
2. If any mapped check has no result, or its result is `error`, the requirement is `not_assessed`.
3. `not_applicable` checks are ignored. If every mapped check is `not_applicable`, so is the requirement.
4. The rule decides the rest:

| Rule | `compliant` | `partially_compliant` | `non_compliant` |
| --- | --- | --- | --- |
| `all_pass` | every check passes | no failures, some `partial` | any check fails |
| `any_pass` | every check passes | at least one `pass` or `partial` | nothing passes or partly passes |
| `threshold` | at least `threshold` checks pass (capped at the number of applicable checks) | fewer pass, but at least one `pass` or `partial` | nothing passes or partly passes |

The observation lists the rule and each check with its outcome and observation; the evidence is the sorted union of the checks' evidence.

## Validate a file

```console
$ hound mapping validate mappings/example.yaml
valid: 4 requirements
```

Problems are printed with the file and line, and the command exits with code 3:

```console
error: mappings/bad.yaml line 7: unknown rule 'most_pass'; use all_pass, any_pass or threshold.
error: mappings/bad.yaml line 6: requirement 'EX-1.1' is already used at line 3.
```

With `--framework <snapshot>`, it also checks every requirement against the framework: unknown URNs and refs, refs that match several rows without `rows: all`, and a mapping for a different framework.

```console
$ hound mapping validate mappings/essential-eight-2023.yaml --framework frameworks/essential-eight-2023.json
error: mappings/essential-eight-2023.yaml line 12: ref 'ISM-0123' matches 4 rows: urn:intuitem:risk:req_node:essential-eight-2023:mfa:ism-0123, ... Name one row by 'urn', or add 'rows: all' to write the same result to every row.
```
