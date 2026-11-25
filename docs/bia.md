# Business impact analysis

This guide is for a GRC team that records how long each system can be down, and what recovery has been proven, in CISO Assistant's
business impact analysis (BIA), with the analysis written and reviewed in the compliance repo. The full file reference is in
[Scope files](scope.md#biabiayaml).

The example compliance repo has one: `examples/compliance-repo/scope/bia/ex-bia-2026.yaml`, with recovery objectives on its systems in
`examples/compliance-repo/scope/setup.yaml`.

## 1. Set each system's objectives

In `scope/setup.yaml`, give a system its security objectives on CISO Assistant's 0 to 4 scale and its recovery objectives as
durations:

```yaml
      - name: Example 365
        objectives: {confidentiality: 3, availability: 2}
        recovery: {rto: 4h, rpo: 1d, mtd: 3d}
```

They are written only when the file declares them, so values set in CISO Assistant are otherwise kept.

## 2. Write the BIA

One file per BIA under `scope/bia/`, on one risk matrix's impact scale (see [Risk studies](risk.md#1-choose-a-matrix-and-threats)).
Each system gets one assessment: what it depends on, the controls that keep it running, its recovery flags, and escalation thresholds
that say how bad an outage becomes over time:

```yaml
  - system: Example 365
    dependencies: [Example laptops]
    controls: [AC-BACKUP]
    recovery: {documented: true, tested_by: [backup.restore_tested], targets_met: false}
    thresholds:
      - {after: 4h, impact: Minor, justification: Staff use phones.}
      - {after: 1d, impact: Major, justification: Customers notice missed replies.}
```

`hound scope validate scope/` checks that thresholds ascend, that each impact is a level of the matrix, and that systems and controls
exist. It warns when an impact falls after an earlier, higher one, because CISO Assistant carries the highest impact forward.

## 3. Let evidence set "recovery tested"

A hand-ticked "tested" box says only that someone ticked it. Name the checks that prove it instead, with `tested_by` (or
`documented_by`, `targets_met_by`). After collecting, each `hound run` sets the flag from every collector's latest results: true only
when every named check passed. It writes only when the value changes, links the checks' evidence to the assessment, and lists what it
set under `recovery` in its JSON. A flag cannot be both set by hand and named by checks (`scope.result_conflict`).

## 4. Plan, review and apply

```console
$ hound scope plan scope/ --config hound.yaml
$ hound scope apply scope/ --config hound.yaml
```

The plan keys an assessment `bia/system` and a threshold `bia/system/after`, such as `EX-BIA-2026/Example 365/1d`. It leaves alone a
flag that runs set from checks.

## What stays in CISO Assistant

- **Computed impacts.** CISO Assistant works out each assessment's impact over time from its thresholds, and carries the highest impact
  forward; the file holds only the thresholds.
- **Recovery flags named by checks.** Each run sets them from the evidence, so neither the file nor `hound scope apply` writes them.
- **Approval.** Moving a BIA to `done` is the team's decision after review; the file records its status, and CISO Assistant keeps who
  changed it and when.
