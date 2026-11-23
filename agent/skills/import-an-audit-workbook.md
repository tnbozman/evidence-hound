---
name: import-an-audit-workbook
description: Import an existing audit workbook (Excel or CSV of requirements, results and evidence) into an audit in the scope with hound scope import. Use when asked to import, migrate or move an audit from a spreadsheet.
includes: [essential-eight-ml2-mapping]
---
## Import an audit workbook

The workbook is the person's record of an audit so far. `hound scope import` reads it where it is and writes each result to the audit, keeping who approved it.

1. The audit must be in `scope/audits/` already. If it is not, use the skill "onboard-an-audit" first and have the person approve it.
2. Open the workbook and find the headings for: the requirement (its ID in the framework), the result, the observation, the evidence reference, who approved it and when. Show the person the column mapping you propose before you import.
3. Import, naming each heading that hound does not recognise by itself:
   `hound scope import <workbook.xlsx or .csv> --repo . --audit <audit ref_id> [--sheet "<sheet>"] --column requirement="<heading>" --column result="<heading>" --column observation="<heading>" --column evidence="<heading>" --column approved_by="<heading>" --column approved_on="<heading>" --json`
4. Read the answer and report every part of it:
   - `imported`: rows the workbook says someone approved. Each is written with "Imported from <workbook>, row <n>, approved by <who> on <date>", exactly as the workbook records it.
   - `drafts`: rows with no approver. They wait for the person, who approves each with `hound approve`.
   - `unmatched`: rows whose requirement is not in the audit's framework. Never guess a match; list them for the person.
   - `already_assessed` and `not_assessed`: rows left as they are.
5. Copy what the workbook says; do not improve it. Never fill in an approver, a date or a result the workbook does not hold, and never edit the workbook.
6. Never commit the workbook or the evidence files it links to: evidence stays outside the repo.
7. Run `hound scope validate scope/ --config hound.yaml --json` and fix what it reports. A result that conflicts with one a check computes (`scope.result_conflict`) is taken back out of the audit file and listed for the person.

## Your part and the person's

- **Read the person's documents yourself, PDFs included.** The workbook itself, and any audit report or sign-off page with it, tell you which column means what and who approved the results.
- **Ask the person** what only they know: which audit the workbook belongs to, what a heading or an abbreviation means, and who approved a row the workbook leaves blank. Ask once, in one short list, and wait for the answer.
- **Never approve.** What you record is a draft. The person approves it with `hound approve` in their own terminal.
