# The security committee

The security committee is where a small organisation without a CISO looks at its security every month, decides what to do and who does
it, and escalates what it cannot fix. It reads one report, [`hound report committee`](../reporting.md), and records every decision back in
CISO Assistant, so the next month's report shows whether it happened.

## Terms of reference (template)

Copy this into your policy set and fill in the brackets.

> **Purpose.** The security committee oversees [Organisation]'s cyber security and compliance programmes ([Essential Eight Maturity
> Level Two], [AI6 Foundations], [others]) on behalf of [the CTO / the board]. It monitors posture each month, confirms remediation
> actions and owners, approves exceptions and risk acceptances within its authority, and escalates to the board what is outside it.
>
> **Members.** The chair ([CTO or general manager]), the authorising official, the GRC member (secretary), the IT lead, and the owners
> of systems on the agenda. A quorum is the chair or authorising official, plus the GRC member.
>
> **Authority.** The committee may accept a risk or approve an exception for up to [12] months. Anything longer, without an expiry, or
> above the risk appetite goes to the board ([board reporting](board-reporting.md)).
>
> **Meetings.** Monthly, [45] minutes, within [10] working days of the month's end. The GRC member circulates the report two working
> days before.
>
> **Records.** Decisions and actions are recorded in CISO Assistant within two working days. The report and minutes are kept as
> evidence on the "Security reporting" applied control.
>
> **Review.** These terms are reviewed every year, or when a programme is added.

## Before the meeting (GRC member, about 30 minutes)

1. Your agent takes the snapshot and drafts the report on the first working day of the month ([reporting](../reporting.md)).
2. Read the summary and section 8 (Actions). Check every proposed owner and due date is realistic; change them in the draft if not.
3. Refine the summary wording with [AI help](ai-assisted-reporting.md), without changing a figure.
4. Send it to the members.

## Agenda (45 minutes)

| Minutes | Item | Report section |
| --- | --- | --- |
| 0 to 5 | Actions from last month: done, late or dropped | Section 8 of last month's report |
| 5 to 10 | Summary and collector health | 1 and 2 |
| 10 to 20 | What changed and what is failing, per programme | 3 |
| 20 to 30 | Exceptions and risk acceptances: new, expiring, expired; decide renewals | 4 |
| 30 to 35 | Remediation and incidents | 5 and 6 |
| 35 to 40 | Overdue manual evidence and supplier reviews: owners overdue a second month are asked why | 7 |
| 40 to 45 | Confirm this month's actions, and anything for the board | 8 |

## Record decisions in CISO Assistant

The committee's decisions only count once they are in CISO Assistant, because that is what the next report reads.

| Decision | Record it as |
| --- | --- |
| A remediation action, with owner and due date | An applied control's ETA and owner, or a finding in the audit's findings assessment |
| Accept a risk | A risk acceptance with approver, date and expiry, linked to the risk scenario |
| Approve an exception to a requirement | A security exception with expiry, linked to the requirement or control |
| Renew an expiring exception | Edit its expiry, and note the meeting date in its description |
| Close an incident | The incident's status, with the date |
| Ask a system owner for overdue evidence | Comment on the task in CISO Assistant; the next report lists it again if still overdue |
| Escalate to the board | An action in section 8 with the board as owner; it appears in the quarter's board report under decisions |

Then upload the minutes as a new revision of the "Security reporting" evidence, next to the month's snapshot.

## Who does what in a small team

| Role | Hours a month | Does |
| --- | --- | --- |
| Chair | 1 | Runs the meeting, decides when the committee disagrees |
| Authorising official | 1 | Accepts risks and approves exceptions within the committee's authority |
| GRC member (secretary) | 2 to 3 | Prepares the report with the agent, records decisions in CISO Assistant |
| IT lead | 1 | Answers for collectors, remediation and incidents |
| System owners | as needed | Attend when their system or evidence is on the agenda |

Training for members: [committee session](../training/committee.md). A member's own guide: [reading the committee report](../operate/committee-member.md).
