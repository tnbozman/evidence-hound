---
name: onboard-the-environment
description: Decide which system provides each mitigation of the framework (the capability map), before systems are onboarded. Use when scope/capabilities.yaml is missing or has mitigations undecided.
includes: [environment-capabilities]
---
## Onboard the environment

One decision per mitigation: which system provides it, that it is evidenced by hand, or that it is a gap.

1. List what is undecided, the drafts already waiting and the systems in scope:
   `hound onboard environment pending --json --repo .`
2. Decide each mitigation, one command each:
   `hound onboard environment decide <mitigation> --repo . --json --provided-by <system|manual|gap> --reason "<why>" [--system "<name>"]... [--supplier <ref_id>] [--owner <email>] [--evidence <collector|profile|manual>] [--route <collector or profile>]`
   - `system`: name each system with `--system`. A system not in scope yet is queued for onboarding.
   - `manual`: no product provides it; someone reports on it by hand.
   - `gap`: nothing provides it yet. Say so plainly; a gap is an honest answer, never a failure to hide.
3. Deciding a mitigation again replaces your draft. A decision a person approved is refused; leave it.
4. Repeat `pending` until nothing is left, or say what you could not decide.
5. Once the person has approved the decisions, `hound onboard environment --repo .` writes the rows each system's audit leaves open for another system.

## Your part and the person's

- **Read the person's documents yourself, PDFs included.** Licence lists, architecture diagrams, product datasheets and earlier audits show which product does what. Quote what a decision rests on in `--reason`.
- **Ask the person** what only they know: which products they actually run, who owns each, and which mitigations they know nothing covers. Ask once, in one short list, and wait for the answer.
- **Never approve.** What you record is a draft. The person approves it with `hound approve` in their own terminal.
