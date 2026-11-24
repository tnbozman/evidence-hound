# AI governance (AI6) in Evidence Hound

This is the reference for how Evidence Hound and CISO Assistant support AI governance with the Australian Government's *Guidance for AI Adoption* (AI6). New to AI governance? Read the [primer](ai-grc-primer.md) first.

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
