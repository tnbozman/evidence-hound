# Running collectors

`hound run` is the one command a schedule calls. For each collector in the [deployment configuration](configuration.md) it collects evidence into the local store, evaluates the checks, maps them to the audit's requirements, plans the updates and publishes them to CISO Assistant.

```
$ hound run --config examples/example-pty-ltd.yaml --dry-run
info: Collector example-fake: Succeeded
{
  "outcome": "succeeded",
  "run_id": "20261121T020000Z",
  "collectors": [
    {
      "name": "example-fake",
      "outcome": "succeeded",
      "problems": [],
      "publish": { "planned": 8, "sent": 0, "skipped": 0, "failed": 0, "audits": [ ... ] },
      "changes": [ { "compliance_assessment": "EXAMPLE-BASELINE-2026", "changed": 4, "unchanged": 0, "drift": 0 } ],
      "drift": []
    }
  ]
}
```

Without `--dry-run` the same report counts what was sent, skipped because CISO Assistant already had it, or failed. A dry run does not read CISO Assistant, so it plans every row.

## Only changes are sent

Before planning, `hound run` reads each target audit's rows, the evidence it cites and the applied controls it sets, and sends only what differs (over the REST transport; the Kafka dispatcher cannot read, so with it everything is sent):

- **A row's state** is its result, the outcome of each check behind it and the hashes of its evidence. Timestamps and observation wording are not part of it, so an unchanged row is never rewritten and its history in CISO Assistant shows only real changes.
- **Evidence** whose latest revision has the same SHA-256 is neither uploaded nor looked up again; rows cite its existing ID. A changed file becomes a new revision of the same evidence item.
- **Every run has an ID**, its start time in UTC (`20261121T020000Z`). Each row a run writes ends its observation with a marker such as `[hound run 20261121T020000Z state 3f9c0a1b2c4d5e6f]`, which records the run and the state it wrote.
- **Drift.** When a row still carries the marker of the state Evidence Hound would write, but its result or evidence was changed in the UI, the run writes the computed values back and reports `publish.drift_corrected` under `drift`, naming the audit, the row's URN and the old and new values. A row whose marker was removed is simply rewritten.
- Rows outside the audit's selected implementation groups are neither sent nor counted.
- **History.** Each published run appends one line per audit to `<evidence root>/_history/<audit>.jsonl`: `date`, `run_id`, `changed`, `unchanged` and `drift`. Lines are never rewritten. Dry runs and `--no-publish` runs add nothing.

`hound plan` without `--dry-run` does the same reading and planning and prints the run ID, the counts per audit, any drift and the dispatcher messages it would send, without changing anything. It needs `HOUND_CA_URL` and `HOUND_CA_TOKEN`. `hound plan --dry-run` stays offline and prints every message.

| Option | Effect |
| --- | --- |
| `--only <name>` | Run one collector. An unknown name is a usage error (exit 2). |
| `--dry-run` | Collect and store evidence, then plan, but send nothing. |
| `--no-publish` | Collect and store evidence only. |

Exit codes: 0 every collector succeeded, 1 any collector failed, 2 usage, 3 configuration or secrets.

## One collector never stops another

Each collector runs on its own. If it crashes, cannot find a secret or CISO Assistant rejects one of its updates, that collector is reported as failed with its problems and the others carry on.

A collector only updates the requirements it fully reports on: a requirement whose checks come from two collectors is left alone by each, so one collector never marks another's work as not assessed.

## Evidence in CISO Assistant

Evidence IDs are scoped by collector (`example-fake/mfa-users.json`), and that ID is the evidence name in CISO Assistant, which needs names unique within a folder. Evidence from `hound publish` with other IDs is named `file name (ID)`. Re-running with unchanged files uploads nothing new.

## Run status and collector health

Every run writes a run record (start, end, outcome, checks passed and failed, publish counts) to `<evidence root>/_status/<collector>.json`. `hound status` shows the latest one per collector:

```
$ hound status --config examples/example-pty-ltd.yaml
COLLECTOR     LAST RUN (UTC)       OUTCOME    PASSED  FAILED  PUBLISHED
example-fake  2026-11-21 05:21:21  succeeded  3       1       0 sent, 0 skipped, 0 failed

AUDIT                  LAST RUN (UTC)       RUN ID            CHANGED  UNCHANGED  DRIFT
EXAMPLE-BASELINE-2026  2026-11-21 05:21:21  20261121T052121Z  0        4          0
```

The audit table, from the history files, appears once any run has published.

With `target.health_control` set, each run also uploads the record as "collector health" evidence (`hound-health/<collector>`) and links it to that applied control, so the security committee sees in CISO Assistant when each collector last ran. Over the Kafka transport the record is uploaded but not linked, because the dispatcher cannot link evidence to controls.
