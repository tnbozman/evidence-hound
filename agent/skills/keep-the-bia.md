---
name: keep-the-bia
description: "Draft and update the business impact analysis in the compliance repo: each system's recovery objectives, dependencies, escalation thresholds and the checks that prove recovery. Use when the person asks how long a system can be down, when a system is added, or when recovery testing needs proving."
---
## Keep the business impact analysis

The guide is [docs/bia.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/bia.md); the file reference is
[docs/scope.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/scope.md#biabiayaml).

1. Each system's security and recovery objectives (`objectives`, `recovery: {rto, rpo, mtd}`) go on the system in `scope/setup.yaml`.
2. One file per BIA under `scope/bia/`, on one risk matrix's impact scale. Each system gets one assessment: what it depends on, the
   controls that keep it running, its recovery flags, and escalation thresholds (`after`, `impact` by level name, `justification`)
   that rise over time.
3. Prove recovery with evidence rather than a ticked box: name the checks with `tested_by` (or `documented_by`, `targets_met_by`). Each
   `hound run` then sets the flag from the latest results: true only when every named check passed. A flag named by checks is never
   also set by hand.
4. Check it: `hound scope validate scope/ --config hound.yaml --json`. It names thresholds that do not rise, impacts the matrix does not
   have, and unknown systems and controls.
5. Show what changes: `hound scope plan scope/ --config hound.yaml --json`. Applying is the person's step.

## Your part and the person's

- **Ask the person** what only they know: how long each system can be down before it hurts, and what happens after 4 hours, a day,
  three days. Ask once, in one short list, and propose thresholds with the reason for each.
- **Never mark a BIA done.** Moving it to `done` is the team's decision after review. CISO Assistant computes the impact over time;
  never write it.
