# Move your registers out of spreadsheets

This guide moves a small team's spreadsheet registers into a compliance repo, so that CISO Assistant is filled from configuration you
can review, and the spreadsheets can be retired. Your AI agent does the typing, row by row; you read what it proposes and approve it.
Plan on two to three hours of your time spread over a week, plus one month of running both side by side.

It uses the fictional registers in [`examples/migration/`](../../examples/migration/README.md) and the
[example compliance repo](../../examples/compliance-repo/README.md). Try it there first.

## What moves where

| Your spreadsheet | Moves into | With |
| --- | --- | --- |
| Systems and assets | `scope/setup.yaml`, under the domain | `hound add system` |
| Security controls | `scope/controls.yaml`, linked to the requirements each supports | `hound add control` |
| Suppliers and contracts | `scope/third-parties/`, drafted, then approved by you | `hound supplier draft`, then `hound approve` |
| Audit results (an Essential Eight assessment workbook) | The audit in `scope/audits/` | `hound scope import` ([details](../onboarding.md)) |
| Evidence you collected by hand | A manual source, attested by you each period | [manual evidence](../manual-evidence.md) |
| Risks, exceptions and incidents | CISO Assistant itself, for now | its own forms or its Data Wizard import ([CISO Assistant docs](https://intuitem.gitbook.io/ciso-assistant)) |

Risks, exceptions and incidents stay in CISO Assistant until release 2 brings them into configuration. The reports read them from there
either way.

## Before you start

1. Make a compliance repo with `hound init` and set up your agent with `hound agent setup` ([first deployment](../tutorials/first-deployment.md)).
2. Export each register to CSV, one file per register, with a heading row. Keep the exports outside the repo if they hold anything you
   would not commit. Your agent reads CSV directly.
3. Tidy the headings so each column says one thing. The sample files show the columns each command needs:

| Register | Columns |
| --- | --- |
| Systems (`systems.csv`) | `name`, `ref_id`, `type` (primary holds the data or business process, supporting does not), `owner` (an email), `description`, `parents` (the systems it supports, comma separated) |
| Controls (`controls.csv`) | `ref_id`, `name`, `owner`, `category` (policy, process, technical, physical or procedure), `systems`, `requirements` (such as `ISM-1511,ISM-1515`) |
| Suppliers (`suppliers.csv`) | `ref` (`SUP-...`), `name`, `kind` (vendor, reseller or msp), `solution_ref`, `solution_name`, `contract_ref`, `contract_name`, `description`, `contract_end`, `data` (sensitive data the solution handles, as `kind:role:detail`, such as `cui:transmits:VPN traffic`; empty if none) |

If a control in your spreadsheet does not say which Essential Eight requirement it supports, ask your agent to suggest one from the
requirement texts in `frameworks/essential-eight-2023.json`, and check each suggestion yourself.

## Move them in

Ask your agent: "Move the registers in `<folder>` into this repo with the commands in
<https://github.com/tnbozman/evidence-hound/blob/main/docs/adopt/migrate-from-spreadsheets.md>. One
command per row, systems first, then controls, then suppliers. Stop at the first error and tell me." For the sample files it runs:

```console
$ hound add system --scope scope --yes --domain "Example Pty Ltd" --name "Example engineering file share" --ref-id SYS-ENG-FILES \
    --type primary --owner eng@example.com --description "Drawings and specifications for customer contracts, some marked CUI." \
    --audit EX-E8-2026
$ hound add control --scope scope --yes --domain "Example Pty Ltd" --ref-id AC-ENG-BACKUP --name "Back up the engineering file share daily" \
    --owner eng@example.com --category technical --systems "Example engineering file share" --audit EX-E8-2026 --requirements ISM-1511,ISM-1515
$ hound supplier draft --ref SUP-EXAMPLE-CAD --name "Example CAD Software Ltd" --kind vendor --solution-ref SOL-EXAMPLE-CAD \
    --solution-name "Example CAD" --contract-ref CTR-EXAMPLE-CAD --contract-name "Example CAD subscription" \
    --description "Makes the CAD software the engineers use." --end 2027-06-30 --json
$ hound supplier draft --ref SUP-EXAMPLE-NETOPS --name "Example Network Services Pty Ltd" --kind msp --solution-ref SOL-EXAMPLE-NETOPS \
    --solution-name "Managed firewall" --contract-ref CTR-EXAMPLE-NETOPS --contract-name "Managed firewall agreement" \
    --description "Manages the office firewall and VPN for us." --end 2026-12-31 --data "cui:transmits:VPN traffic to the engineering file share" --json
```

Systems go first because controls name them. Suppliers come last here only because their drafts wait for your approval; when you later
onboard a new system, its supplier is approved first ([onboarding](../onboarding.md)). A system that holds CUI is the start of your CUI
boundary: keep it small, and record which suppliers handle the data with `--data` ([third parties](../third-parties.md)). After the
approval, `hound supplier list --data cui` lists them, which is what a prime asks first.

Then you approve the supplier drafts, yourself, in a terminal: `hound approve`. Your agent cannot do this for you.

## Reconcile

Before anything reaches CISO Assistant, check that every row arrived. For the sample registers the counts are:

| Register | Rows | Check |
| --- | --- | --- |
| Systems | 4 | each `ref_id` appears in `scope/setup.yaml` |
| Applied controls | 4 | each `ref_id` appears in `scope/controls.yaml` |
| Suppliers | 3 | each `ref` has an approved file in `scope/third-parties/` |

Then:

1. `hound scope validate scope --config hound.yaml` reports no problems.
2. `hound scope plan scope --config hound.yaml --out plan.json` lists one create per row, and nothing you did not expect.
3. `hound explain plan.json` reads the plan back in plain English. Steps 2 and 3 need CISO Assistant; without it, `git diff | hound explain`
   reads the change to the repo instead, but says less about systems and controls than the plan does. Read it, and compare it with the spreadsheet: same names, owners
   and requirements.
4. `hound scope apply scope --config hound.yaml` writes it to CISO Assistant.
5. In CISO Assistant, open the audit and check that the controls appear against the requirements you expected.

Ask your agent to do steps 1 to 3 and give you a table of spreadsheet rows against plan lines; you check the table, then run step 4.

## Run both for one month

Keep the spreadsheets for one reporting month, read only, so nobody edits both:

- Make every change in the repo, by pull request, and let `hound scope apply` carry it to CISO Assistant.
- At the month's end, take a snapshot and the committee report ([reporting](../reporting.md)) and compare it with what the spreadsheet
  would have said. Note every difference and its cause.
- When the differences are explained, the compliance owner signs off: record the decision on the "Security reporting" applied control
  in CISO Assistant (an evidence revision with the month's report and the note "spreadsheets retired"), then archive the spreadsheets.

## When something goes wrong

| Problem | What to do |
| --- | --- |
| `hound add system` says `the file has nothing at 'domains[name=...].systems'` | The domain is unknown: name it exactly as in `scope/setup.yaml`, or add it there first. |
| `hound add control` says a requirement is unknown | Use the requirement's `ref_id` from the framework snapshot, such as `ISM-1511`, not a description. |
| A supplier draft fails on `--ref` | Ref IDs start with `SUP-` and use capitals, digits and dashes. |
| `hound scope plan` wants to change something you did not touch | Someone edited CISO Assistant directly. Run `hound scope export` to see it, then decide which copy is right. |
