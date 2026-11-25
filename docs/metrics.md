# Metrics and dashboards

This guide is for a GRC team that wants the security committee to see trends, such as MFA coverage or how much of the Essential Eight is
compliant, without anyone typing numbers each month. You declare what you measure in the compliance repo; `hound scope apply` writes it
to CISO Assistant's metrology (metric definitions, metric instances, dashboards and widgets); each `hound run` fills in the values.
The full file reference is in [Scope files](scope.md#metricsyaml).

The example compliance repo has two metrics and a committee dashboard: `examples/compliance-repo/scope/metrics.yaml`.

Start with two or three metrics the committee will act on. Each one is a number someone must explain every month, so a long list costs
more time than it saves.

## 1. Define what you measure

A definition says what is measured and how it reads: a number with a `unit`, which way is better, and the target. Add it to
`scope/metrics.yaml`:

```yaml
definitions:
  - ref_id: EX-MET-MFA-CHECKS
    name: MFA checks passing
    unit: percentage
    direction: higher_is_better
    decimals: 0
    frequency: monthly
    target: 100
```

## 2. Measure it in a domain, from your runs

An instance measures a definition in one domain, with its owners and the controls it tells about. `source: hound` makes each run fill
it in:

```yaml
instances:
  - ref_id: EX-MET-MFA-CHECKS-CORP
    name: MFA checks passing
    definition: EX-MET-MFA-CHECKS
    domain: Example Pty Ltd
    owners: [it@example.com]
    controls: [AC-MFA]
    source: hound
    measure: check_pass_rate
    checks: [identity.mfa.*]
```

| Measure | What it records each period |
| --- | --- |
| `check_pass_rate` | the share of the named checks that pass (`checks: [identity.mfa.*]`) |
| `requirements_compliant_pct` | the share of an audit's applicable requirements that are compliant (`audit: EX-E8-2026`); rows not assessed count against it |
| `evidence_freshness_pct` | the share of the checks (all, or the named ones) whose evidence is at most a week old |
| `collector_success_rate` | the share of the run's collectors that succeeded |

## 3. Add a dashboard

A dashboard lays widgets on a 12-column grid. A widget shows a metric instance, a builtin metric CISO Assistant computes for an audit,
risk study or domain, or text:

```yaml
dashboards:
  - ref_id: EX-DASH-COMMITTEE
    name: Security committee
    domain: Example Pty Ltd
    widgets:
      - {title: MFA checks passing, metric: EX-MET-MFA-CHECKS-CORP, chart: line, time_range: last_year}
      - {title: Essential Eight progress, audit: EX-E8-2026, builtin: progress, x: 6}
```

## 4. Check, review and apply

```console
$ hound scope validate scope/
$ hound scope plan scope/
$ hound scope apply scope/
```

`validate` checks the file offline: the codes it reports are listed in [Scope files](scope.md#metricsyaml). `plan` shows the
definitions, instances, dashboards and widgets it would create or change; review it in the pull request, then `apply` writes them.

## 5. Let the runs fill it in

After collecting, each `hound run` measures every instance with `source: hound` and records one value for the current period. A later
run in the same period replaces it, and an unchanged value is not sent, so the chart shows one point a month. The run's JSON lists
what it recorded under `metrics`. Two warnings name what it could not do: `metric.not_applied` (run `hound scope apply` first) and
`metric.nothing_measured` (no check matched, or the audit has no applicable rows). See
[Metrics filled in by runs](scope.md#metrics-filled-in-by-runs) for the rules.

`hound status --scope scope/` gives each programme's standing from the latest posture snapshot beside the collectors' runs, so the
weekly look needs no dashboard at all; the dashboard is for the committee.

## Metrics people enter, and computed metrics

Not everything comes from a run. A qualitative metric lists its `levels` worst first, such as `[Missing, Drafted, Approved, Reviewed]`,
and someone enters its value in CISO Assistant each period; leave `source` out. A computed metric has a `formula` over other definitions,
and CISO Assistant works it out from their instances in the same domain:

```yaml
  - ref_id: EX-MET-MFA
    name: MFA coverage
    unit: percentage
    formula:
      expression: covered / total * 100
      inputs:
        - {key: covered, definition: EX-MET-MFA-COVERED, combine: sum}
        - {key: total, definition: EX-MET-MFA-TOTAL, combine: sum}
```

A computed metric cannot also be filled in by runs (`scope.result_conflict`): CISO Assistant refuses values typed into it.

## The governance checks behind the numbers

The [`ciso-assistant` profile](collectors/ciso-assistant.md#the-ciso-assistant-profile) checks CISO Assistant itself on every run:
suppliers' assessments and contracts current, risk studies reviewed within a year, approved security exceptions not past expiry, and
critical systems' recovery tested. Name them in a `check_pass_rate` metric, for example `checks: [risk.*, bia.*, supplier.*]`, to chart
how well governance keeps up.

## What stays in CISO Assistant

- **The values.** Samples are never declared in the repo: runs record them, and people enter the ones runs cannot measure. CISO
  Assistant keeps their history, one value per period.
- **Computations.** Formulas and builtin metrics (an audit's progress, a domain's open incidents) are worked out by CISO Assistant.
- **The charts.** Dashboards are drawn and read in CISO Assistant; the repo holds only their layout.
- **Collection tasks.** If someone enters a metric by hand, CISO Assistant's task for it reminds them each period.
