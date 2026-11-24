# Runbook: AI governance records

AI6 (Guidance for AI Adoption): the governance records CISO Assistant does not hold. No collector reads these, so a person
exports them, an agent may propose a result with `hound manual propose`, and the owner attests it with `hound manual approve` at
a terminal. The next `hound run` publishes the result to every AI6 row below. CISO Assistant emails this runbook to the owner
before each due date when an evidence plan row points at it (docs/manual-evidence.md). New to AI6? Read docs/ai-grc-primer.md.

## What to export

1. The AI policy, with who approved it and when, and the senior person named as accountable for AI.
2. The training record for the people who own, use or oversee AI systems: who was trained, on what, and when.
3. How people raise a concern about an AI-made or AI-assisted outcome, and how a person reviews it.
4. The risk method the AI policy points to: how AI systems are screened and how their risks join the risk register.
5. Where the organisation tells people it uses AI, and how AI-generated content is labelled where that matters.
6. For each AI system retired this year: when, who decided, and what replaced it.

Do not send passwords, keys or personal details beyond names and roles. Export documents and records, not screenshots, where you
can: an export can be read again next time and compared.

## How often

Every year, and when the AI policy changes. The source is overdue 366 days after the last attested file.

## Who attests

The AI governance owner: the senior person accountable for AI (AI6-1.1). An agent may read the export and propose a result for
each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source ai-governance <file>`. The GRC team checks the file is complete, records it
against the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `ai.senior_owner_named` | AI6-1.1 Accountable people | A senior person is named as accountable for AI overall. |
| `ai.training_current` | AI6-1.3 AI literacy and training | The people who own, use or oversee AI systems were trained in the last year. |
| `ai.policy_approved` | AI6-1.4 AI governance framework, AI6-3.1 Risk management framework | An AI policy is approved and reviewed in the last year. |
| `ai.redress_process_in_place` | AI6-2.2 Feedback and redress processes | People can raise a concern about an AI outcome and have a person review it. |
| `ai.risk_method_documented` | AI6-3.1 Risk management framework | The policy says how AI systems are screened and how their risks are recorded. |
| `ai.use_disclosed` | AI6-4.2 Transparency and explainability | People are told where AI is used, and what it can and cannot do, where it affects them. |
| `ai.generated_content_labelled` | AI6-4.4 AI-generated content transparency | AI-generated content is labelled where it could be mistaken for a person's work. |
| `ai.retirement_recorded` | AI6-6.2 Decommission when appropriate | Each AI system retired has a record of the decision and what replaced it. |
