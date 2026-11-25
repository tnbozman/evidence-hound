---
name: keep-the-metrics
description: "Define the metrics and dashboards the security committee watches, measured by hound runs where possible. Use when the person asks for a trend, a KPI or a committee dashboard, or when a run warns metric.not_applied or metric.nothing_measured."
---
## Keep the metrics

The guide is [docs/metrics.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/metrics.md); the file reference is
[docs/scope.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/scope.md#metricsyaml).

1. Start with two or three metrics the committee will act on; each is a number someone explains every month.
2. In `scope/metrics.yaml`, a definition says what is measured (unit, which way is better, target, frequency). An instance measures it
   in one domain with its owners and controls; `source: hound` with a `measure` (`check_pass_rate`, `requirements_compliant_pct`,
   `evidence_freshness_pct` or `collector_success_rate`) makes each run fill it in. A dashboard lays widgets on a 12-column grid.
3. Check and show the change: `hound scope validate scope/ --config hound.yaml --json`, then
   `hound scope plan scope/ --config hound.yaml --json`. Applying is the person's step.
4. After the next run, read its JSON `metrics` and warnings: `metric.not_applied` means the scope is not applied yet;
   `metric.nothing_measured` means no check matched or the audit has no applicable rows. Say which, and what fixes it.

## Your part and the person's

- **Never write a value.** Values come from runs or from people in CISO Assistant; the file holds only what is measured.
- **Ask the person** which numbers the committee wants to see and what target each has. Ask once, in one short list.
