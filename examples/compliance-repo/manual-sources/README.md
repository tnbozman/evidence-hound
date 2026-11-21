# Manual sources

Some evidence has no API: an incident register kept by hand, a restore test someone runs each quarter. Each such source is a `manual` collector in [`../hound.yaml`](../hound.yaml), and the people who own it drop their export into its folder **on the server that runs Evidence Hound**, never into this repo.

| Source | Drop folder (on the server) | Checks | Due every | Owner |
| --- | --- | --- | --- | --- |
| Incident register | `/var/lib/evidence-hound/manual/example-incident-register` | `incident.reported_to_ciso`, `incident.reported_to_asd`, `incident.response_plan_followed` | 31 days | Alex Example |
| Backup restore test | `/var/lib/evidence-hound/manual/example-backup-restore-test` | `backup.restore_tested` | 92 days | Sam Example |

Keep this table in step with `hound.yaml` when you add or change a source. How a manual source works, and the file format it reads, is in Evidence Hound's [configuration guide](../../../docs/configuration.md#manual-sources).
