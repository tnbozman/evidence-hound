## What this repo is

This is a compliance repo for Evidence Hound (`hound`). Its files say what CISO Assistant should hold: systems, suppliers, audits, controls and the checks that gather evidence. `hound` validates every file, plans every change and applies it. You read, map and draft; `hound` decides what is valid; a person approves.

## How to work here

- Use the hound skills for the job in hand: onboard the organisation, the environment, a third party, a system or an audit; assess a supplier; read an API specification; import an audit workbook; write a profile; process manual evidence; prepare the weekly review; work through pending decisions with the person; review before apply; operate the collectors; build the SSP; respond to an incident; keep frameworks and libraries current; prepare the reports.
- The skill "hound-commands" lists every hound command and its options, generated from the hound installed here. Check a command there before you run it, and never run one it does not list.
- Run `hound` commands with `--json` where they offer it and read the result; never guess what a command did.
- After every change run `hound scope validate scope/ --config hound.yaml --json` and fix every problem it reports. A problem has a file, a line, a code and a hint. Never weaken a rule to make a problem go away.
- What you record is a draft. Finish by telling the person what is waiting for them: `hound review` (or `hound approve`) in a terminal, after going through each decision with them if they want (skill "work-through-pending-decisions"), then `hound scope plan scope/ --config hound.yaml` to read, then `hound scope apply`.
- Say what you could not find out. Never invent a fact about a system, a supplier or a requirement; ask the person or leave the item undecided.
- Keep changes small and on one subject, so a reviewer can follow them.

## Layout

- `hound.yaml`: collectors, schedules, the target audit and its mapping.
- `scope/setup.yaml`: frameworks, domains, perimeters and systems. `scope/audits/`: one file per audit. `scope/third-parties/`: one file per supplier. `scope/capabilities.yaml`: which system provides each mitigation.
- `onboarding/<SYSTEM>/session.json`: the decisions drafted for a system, with who drafted and who approved each. `onboarding/drafts/`: drafted audits and imported rows waiting for `hound approve`.
- `profiles/`: REST profiles and their recorded tests. `mappings/`: which checks satisfy which requirements. `frameworks/`: framework snapshots.
- `skills/`: the organisation's own guidance, which `hound onboard <system> pending --json` returns with the defaults.
