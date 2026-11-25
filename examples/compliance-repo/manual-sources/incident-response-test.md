# Runbook: yearly incident response plan approval and test

CISO Assistant emails this to the IT team 30, 7 and 1 days before the yearly due date. You do not need a CISO Assistant
login: follow the steps and send the result to the GRC team.

## What to export

1. Review the incident response plan with the CEO: who leads, who decides, who calls whom, and the reports in
   `scope/reporting.yaml` with their clocks. Fix anything that changed in the year (people, phone numbers, suppliers).
2. The CEO approves it: a signed and dated PDF of the plan.
3. Run a one-hour tabletop exercise with the people the plan names. Pick one scenario a year, such as ransomware on a
   laptop that handles customer data. Walk the plan step by step, and note what went well and what to change.
4. Write a one-page record: the date, who took part, the scenario, the reports the scenario would owe and whether the
   team knew where to make each one, and the actions you agreed.

## Send it to the GRC team

Email the approved plan and the exercise record to the IT team, the GRC team for Example Pty Ltd, with the subject
`Incident response test <year>`. The IT team saves both to the `example-incident-response-plan` drop folder, where
Evidence Hound reads them, and records them in CISO Assistant.

## If the test finds gaps

Say so. A test that finds gaps is evidence too: the GRC team records it and opens an action for each change.
