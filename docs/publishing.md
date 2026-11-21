# Publishing to CISO Assistant

`hound publish` plans a run's updates (the same plan as `hound plan --dry-run`) and sends them to CISO Assistant.

```console
$ make ca-up                                    # local CISO Assistant, seeded; writes .dev/ca.env
$ set -a; . .dev/ca.env; set +a                 # HOUND_CA_URL and HOUND_CA_TOKEN
$ make run ARGS="publish --config examples/example-pty-ltd.yaml \
    --checks tests/fixtures/publish/checks.json --evidence-dir tests/fixtures/publish/evidence"
```

Add `--dry-run` to print the report without calling CISO Assistant (no token needed).

## Settings

| Setting | Meaning |
| --- | --- |
| `HOUND_CA_URL` | The API root, for example `http://localhost:8000/api/`. |
| `HOUND_CA_TOKEN` | A CISO Assistant personal access token. Sent as `Authorization: Token ...`; never logged. |
| `HOUND_TRANSPORT` | `rest` (default) or `kafka`. |
| `HOUND_KAFKA_BOOTSTRAP_SERVERS` | Kafka bootstrap servers the dispatcher reads, for `kafka`. |

They are read through .NET configuration, so environment variables work everywhere. The full config schema (s03) will move the URL and transport into the config file.

## What happens, in order

1. **Evidence uploads.** For each evidence file the publisher finds the CISO Assistant evidence with `ref_id` equal to the evidence ID, creating it (named after the file) if there is none. If the latest revision's SHA-256 equals the file's, nothing is uploaded and the item is **skipped**. Otherwise the file is read from `--evidence-dir`, checked against the SHA-256 recorded at collection (`evidence.hash_mismatch` if it changed), and uploaded as a **new revision**.
2. **Requirement assessments.** Each is found by `compliance_assessment__ref_id` and `requirement__ref_id` (the dispatcher's selector) and patched with `result`, `observation` and `evidences`. The plan cites evidence by ref_id; the publisher swaps in the CISO Assistant IDs from step 1. A requirement citing evidence whose upload failed fails with `publish.evidence_unresolved`.
3. **Applied controls.** Each is found by `ref_id` and its `status` set.

Every item is attempted: a failure is recorded and the rest go on. Running the same plan twice creates no duplicate evidence or revisions, and leaves results as they were.

## The report

Stdout is a JSON report; logs go to stderr.

```json
{
  "planned": 0, "sent": 6, "skipped": 3, "failed": 0,
  "items": [
    { "update": "upload evidence ev-0001 (mfa-admins.json)", "outcome": "skipped", "problems": [] }
  ]
}
```

| Exit code | Meaning |
| --- | --- |
| 0 | Everything was sent or skipped. |
| 3 | Nothing was sent: bad config, check results, mapping, or missing URL, token or transport settings. |
| 4 | The run finished but at least one item failed. Each failure is logged at `error` with its code. |

## Problem codes from CISO Assistant

| Code | Cause |
| --- | --- |
| `ciso_assistant.auth` | HTTP 401 or 403: bad or expired token, or missing permissions. |
| `ciso_assistant.not_found` | HTTP 404, or no object matches the selector (for example a requirement not in the audit's framework). |
| `ciso_assistant.ambiguous` | More than one object matches a selector that must match one. |
| `ciso_assistant.rejected` | Another 4xx; the message carries CISO Assistant's explanation. |
| `ciso_assistant.unavailable` | 5xx after retries, timeout, open circuit or no connection. |

Requests retry with exponential backoff and jitter on 5xx, 408 and 429 (POST is never retried, so a create is never doubled), time out, and stop calling a failing server through a circuit breaker.

## Kafka transport

`HOUND_TRANSPORT=kafka` writes the same updates as dispatcher JSON to the `observation` topic, for CISO Assistant's official [dispatcher](https://github.com/intuitem/ciso-assistant-community/tree/main/dispatcher) to apply. `make ca-up-kafka` adds Redpanda (Kafka API on `localhost:19092`) and a dispatcher built from the pinned release.

Differences from REST, both because the dispatcher passes `values` straight to the API:

- Requirement messages leave out `evidences`: the API wants evidence IDs, and only the REST transport can look them up. Evidence is still uploaded.
- "Sent" means Kafka accepted the message; the dispatcher's own errors go to its `errors` topic. Nothing is skipped, so re-running uploads files again.
