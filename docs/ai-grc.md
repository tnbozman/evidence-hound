# AI governance (AI6) in Evidence Hound

This is the reference for how Evidence Hound and CISO Assistant support AI governance with the Australian Government's *Guidance for AI Adoption* (AI6). New to AI governance? Read the [primer](ai-grc-primer.md) first, then follow [the walkthrough](tutorials/ai-governance.md) on the local
stack; [the training outline](training/ai-governance.md) turns it into a 30-minute team session.

## What AI6 asks, and where each part lives

AI6 asks six things: decide who is accountable, understand the impacts, measure and manage the risks, share essential information, test
and monitor, and keep people in control. Evidence Hound handles them with what CISO Assistant already has, plus your repo:

| AI6 asks for | Kept in | How it is checked |
| --- | --- | --- |
| A register of AI systems, each with an owner | The `ai` block of each AI system in `scope/setup.yaml`, shown in CISO Assistant as an asset labelled `ai-system` | `ai-register` collector |
| Screening into a risk tier, and an impact assessment before a medium or high tier system goes live | The same `ai` block: `tier`, `screened_on`, `impact_assessment`, `impact_assessed_on`, `in_use_from` | `ai-register` collector |
| The risks of medium and high tier systems | Risk scenarios in CISO Assistant, linked to the system's asset | `ciso-assistant` profile |
| Human oversight | `oversight` and `oversight_note` in the `ai` block | `ai-register` collector |
| Testing, and testing again in proportion to risk | `last_tested_on` in the `ai` block, with the test result as evidence | `ai-register` collector |
| Suppliers of third-party AI | The supplier's solution, entity assessment and contract in CISO Assistant (as for any supplier) | `ciso-assistant` profile |
| Policy, training, redress, disclosure, monitoring and incident reviews | Records a person keeps and attests | `ai-governance` (yearly) and `ai-operations` (quarterly) manual sources |
| The self-assessment | An audit of the AI6 framework, group `F` (Foundations) | `mappings/ai6.yaml` turns every check into AI6 results |

CISO Assistant's custom fields are an Enterprise feature, so the register entry lives in the repo and reaches CISO Assistant as
filtering labels (`ai-tier-medium`, `ai-use-third-party`, `ai-oversight-on-the-loop`, `ai-retired`) and as lines in the asset's
description. Filtering the asset list by `ai-system` gives the register; CISO Assistant's own Excel export saves it.

## Who sets each field

Everything in the `ai` block is changed by pull request, so each change is reviewed. The [scope guide](scope.md#ai-systems) lists the
keys and what `hound scope validate` reports.

| Field | Set by | When |
| --- | --- | --- |
| `tier`, `screened_on` | The GRC team, from the [screening runbook](../examples/compliance-repo/runbooks/ai-screening.md) | When a system is proposed, and again when its use changes |
| `use` | The GRC team | At screening |
| `oversight`, `oversight_note` | The system's owner, reviewed by the GRC team | At screening, and when the arrangement changes |
| `impact_assessment`, `impact_assessed_on` | The GRC team, once the assessment is approved | Before a medium or high tier system goes live |
| `in_use_from`, `retired_on` | The system's owner | At go-live and when the system is switched off |
| `last_tested_on` | The system's owner, with the test result kept as evidence | Before go-live, then at least every 12 months (6 for high tier) |

A third-party AI system also names its supplier's `solution`, and its supplier is approved before the system is registered, as for any
other system.

## The checks

The [collector guide](collectors/ciso-assistant.md) lists each check and when it passes. In short: the `ai-register` collector checks the
entries in your repo every night (owners, screening, impact assessments before use, oversight, test dates) and the `ciso-assistant` profile
checks what only CISO Assistant holds (risk scenarios, supplier assessments and contracts). Every observation names the AI6 sub-practice
in words and says what to do. Checks a person attests come from two manual sources, copied from
[`examples/manual-sources/ai6/`](../examples/manual-sources/ai6/): `ai-governance` once a year and `ai-operations` each quarter.

## Adopting at Foundations

1. Add the AI6 library to `scope/setup.yaml` (as `examples/compliance-repo/scope/setup.yaml` does), an audit for group `F`, and an `ai6`
   programme with `register: { of: ai_systems, by: [tier, use] }`.
2. Register each AI system you use with an `ai` block, after screening it.
3. Add the `ai-register` collector, the `ciso-assistant` profile and the two manual sources to `hound.yaml`, and `mappings/ai6.yaml` as a
   target for the AI6 audit.
4. Add evidence plan rows for the manual sources, so their owners are reminded.
5. `hound scope plan`, review, `hound scope apply`, then `hound run`.

If your team uses its own coding agent with `hound onboard` or `hound manual propose`, that agent is AI use too: screen it and register it
like any other AI system (usually low tier, human in the loop, because a person approves every proposal).

The example compliance repo has all of this for two fictional AI systems; one is deliberately not tested yet, so a run shows AI6-4.1
(the register) met and AI6-5.1 (testing before use) not met.

## Moving to Implementation

Change the audit's and the programme's group from `F` to `I`. AI6-4.4 (AI-generated content transparency) joins the audit; it is filled
by `ai.generated_content_labelled` from the `ai-governance` manual source. Implementation expects more depth in each record a person
attests: read the *Implementation practices* for each sub-practice and widen your runbooks.

## What Evidence Hound does not do

- It does not test, evaluate or monitor a model. `last_tested_on` records that a test was done; the test itself is yours.
- It has no AI incident workflow. AI incidents go through your incident process; the `ai-operations` source attests they were reviewed.
- It does not screen a system for you. Screening is a person's judgement, recorded in the repo and reviewed by pull request.
- It has no model of its own. An agent may draft a register entry or a manual result; a person reviews it before it counts.

## The AI6 framework library

[`frameworks/ai6.yaml`](../frameworks/ai6.yaml) is a CISO Assistant library for AI6, and [`frameworks/ai6.json`](../frameworks/ai6.json) its snapshot.

- **Source.** *Guidance for AI Adoption: Foundations* and *Implementation practices*, National AI Centre, v1.0, October 2025.
- **Text.** The guidance states no licence, so the library carries the practice and sub-practice titles with short descriptions in our own words and a link to the guidance. It will carry the full text only if the guidance is published under CC BY 4.0.
- **Structure.** The six practices are headings (`AI6-1` to `AI6-6`, not assessed). The 21 Implementation sub-practices (`AI6-1.1` to `AI6-6.2`) are the assessable rows.
- **Groups.** `I` (Implementation) holds every sub-practice. `F` (Foundations) holds a sub-practice when at least one Foundations action falls under it (table below). An audit for group `F` shows only those rows. Only `AI6-4.4` (AI-generated content transparency) has no Foundations action.
- **Versions.** The library's `version` goes up with every content change, with a note at the top of the file. CISO Assistant keeps a framework loaded from an older library, and `hound scope validate` reports it rather than upgrading silently.

## Foundations actions traced to sub-practices

Foundations has no numbered sub-practices: each practice has "Getting started" and "Next steps" actions, 30 in all. Each is traced to the one Implementation sub-practice it falls under. The actions are summarised in our own words; the numbers (F1.1 to F6.4) are ours, in the order the guidance lists them.

| Action | Foundations part | Action, in brief | Sub-practice |
| --- | --- | --- | --- |
| F1.1 | Getting started | Name a senior leader as the overall owner of AI governance. | AI6-1.1 |
| F1.2 | Getting started | Write an AI policy for responsible use, with advice to staff on the risks. | AI6-1.4 |
| F1.3 | Next steps | Make one named person accountable for each AI system in use. | AI6-1.1 |
| F1.4 | Next steps | Train the accountable people to decide well about AI risks. | AI6-1.3 |
| F1.5 | Next steps | Agree responsibilities with vendors, developers and integrators. | AI6-1.2 |
| F1.6 | Next steps | Grow the AI policy into a governance framework of policies and procedures. | AI6-1.4 |
| F2.1 | Getting started | Assess who an AI system affects, with care for vulnerable groups. | AI6-2.1 |
| F2.2 | Getting started | Give people a way to challenge AI decisions and have harms put right. | AI6-2.2 |
| F2.3 | Next steps | Engage the people affected through the system's life. | AI6-2.1 |
| F2.4 | Next steps | Watch feedback for patterns of unwanted impact. | AI6-2.3 |
| F3.1 | Getting started | Screen AI systems and uses to flag those that are unacceptable or need more governance. | AI6-3.2 |
| F3.2 | Next steps | Fit risk processes to the kind of AI: traditional IT, narrow, general-purpose or agentic. | AI6-3.1 |
| F3.3 | Next steps | Assess the risks of each use and plan how to reduce them. | AI6-3.2 |
| F3.4 | Next steps | Apply controls in proportion to each use's risk. | AI6-3.3 |
| F3.5 | Next steps | Investigate and learn from AI incidents. | AI6-3.4 |
| F4.1 | Getting started | Keep a register of every AI system, built or bought, including AI inside other tools. | AI6-4.1 |
| F4.2 | Getting started | Tell people when AI makes or influences decisions, or could be mistaken for a person. | AI6-4.2 |
| F4.3 | Next steps | Explain what each AI system can and cannot do. | AI6-4.2 |
| F4.4 | Next steps | Be open along the supply chain about components and data sources. | AI6-4.3 |
| F4.5 | Next steps | Explain AI outcomes to the people affected, in proportion to the stakes. | AI6-4.2 |
| F5.1 | Getting started | Ask suppliers for proof their AI system was tested. | AI6-5.1 |
| F5.2 | Getting started | Test before deployment. | AI6-5.1 |
| F5.3 | Getting started | Monitor after deployment in proportion to the risk. | AI6-5.2 |
| F5.4 | Getting started | Extend data governance and cyber security to cover AI. | AI6-5.4 |
| F5.5 | Next steps | Stress-test the system, including attempts to get around its controls. | AI6-5.3 |
| F5.6 | Next steps | Consider independent testing for uses that need more governance. | AI6-5.3 |
| F6.1 | Getting started | Give meaningful human oversight matched to the system's autonomy and stakes. | AI6-6.1 |
| F6.2 | Getting started | Build in points where a person can pause, override, roll back or shut down the system. | AI6-6.1 |
| F6.3 | Next steps | Train the people who oversee AI on when to step in. | AI6-6.1 |
| F6.4 | Next steps | Keep another way to do critical work if the AI fails or is retired. | AI6-6.2 |
