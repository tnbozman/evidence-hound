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

- `framework_urn` starts with `urn:`. Requirement refs are unique within a file.
- Check IDs are lowercase dot-separated segments that start with a letter (`identity.mfa.admins`, `backup.restore_tested`), at most 128 characters, and are listed once per requirement.
- `threshold` is required with `rule: threshold`, is between 1 and the number of checks, and is not allowed with the other rules.
- Unknown keys are errors.

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
error: mappings/bad.yaml line 6: requirement ref 'EX-1.1' is already used at line 3.
```
