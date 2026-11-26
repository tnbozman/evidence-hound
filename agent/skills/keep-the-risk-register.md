---
name: keep-the-risk-register
description: "Draft and update the risk register in the compliance repo: risk studies, their scenarios and ratings, the risk matrix and the threats they name. Use when the person asks to add, rate or treat a risk, when a new system or gap brings a new risk, or when a study needs its yearly review."
---
## Keep the risk register

The guide is [docs/risk.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/risk.md); the file reference is
[docs/scope.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/scope.md#risksstudyyaml).

1. Find the study in `scope/risks/`, one file per study. A study is rated on one matrix: read its level names from the library snapshot
   the study's `matrix` names (in `libraries/` or `frameworks/`). Never invent a level name.
2. A threat or matrix no loaded library has goes in the organisation's own library file. After changing it, raise its `version`, then
   check and snapshot it: `hound library validate libraries/<library>.yaml` and
   `hound library snapshot libraries/<library>.yaml --out libraries/<library>.json`.
3. To add a risk, copy a scenario, give it a new `ref_id`, and fill in the systems it affects, its threats, the controls in place
   (`existing_controls`) and planned (`planned_controls`), the current and residual ratings, the treatment and a justification a
   reviewer can check. The residual rating can only be the same or lower than the current one.
4. Check it: `hound scope validate scope/ --config hound.yaml --json`. Fix each problem at the line it names.
5. Show what changes: `hound scope plan scope/ --config hound.yaml --json`. Rating changes read by level name; quote them to the person.
   Applying is the person's step (`hound scope apply`), in the weekly session or when they ask.
6. A gap the organisation will live with for a while is not a lower rating: draft a security exception or a risk acceptance with the
   skill "keep-exceptions-and-acceptances".

## Your part and the person's

- **Ask the person** what only they know: how likely and how bad a risk is for them, who owns it, and what they will do about it. Ask
  once, in one short list. Propose ratings with the reason; never present your rating as theirs.
- **Never accept a risk.** `treatment: accept` asks for a risk acceptance; the approver accepts or rejects it in CISO Assistant. CISO
  Assistant computes each risk level from the ratings, so never write one.
