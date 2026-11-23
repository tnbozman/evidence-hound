# Design: onboarding driven by the person's own coding agent

Status: Agreed (Nov 23). Changes the [onboarding assistant design](onboarding-assistant.md): the model moves out of `hound` and into the coding agent the person already uses. The build is tasks s09-36 to s09-40 in [the s09 sprint](../sprints/s09-e8-gap-collectors.md).

## Why

The first real onboarding (a Sophos XGS 88w against Essential Eight ML2, [s09-25 evidence](../evidence/s09/s09-25.md)) worked end to end, and showed where the built-in model falls short:

- **It cannot do the research a good pass needs.** It read only the text it was given: no PDFs (the vendor's ISO/IEC 27001 certificate), no response fields behind `$ref` in the API spec (so it missed `firmwareVersion`), no vendor documentation beyond what the person pasted. 46 of 107 requirements fell back to "manual", including MFA, admin accounts, logging and configuration backups that the firewall's own XML API can likely evidence.
- **It cannot iterate.** Profile drafting stopped after 3 rounds with a profile that did not validate; an agent that can run `hound profile validate` and `hound profile test` itself would keep going.
- **It costs a second subscription.** A small business needs an API key, a token budget and `hound onboard config` on top of the Claude Code or GitHub Copilot seat it already pays for.
- **The review was too weak.** About 27 of 60 "not applicable" rows rest on another system ("a vulnerability scanner", "the SIEM") that no audit covers yet, so real gaps showed as closed.

The person running the trial asked for a best-effort pass that a small business can get from tools it already has, and is content for that agent to read their own audit workbook and evidence, then onboard, validate, plan and apply through `hound`.

## Decision

1. **The agent drafts; `hound` decides what is valid; a person approves.** The person's coding agent (Claude Code, GitHub Copilot, or another that reads repo instructions) does the reading, mapping and drafting. `hound` stays deterministic: it validates every file, plans every change, and records every decision with its provenance. Approvals stay with people.
2. **`hound` grows an agent interface,** non-interactive and JSON in and out, so an agent never has to script around prompts:
   - `hound onboard <system> pending --json`: what is still undecided, with requirement text, the API summary and the skills that apply.
   - `hound onboard <system> decide <requirement> --kind <api_check|manual|not_applicable|other_system> --reasoning ... --source ...`: records one decision, validated, marked "drafted by agent".
   - `hound supplier draft ...`, `hound scope import ...` and the existing `hound scope validate|plan --json`, `hound profile validate|test`, `hound explain --json`.
3. **Approvals refuse an agent.** Not-applicable decisions, supplier approval and concluding a supplier review need a person: an interactive prompt on a terminal, or the approver's own commit. A decision drafted by an agent stays a draft until then. (In the trial, Claude Code's own permission system blocked it from approving the supplier; `hound` should enforce the same.)
4. **"Met by another system" is its own state, never not applicable.** The row stays open in this audit, names the system, and closes only when that system's audit shows the requirement met. `hound scope plan` lists such rows as open dependencies.
5. **Imported decisions keep their provenance.** An agent onboarding an existing audit workbook records each result as "imported from `<workbook>`, row `<n>`, approved by `<who>` on `<date>`" as the workbook says, never as approved by the agent. A row without an approver is imported as a draft.
6. **Skills ship for the agents people have.** The existing skills (supplier assessment, applicability, reading an API specification, reviewing a config change, mapping) are packaged as Claude Code skills (`.claude/skills/`) and as `AGENTS.md` plus `.github/copilot-instructions.md` for Copilot, in the compliance repo template that `hound init` writes. A new skill, **import an audit workbook**, covers mapping an existing Excel audit of controls and evidence to scope files.
7. **A stronger review before apply.** A review skill runs `hound explain --json`, compares each decision with the same ISM control under other strategies, and must clear every flag it raises (or the person accepts it) before the agent proposes `hound scope apply`.
8. **The built-in model is frozen, then removed.** No new features use it from today. Once the agent path has onboarded the Sophos firewall again, one task removes the model calls, the Anthropic and OpenAI SDKs, `hound onboard config`, `onboarding.yaml`, the outbound guard and the token budget. Commands keep their deterministic parts (`explain` still prints the flags code finds; `onboard` still records decisions).
9. **`hound agent setup` writes the assistant files.** It asks which assistant the person uses in VS Code and writes them into the compliance repo from one source of skills, and `hound init` offers it at the end. Re-running it updates hound's files and leaves the person's own untouched:

   | Assistant | Files |
   | --- | --- |
   | Claude Code | `CLAUDE.md`; `.claude/skills/<skill>/SKILL.md` for each skill; `.claude/agents/<agent>.md` for the onboarding and review agents |
   | GitHub Copilot in VS Code | Custom agents `.github/agents/<agent>.agent.md` (frontmatter `name`, `description`, `tools`, `skills`; tools by alias: `read`, `search`, `edit`, `execute`), and `.github/copilot-instructions.md` for the rules every agent follows |
   | Both | All of the above, from the same skill text |

   Agents get only the tools their job needs: the review agent has `read`, `search` and `execute` (to run `hound`) but not `edit`, because VS Code warns that an instruction not to edit is no permission boundary. Approvals are enforced by `hound`, not by the agent files.

## Rules that do not change

- **No model in evidence collection.** `hound run`, `hound serve` and every collector stay model-free.
- **Nothing reaches CISO Assistant except through `hound scope plan` and `apply`.**
- **No organisation data in this repository.** The person's workbook, evidence and outputs live only in their private compliance repo. Each skill says so first.

## What changes about data handling

With the built-in model, `hound`'s outbound guard decided what left the machine. With an agent, the agent reads the repository and the files it is given, so the person's agent provider sees them. The docs say so plainly. Secrets stay in files the agent has no need to read (`secrets/`, outside the repo), and the skills tell the agent not to open them.

## Proposed tasks

| Task | What |
| --- | --- |
| Agent interface | `pending`, `decide`, supplier draft, `--json` on validate, plan and explain; approvals refused without a person. |
| Another-system state | Its own result and plan section; environment onboarding uses it instead of not applicable; skips rows the mapping computes. |
| Skills for agents | Package the skills for Claude Code and Copilot in the repo template; a driving skill ("onboard a system with hound") and the review skill. |
| Import an audit workbook | A skill, plus `hound scope import` for the structured part (requirement, result, evidence reference, approver, date), with provenance. |
| Sophos firewall XML API | A reader for the on-device XML API, read-only, so MFA, admin accounts, device access, syslog and backup schedules can become checks. To be checked against Sophos's XML API documentation first. |
| Spec summaries | Resolve `$ref` response schemas so read endpoints list their fields. |
| Trial | Onboard the Sophos firewall again, this time driven by Claude Code with the skills, and compare with the s09-25 run. |

## Open questions

- Can a person's approval be proved by a signed commit alone, or does `hound` also need an interactive confirmation?
