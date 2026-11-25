# Risk studies and security exceptions

This guide is for a GRC team that keeps its risk register in CISO Assistant and wants each change proposed and reviewed in the
compliance repo, like the rest of the scope. It shows the files, how `hound` checks them, how they reach CISO Assistant, and which
decisions stay with a person in CISO Assistant. The full file reference is in [Scope files](scope.md#risksstudyyaml).

The example compliance repo has all of it: `examples/compliance-repo/scope/risks/ex-risk-2026.yaml`,
`examples/compliance-repo/scope/exceptions.yaml` and the matrix and threats in `examples/compliance-repo/libraries/example-risk.yaml`.

## 1. Choose a matrix and threats

A risk study is rated on one risk matrix. Use a built-in one (such as `urn:intuitem:risk:library:critical_risk_matrix_5x5`) or write
your own in a library file, with the threats your scenarios name that no loaded catalogue has. List it in `scope/setup.yaml` and
snapshot it, so `hound` can check level names and threats without CISO Assistant:

```console
$ hound library validate libraries/example-risk.yaml
$ hound library snapshot libraries/example-risk.yaml --out libraries/example-risk.json
```

Raise the library's `version` whenever you change it: CISO Assistant takes only a new version, so `hound` stops with
`library.version_not_bumped` otherwise. Give your threats and matrix their own `ref_id`s and names: CISO Assistant refuses a library
whose threat reuses a `ref_id` or name already loaded in the same folder (HTTP 422, "already used in this scope").

## 2. Write the study and its scenarios

One file per study under `scope/risks/`. Each scenario names the systems it affects, its threats, the controls in place and planned,
and its ratings by level name:

```yaml
  - ref_id: R-01
    name: Ransomware encrypts the file shares
    systems: [Example 365]
    threats: [THR-02]
    existing_controls: [AC-BACKUP]
    current: {probability: Possible, impact: Major}
    residual: {probability: Unlikely, impact: Major}
    treatment: mitigate
    justification: Backups are restored and tested each month.
```

To add a scenario, copy one, give it a new `ref_id`, rate it, and run `hound scope validate scope/`. It names the line of any level the
matrix does not have, any system, threat or control the scope does not declare, and a residual rating above the current one (treatment
can only lower a risk).

## 3. Request a security exception

A known gap you live with for a while, such as an application that cannot use single sign-on yet, goes in `scope/exceptions.yaml` with
its expiry, owner, the systems, controls and scenarios it concerns, and the audit rows it covers. Write no `status`: only the approver
decides, in CISO Assistant (`scope.approval_not_configurable` if you try).

## 4. Plan, review and apply

```console
$ hound scope plan scope/ --config hound.yaml
$ hound scope apply scope/ --config hound.yaml
```

The plan shows rating changes by level name (such as `current_probability: Likely → Possible`), so a reviewer reads what changed
without the matrix to hand. Its "Security exceptions and risk acceptances" section lists each request with the approver's decision, then
what to renew or close: an approved exception that expires within 60 days, or one past its expiry that is still approved. Between plans,
`hound status --config hound.yaml --scope scope` lists the exceptions whose declared expiry is near or past, offline.

A failing check on a row an approved, unexpired exception covers keeps its result, and `hound run` names the exception in the row's
observation. An exception explains a gap; it does not close it.

## What stays in CISO Assistant

- **Approvals.** Accepting a risk and approving an exception are a person's decisions, made by the approver in CISO Assistant, so the
  decision keeps its separation of duties: the person who writes the request is not the one who approves it. A scenario with
  `treatment: accept` asks for a risk acceptance (`ACC-<study>-<scenario>`); `hound` never sets its state.
- **Computed levels.** CISO Assistant computes each scenario's risk level from its probability and impact on the matrix, so the file
  holds only the ratings and `hound` never writes a level.
- **History.** CISO Assistant keeps who changed what and when; the repo keeps why, in each reviewed pull request.
