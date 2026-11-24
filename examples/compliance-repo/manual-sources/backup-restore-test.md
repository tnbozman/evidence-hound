# Runbook: quarterly backup restore test

CISO Assistant emails this to the backup owners 30, 7 and 1 days before each quarterly due date. You do not need a
CISO Assistant login: follow the steps and send the result to the GRC team.

## What to export

1. Pick one system's backup from the last 7 days (rotate: Example 365 mail, a file share, one laptop).
2. Restore it to a test location, never over the live data.
3. Open three restored files or messages and check they read correctly.
4. Export the backup service's job report for the restore as PDF or CSV. It shows the backup date, the restore date
   and the result.

## Send it to the GRC team

Email the job report and one line (which system, which files you opened, whether they were correct) to the IT team,
the GRC team for Example Pty Ltd, with the subject `Restore test <quarter>`. The IT team saves the report to the
`example-backup-restore-test` drop folder, where Evidence Hound reads it, and records it in CISO Assistant.

## If the restore fails

Say so in the email. A failed restore is evidence too: the GRC team records it and opens an action.
