# Reporting: snapshots, the committee and the board

hound reports in two tiers, built only from CISO Assistant and hound's own run store:

- **Monthly, to the security committee** (operational): what changed, what is failing, what is overdue, and actions to confirm.
- **Quarterly, to the board** (governance): one compliance position per programme, the trend over four quarters, key risks, the risks
  accepted on the board's behalf, and the decisions asked of it.

Both are plain Markdown. Every figure links back to its CISO Assistant object, and every report is a function of saved snapshots, so a
past month's report can be produced again and says the same thing.

## 1. Take the month's snapshot

On the first working day of each month, for the month just ended:

```console
$ hound report snapshot --period 2026-11 --config hound.yaml --control AC-REPORTING
wrote /var/lib/evidence-hound/evidence/reports/posture-2026-11.json: 2 programmes, 168 requirement rows
uploaded hound-report/posture-2026-11 to applied control AC-REPORTING
```

- It reads each programme in `scope/setup.yaml` (framework, groups and audit of record), the requirement results of each audit, applied
  controls, risk acceptances and security exceptions, findings, incidents, risk scenarios, tasks and supplier reviews, and the latest run
  of each collector.
- It writes the snapshot to `reports/` in the evidence store. With `--control`, it also uploads it as evidence on that applied control,
  so the record of what you reported lives in CISO Assistant. Create the control once:
  `hound add control --scope scope --yes --ref-id AC-REPORTING --name "Security reporting" --owner grc@example.com --category process`,
  then `hound scope apply`.
- When your audit runs in periods, the snapshot reads the period that holds the month's last day.

Your agent can run this for you; it only reads CISO Assistant, apart from the one upload.

## 2. The committee report

```console
$ hound report committee --month 2026-11 --config hound.yaml --out committee-2026-11.md
```

It compares November's snapshot with October's. If October's is missing, it says so and reports November alone. Sections, in order:

1. **Summary**: the few lines that need the committee's attention.
2. **Collector health**: each collector's last run and outcome; a collector with no run in the last 7 days of the month is marked stale.
3. **Control status changes**: per programme and parent (an Essential Eight strategy, an AI6 practice), and the register summary.
4. **Exceptions and risk acceptances**: new, expiring within 60 days, and expired.
5. **Remediation**: open items, completed this month and overdue, by owner and due date.
6. **Incidents** opened and closed this month.
7. **Manual evidence and third parties**: tasks due or overdue, owners overdue for a second month first.
8. **Actions**: proposed, each with an owner and due date, for the committee to confirm. Every overdue item from section 7 is carried
   here until it is done, because the committee is the escalation after the reminder emails.

How to run the meeting is in [the security committee guide](govern/security-committee.md).

## 3. The board report

```console
$ hound report board --quarter 2026-Q4 --config hound.yaml --out board-2026-q4.md
```

It reads the snapshot of each quarter's last month (for 2026-Q4, December) for this quarter and the three before it. The body fits two
printed pages; the operational detail goes in the appendix.

The compliance position it proposes for each programme follows fixed rules, so it never depends on a model:

| Position | When |
| --- | --- |
| Off track | A requirement got worse since last quarter, or more than 25% of the requirements assessed are non-compliant |
| On track | Nothing non-compliant, and at most 10% not assessed |
| On track with conditions | Anything in between |

An exception or risk acceptance made this quarter that is valid for more than 12 months, or has no expiry, is marked for board
endorsement. How the board uses the report is in [the board reporting guide](govern/board-reporting.md).

## 4. Schedule it

| When | Who | What |
| --- | --- | --- |
| First working day of the month | Agent | `hound report snapshot` for last month, then `hound report committee` |
| Before the committee meets | GRC member (30 minutes) | Read the draft, check the summary and actions, refine the wording with [AI help](govern/ai-assisted-reporting.md) |
| First working day of the quarter | Agent | `hound report board` for last quarter, after that month's snapshot |
| Before the board meets | Authorising official | Confirm or change each compliance position |

## Problems

| Message | What to do |
| --- | --- |
| `No snapshot for 2026-11 in ...` | Take that period's snapshot first: `hound report snapshot --period <period> --config hound.yaml`. |
| `'2026-13' is not a period ...` | Months are `YYYY-MM`, quarters `YYYY-Qn`. |
| A programme has no coverage figures | The config has no `target` for that programme's audit. |
| Collector success rate is not shown | hound keeps only each collector's latest run; the committee sees the last outcome and staleness. |
