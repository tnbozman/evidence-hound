# Running collectors

`hound run` is the one command a schedule calls. For each collector in the [deployment configuration](configuration.md) it collects evidence into the local store, evaluates the checks, maps them to the audit's requirements, plans the updates and publishes them to CISO Assistant.

```
$ hound run --config examples/example-pty-ltd.yaml --dry-run
info: Collector example-fake: Succeeded
{
  "outcome": "succeeded",
  "collectors": [
    {
      "name": "example-fake",
      "outcome": "succeeded",
      "problems": [],
      "publish": { "planned": 8, "sent": 0, "skipped": 0, "failed": 0 }
    }
  ]
}
```

Without `--dry-run` the same report counts what was sent, skipped because CISO Assistant already had it, or failed.

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
```

With `target.health_control` set, each run also uploads the record as "collector health" evidence (`hound-health/<collector>`) and links it to that applied control, so the security committee sees in CISO Assistant when each collector last ran. Over the Kafka transport the record is uploaded but not linked, because the dispatcher cannot link evidence to controls.
