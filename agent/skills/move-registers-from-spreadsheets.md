---
name: move-registers-from-spreadsheets
description: Move the organisation's spreadsheet registers (systems, controls, suppliers, an audit workbook) into the compliance repo with hound, reconcile them row by row, and run both for a month. Use when the person starts from spreadsheets.
---
## Move registers out of spreadsheets

The guide is [docs/adopt/migrate-from-spreadsheets.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/adopt/migrate-from-spreadsheets.md). Read it first and follow its order.

1. Ask the person for each register as a file in this repo or a scratch folder: systems and assets, controls, suppliers and contracts,
   and the latest audit workbook. Never copy a register into a public place.
2. Map each row to where it goes, and draft it with one command per row:
   - a system: `hound add system --scope scope --yes --domain "<domain>" --name "<name>" --ref-id <SYS-...>`
   - a control: `hound add control --scope scope --yes --domain "<domain>" --ref-id <AC-...> --name "<name>" --audit <audit> --requirements <ISM-1511,...>`, naming each requirement by its `ref_id`
   - a supplier: the skills "onboard-a-third-party" and "assess-a-supplier" (`hound supplier draft`)
   - an audit workbook: the skill "import-an-audit-workbook" (`hound scope import`)
3. Keep a list of every row you could not place, with the reason. Do not guess a domain, requirement or supplier level.
4. Check the result: `hound scope validate scope --config hound.yaml` must report no problems. Then count: each register's rows against
   what is now in `scope/`, and tell the person any difference.
5. Tell the person what waits on them: supplier drafts and imported rows without an approver are approved with `hound approve` in their
   own terminal; then `hound scope plan` and `hound scope apply`, after a review with the skill "review-before-apply".
6. For the first month, the spreadsheet stays read-only and every change goes through the repo. Say so in your summary.
