# Reporting to the board

The board sets how much cyber risk the organisation will accept, and holds management to it. Once a quarter it reads a two-page report,
[`hound report board`](../reporting.md), confirms each programme's compliance position, and endorses the risks management has accepted
on its behalf.

## Risk appetite statement (template)

The board approves this once a year. The committee and the authorising official work within it.

> [Organisation] holds [customer and defence contract data, some of it CUI] and relies on [Microsoft 365, the engineering file share].
> We have a **low** appetite for risks to the confidentiality of customer and contract data, and to our ability to keep contracts
> that require [Essential Eight Maturity Level Two / CMMC Level 2].
>
> - We do not accept any risk rated **high** or above after treatment for longer than [3] months without the board's endorsement.
> - Exceptions and risk acceptances are time-limited. The committee may approve them for up to [12] months; anything longer, or without
>   an expiry, needs the board's endorsement.
> - We aim to meet every requirement of our audits of record; a programme more than [25]% non-compliant is reported off track.
> - We have a **moderate** appetite for operational risk in internal systems where a failure would cause delay but no data loss.

Record the approved statement in CISO Assistant as a policy (an applied control in the policy category), with the board minutes as its
evidence, and set the matching risk matrix levels so the board report's "above appetite" mark agrees with it.

## How the monthly reports roll up

| Board report section | Comes from |
| --- | --- |
| 1. Compliance position | The last month of the quarter, per programme, by fixed rules ([reporting](../reporting.md#3-the-board-report)) |
| 2. Posture and trend | The snapshots of the last month of this quarter and the three before it |
| 3. Key risks | The top five risk scenarios by level against appetite, with owner, treatment and target date |
| 4. Risk-acceptance decisions | Acceptances and exceptions the committee made during the quarter, with who and when |
| 5. Assurance | Automated versus manual coverage, evidence freshness, independent review, supplier assurance |
| 6. Decisions and asks | The positions to confirm, endorsements and recovery plans, generated; the committee's escalations, added by hand |

Nothing in the body is operational: no collector failure or task appears there. They stay in the committee reports and the appendix.

## Before the meeting

1. The agent drafts the report on the first working day of the quarter.
2. The authorising official reads section 1 and confirms, or changes, each proposed position with a reason. A changed position is
   written into the report with the reason, never by editing a figure.
3. The authorising official adds the committee's escalations of the quarter (actions with the board as owner in the committee reports) to
   section 6, and the chair of the security committee checks none is missing.
4. The report goes to the board with its papers.

## Record the board's decisions in CISO Assistant

| Decision | Record it as |
| --- | --- |
| Confirm a compliance position | A comment on the audit of record, with the meeting date, and the report uploaded as a revision of the "Security reporting" evidence |
| Endorse a risk acceptance or exception | Add the board as approver in its description with the meeting date, and keep or shorten the expiry |
| Decline one | Set it to rejected, and add a remediation action with an owner and due date |
| Change the risk appetite | A new revision of the risk appetite policy's evidence, with the minutes |
| Ask for an independent review | A task in CISO Assistant for the authorising official, with a due date |

Training for board members: [board session](../training/board.md).
