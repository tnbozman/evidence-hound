---
name: import-an-audit-workbook
description: Map an existing audit workbook (Excel or CSV of controls, requirements and evidence) to the repo's scope files. Use when asked to import, migrate or move an audit from a spreadsheet.
includes: [essential-eight-ml2-mapping]
---
## Import an audit workbook

The workbook is the person's record of an audit so far. You map it to scope files; `hound` validates them; a person reviews the change.

1. **Read the workbook where it is.** Ask for a CSV export of each sheet if you cannot read the file. Never commit the workbook or any evidence file it links to; evidence stays outside the repo.
2. **Find the columns:** the requirement (its ID in the framework), the system it is about, the control or practice that meets it, the evidence and where it comes from, the owner, the result and its date. Show the person the column mapping you propose before you write anything.
3. **Match each row to a requirement** in the framework snapshot under `frameworks/` by its ID. List the rows you could not match; never guess a match.
4. **Write scope, not results a check computes:**
   - a system the workbook names goes through the skill "onboard-a-system-with-hound", so its supplier is approved first;
   - a control goes in `scope/controls.yaml`, once, with its owner;
   - a row that says how a requirement is evidenced becomes a decision (`hound onboard <system> decide ...`): `manual` with who provides what and how often, `api_check`, `other_system`, or `not_applicable` with the workbook's stated reason as the justification;
   - a result the workbook records by hand goes under the audit's `assessments` only when no check computes that requirement.
5. **Copy what the workbook says; do not improve it.** A blank result stays not assessed. A "not applicable" without a reason stays undecided, and you list it for the person.
6. Run `hound scope validate scope/ --config hound.yaml --json` and fix every problem. Then give the person a short summary: rows imported, rows left out and why, and what waits for `hound approve`.
