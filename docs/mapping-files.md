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

## Framework snapshots

A framework snapshot lets you check a mapping without CISO Assistant. It lists the framework's URN, version and implementation groups, and each assessable requirement's URN, `ref_id`, parent, groups and a short title. Make one from a CISO Assistant library file and commit it under `frameworks/`:

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
