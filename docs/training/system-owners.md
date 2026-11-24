# Training: providing evidence (60 minutes)

For system owners. Built on the [system owner guide](../operate/system-owner.md). The presenter runs the example deployment; owners
watch, then do the exercises themselves.

**By the end, each person can:** follow a runbook from a reminder email, check an export is complete, send it the right way, say what
happens to it next, and find and update their own actions.

**Before the session:** the presenter runs `make trial` and `hound scope apply` so the example tasks exist in the local CISO Assistant.

| Minutes | Topic | What happens |
| --- | --- | --- |
| 0 to 10 | Why your evidence matters | One requirement of the example audit (ISM-1515, restoring backups): what it asks, why no API shows it, and what happens at month end if nobody provides it (the committee report's section 7). |
| 10 to 25 | A reminder arrives | Open the example task's runbook ([backup restore test](../../examples/compliance-repo/manual-sources/backup-restore-test.md)). Walk through what to export and what the export must show. |
| 25 to 35 | Exercise 1 | Each owner lists, from the runbook alone, what a complete export must show. The presenter then shows a restore report that leaves out one system; owners spot what is missing and say what they would send instead. |
| 35 to 45 | What happens next | The presenter uploads it (`hound manual upload`), the agent proposes a result (`hound manual propose`), and shows the attested result in the audit after a run. Owners see that a person, not the AI, attests it. |
| 45 to 55 | Exercise 2 | Each owner finds an applied control they own in CISO Assistant (or in the committee report's actions) and updates its ETA with a note. |
| 55 to 60 | When something changes | New systems, suppliers, AI features, data moves and admin changes: tell the GRC team first. |

**Check understanding:**

1. The export is missing two servers. What do you do? (Export again with every server; if you cannot, reply to the reminder with why
   and when.)
2. Who decides the result of your evidence? (The GRC team attests it, with the agent's proposal; you provide it.)
3. You cannot meet the due date. What happens if you say nothing? (It is overdue at month end and listed by name in the committee report.)
