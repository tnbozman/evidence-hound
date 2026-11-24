# The AI register and suppliers, checked on every run

Two collectors check the AI register and your suppliers. Each reads the place where the facts are kept.

| Collector | Reads | Checks |
| --- | --- | --- |
| `ai-register` (built in) | The `ai` block of each system in your compliance repo's `scope/setup.yaml`: the [AI register entry](../scope.md#ai-systems) your team reviews by pull request. | The seven checks on the entries themselves. |
| `rest` with the `ciso-assistant` profile | CISO Assistant, with a read-only API token: assets and their labels, risk scenarios, suppliers' solutions, entity assessments and contracts. | Risks of the AI systems, and suppliers' assessments and contracts. |

[The AI governance primer](../ai-grc-primer.md) explains the terms. Each check's observation starts with the AI6 sub-practice it
serves, in words, and ends with what to do. `mappings/ai6.yaml` turns them into AI6 results.

## The ai-register collector

```yaml
collectors:
  - name: ai-register
    type: ai-register
    schedule: "0 6 * * *"
    settings:
      scope: ../scope            # the scope folder, relative to this config file
```

An AI system is **in use** when its `in_use_from` date has come and it is not retired. Checks over systems in use are **not
applicable** when there are none.

| Check | Passes when |
| --- | --- |
| `ai.register_complete` | every AI system has an impact assessment status (`not_required` for a low tier system) |
| `ai.owner_assigned` | every AI system has an owner |
| `ai.screening_recorded` | no system in use is `prohibited`, and each was screened before it went into use |
| `ai.impact_assessment_before_use` | each `medium` or `high` tier system in use had its impact assessment approved on or before going into use |
| `ai.human_oversight_documented` | no `high` tier system in use is `human_out_of_loop` |
| `ai.tested_before_use` | each system in use has a `last_tested_on` date |
| `ai.test_results_current` | each system in use was tested within 6 months (`high` tier) or 12 months (otherwise) |

Each run saves `ai-register.json`: every AI system's entry, whether it is in use, and the SHA-256 of `scope/setup.yaml`, so a result
can be traced to the reviewed file. A `setup.yaml` that does not load is an error (`ai_register.scope_invalid`), never a fail: run
`hound scope validate` and fix it.

## The ciso-assistant profile

Copy `profiles/ciso-assistant.yaml` into your profiles folder, then:

```yaml
collectors:
  - name: ciso-assistant
    type: rest
    schedule: "0 6 * * *"
    settings:
      profile: ciso-assistant
      base_url: https://ciso.example.com/api/
    secrets:
      token: env:HOUND_CA_READ_TOKEN
```

The token is a CISO Assistant API token of a user whose role can only read, such as the built-in Reader role, on the domains you
check. It is not the token `hound` publishes with.

| Check | Passes when |
| --- | --- |
| `ai.risks_in_register` | every asset labelled `ai-tier-medium` or `ai-tier-high` is linked to at least one risk scenario |
| `ai.suppliers_assessed` | every solution providing an asset labelled `ai-use-third-party` comes from a supplier with an entity assessment concluded *ok* whose expiry date has not passed |
| `ai.supplier_contracts_current` | each of those solutions has an *active* contract not past its end date |
| `supplier.assessments_current` | the same, for every supplier's solution |
| `supplier.contracts_current` | the same, for every supplier's solution |

The labels come from your scope: `hound scope apply` writes them. A list the token cannot read makes the checks over it **unable to
assess**, never a fail; the other checks still run. The profile's recorded test cases are in `profiles/ciso-assistant.tests/`; run
them with `hound profile test ciso-assistant`.
