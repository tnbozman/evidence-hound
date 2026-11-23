---
name: onboard-the-organisation
description: Start a new compliance repo for an organisation with hound init. Use when the repo has no hound.yaml yet, or the person asks to set up their organisation.
---
## Onboard the organisation

`hound init` writes a new compliance repo: the config, the scope, an Essential Eight audit and its mapping. Give every answer as an option, so nothing is asked:

`hound init <folder> --org "<organisation name>" --code <short code> --url <CISO Assistant API address> --time-zone <IANA zone> --level <1|2|3> --interval <quarter|month> --owner <email> --assistant <claude|copilot|both|none> --json`

1. The folder must be new or empty. Never run `hound init` over an existing repo.
2. Read the JSON answer: the files written and the audit's ref_id.
3. Run `hound scope validate <folder>/scope --config <folder>/hound.yaml --json` and fix what it reports.
4. Tell the person what comes next: the environment ("onboard-the-environment"), then each third party and system.

## Your part and the person's

- **Read the person's documents yourself, PDFs included.** An organisation chart, a policy or an earlier audit report can tell you the organisation's name, who owns compliance and which maturity level it works to.
- **Ask the person** what only they know: the organisation's name as it should appear, the short code for ref IDs, the CISO Assistant address, the time zone, the maturity level, whether the audit runs by quarter or by month, and the compliance owner's email. Ask once, in one short list, and wait for the answer.
- **Never approve.** What you record is a draft. The person approves it with `hound approve` in their own terminal.
