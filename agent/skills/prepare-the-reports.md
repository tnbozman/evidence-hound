---
name: prepare-the-reports
description: Take the month's posture snapshot and build the monthly security committee report and the quarterly board report with hound, then draft their wording with every figure traced to the report. Use on the first working day of a month or quarter, or when the person asks for a report.
---
## Prepare the committee and board reports

The guides are [docs/reporting.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/reporting.md), [docs/govern/security-committee.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/govern/security-committee.md),
[docs/govern/board-reporting.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/govern/board-reporting.md) and [docs/govern/ai-assisted-reporting.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/govern/ai-assisted-reporting.md).

1. Take last month's snapshot, once: `hound report snapshot --period <YYYY-MM> --config hound.yaml`. Only the person adds
   `--control <ref_id>` to upload it as evidence; ask them.
2. Build the committee report: `hound report committee --month <YYYY-MM> --config hound.yaml --out committee-<YYYY-MM>.md`.
   On the first month of a quarter, after the snapshot of the quarter's last month, build the board report:
   `hound report board --quarter <YYYY-Qn> --config hound.yaml --out board-<YYYY-Qn>.md`.
3. If `No snapshot for <period>` appears, take that period's snapshot first. Never fill a gap by hand.
4. Draft only the wording the guide allows: the committee summary, the board's compliance statements, the questions to expect. **Every
   figure, result, date and name comes from the generated report.** Do not add, round or estimate one. After each sentence you draft,
   list the report line it came from.
5. Escalations the committee owns for the board are not read into the board report yet; list them for the person from the committee
   minutes, and say so.
6. Hand the person the report files and your draft. They check each sentence against its line and publish the report themselves.
