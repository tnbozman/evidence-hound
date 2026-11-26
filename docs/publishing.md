# Publishing to CISO Assistant

`hound publish` plans a run's updates (the same plan as `hound plan --dry-run`) and sends them to CISO Assistant. It sends the whole plan; `hound run` sends only what changed (see [Running collectors](running.md#only-changes-are-sent)).

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

They are read through .NET configuration, so environment variables work everywhere.

## What happens, in order

1. **Evidence uploads.** For each evidence file the publisher finds the CISO Assistant evidence with `ref_id` equal to the evidence ID, creating it (named after the file) if there is none. If the latest revision's SHA-256 equals the file's, nothing is uploaded and the item is **skipped**. Otherwise the file is read from `--evidence-dir`, checked against the SHA-256 recorded at collection (`evidence.hash_mismatch` if it changed), and uploaded as a **new revision**.
2. **Requirement assessments.** Each is found by `compliance_assessment__ref_id` and `requirement__ref_id` (the dispatcher's selector) and patched with `result`, `observation` and `evidences`. The plan cites evidence by ref_id; the publisher swaps in the CISO Assistant IDs from step 1. A requirement citing evidence whose upload failed fails with `publish.evidence_unresolved`.
3. **Applied controls.** Each is found by `ref_id` and its `status` set.

Every item is attempted: a failure is recorded and the rest go on. Running the same plan twice creates no duplicate evidence or revisions, and leaves results as they were.

### A requirement with no evidence

When a check a requirement needs is missing from the run or ended in an error, the requirement has no evidence, and it is published as
`non_compliant` with an observation that starts "No evidence yet:" and names each check and what happened to it. CISO Assistant leaves
not-assessed rows out of its compliance percentage, so publishing them as not assessed would let a few passes read 100%. A target can
opt out with `no_evidence: not_assessed` ([configuration.md](configuration.md)); see
[scope.md](scope.md#no-evidence-is-not-compliance) for the same rule on the rows the scope writes.

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

## Kafka transport (removed)

Release 3 removed the Kafka transport. CISO Assistant's dispatcher cannot read what it holds, link evidence to controls or skip unchanged rows, and a small team would have to run Kafka for it. A config with `transport: kafka` or `kafka_bootstrap_servers`, or `HOUND_TRANSPORT=kafka`, stops with `config.transport_removed`: remove them and hound publishes over the REST API.
