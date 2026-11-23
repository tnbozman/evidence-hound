---
name: onboard-a-system-with-hound
description: Onboard one system into the compliance repo with hound, deciding for each requirement how it is evidenced. Use when asked to onboard, register or add a system or product.
includes: [applicability, environment-capabilities]
---
## Onboard a system with hound

You draft the whole onboarding with commands that ask nothing. `hound` checks each step. Nothing you record is approved or applied.

1. **The supplier first.** A system is registered only once its supplier is approved. If no file in `scope/third-parties/` declares the supplier and its solution, use the skill "assess-a-supplier".
2. **Start the session and list what is undecided:**
   `hound onboard <system> pending --json --repo . --group <level> --spec <api specification> --owner <owner email> --product "<product>" --solution <solution ref_id>`
   Ask the person for the owner, the product and the maturity level if you do not know them. Later calls need only `hound onboard <system> pending --json --repo .`.
3. **Read the answer.** It lists each undecided requirement with its text, a summary of the API, and the skills that apply, including the organisation's own. Follow those skills.
4. **Decide each requirement, one command each:**
   `hound onboard <system> decide <requirement> --repo . --json --kind <api_check|manual|not_applicable|other_system> --reasoning "<why>"`
   - `api_check`: add `--source "<METHOD /path>"` for each API operation it rests on. Use the skill "read-an-api-specification".
   - `manual`: the owner provides the evidence by hand. Say what and how often.
   - `not_applicable`: add `--justification "<the fact that makes it so>"`. It stays a draft until a person approves it.
   - `other_system`: add `--other-system <name>` when another system provides it.
   Name the requirement exactly as `pending` lists it under `requirement`. If `hound` refuses a decision, read the problem and fix the decision; do not work around it.
5. **Repeat `pending`** until nothing is left. Leave a requirement undecided, and say so, when you cannot tell.
6. **Hand over.** Tell the person to run `hound approve --repo .` in a terminal, then `hound scope validate`, `hound scope plan` and `hound scope apply`. For the accepted API checks, the skill "write-a-profile" comes next.
