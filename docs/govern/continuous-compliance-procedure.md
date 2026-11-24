# Continuous compliance procedure

A generic procedure for keeping a compliance programme current between assessments, for a small organisation without a CISO. Adapt
the bracketed values, approve it at the security committee, and keep it with your policies. It applies to every programme in
`scope/setup.yaml`.

## 1. Roles

| Role | Who (small team) | Responsible for |
| --- | --- | --- |
| Authorising official | [CTO or general manager] | Accepting the residual risk of operating the systems in scope; confirming each programme's compliance position each quarter |
| GRC member | [one person, 2 to 8 hours a week] | Running this procedure with an AI agent; the weekly review; preparing reports; recording decisions |
| System owners | [named per system] | Providing manual evidence on time; fixing their systems' gaps |
| Collector operator | [IT lead] | Keeping collectors running and credentials current |
| Security committee | [see its terms of reference] | Monthly oversight, exceptions and risk acceptances within its authority |
| Board | | Risk appetite, endorsement of long or large acceptances, quarterly oversight |

## 2. Evidence and its freshness

Each requirement in an audit of record is answered by a collector, a manual source attested by a person, or a hand-written result in
the audit file. Evidence counts only while it is fresh:

| Evidence | Freshness window | Who refreshes it |
| --- | --- | --- |
| Collector checks | [7 days]: the run schedule, at least weekly | Runs on schedule; the operator fixes failures |
| Manual evidence | The source's interval in the evidence plan ([monthly], [quarterly], [annually]) | The system owner, on CISO Assistant's reminder |
| Supplier assurance | [12 months], or the certificate's expiry if sooner | The GRC member, with the agent's draft review |
| Policies and plans | [12 months] | The owner, approved at committee |

Evidence past its window is reported: a stale collector in the committee report, an overdue task in section 7. A requirement whose
evidence is missing is `not assessed`, never assumed compliant.

## 3. The cycle

| When | What | Record |
| --- | --- | --- |
| Continuously | Collectors run; changes are published to CISO Assistant | Run records; audit history |
| Weekly | Guided review: attest manual evidence, approve drafts, answer challenges | `review/sign-offs/` in the repo |
| Monthly | Snapshot; committee report and meeting; decisions recorded | Snapshot and report on the "Security reporting" control |
| Quarterly | Board report; authorising official confirms positions | Board minutes; comment on the audit of record |
| Annually | Procedure, risk appetite and policies reviewed; independent assessment if required | New revisions of each |

## 4. Reassessment triggers

Outside the cycle, the GRC member reassesses the affected requirements, and tells the committee, when any of these happens:

- A new system, supplier or AI feature enters scope, or one changes how it handles sensitive data (CUI, personal or export-controlled
  data). Onboard it before go-live ([onboarding](../onboarding.md)).
- A significant change to identity, network boundary, backup or endpoint management.
- An incident affecting a system in scope, or a supplier's incident that affects your data.
- A framework or its library changes version, or a customer or prime changes the requirements they ask for.
- A requirement becomes non-compliant and stays so for more than [one month].
- An exception or risk acceptance expires.

## 5. Exceptions and risk acceptances

- Every exception or risk acceptance has an owner, a reason, the compensating controls, an approver with the authority, and an
  **expiry**. None is open-ended.
- The committee may approve up to [12] months. Longer, open-ended, or above appetite needs the board's endorsement.
- Each is recorded in CISO Assistant (security exception or risk acceptance), linked to the requirement, control or risk it covers.
- Sixty days before expiry the committee report lists it. At expiry it is either renewed by a new decision or the gap is fixed.

## 6. AI assistance

The GRC member's agent reads, drafts and recommends; a person approves. No model takes part in collection, and no evidence is recorded
without a person's attestation ([agent-driven onboarding](../design/agent-driven-onboarding.md)). Report figures come from hound, not a
model ([AI-assisted reporting](ai-assisted-reporting.md)).

## 7. Records

CISO Assistant is the system of record. The compliance repo holds the configuration and its history, so every change to scope, mapping
or evidence plan is reviewed by pull request and can be traced.
