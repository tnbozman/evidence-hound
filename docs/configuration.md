# Deployment configuration

One YAML file per deployment says how to reach CISO Assistant, where evidence is stored, which collectors run and which audit they update. Nothing organisation-specific lives in code. The fictional [`examples/example-pty-ltd.yaml`](../examples/example-pty-ltd.yaml) is a complete example, and [`schemas/config.schema.json`](../schemas/config.schema.json) describes every key for editors and other tools.

```
$ hound config validate examples/example-pty-ltd.yaml
valid
```

`hound config validate` reports every problem at once, each with its file and line, and exits 3 if there are any. It never prints a value that might be a secret.

## Sections

| Key | Required | Meaning |
| --- | --- | --- |
| `ciso_assistant.url` | yes | API address, for example `http://localhost:8000/api/`. The `HOUND_CA_URL` setting overrides it. |
| `ciso_assistant.token` | yes | Secret reference to a CISO Assistant personal access token. |
| `ciso_assistant.transport` | no | `rest` (default) or `kafka` (through CISO Assistant's dispatcher). |
| `ciso_assistant.kafka_bootstrap_servers` | with `kafka` | Kafka servers the dispatcher reads. |
| `evidence.root` | yes | Evidence folder, relative to the config file. |
| `evidence.retention_days` | no | Runs older than this many days are deleted; the latest successful run of each collector is always kept. |
| `collectors[]` | yes (may be empty) | `name` (unique, lowercase letters, digits and hyphens), `type` (a known collector type), `schedule` (five-field cron, UTC), `settings` (plain values) and `secrets` (references). |
| `target.mapping` | yes | Framework mapping file, relative to the config file. |
| `target.compliance_assessment` | yes | The audit's `ref_id` in CISO Assistant. |
| `target.controls` | no | Control key to applied control `ref_id`. |
| `target.health_control` | no | Applied control `ref_id` that each collector's "collector health" evidence is linked to after every run (see [running.md](running.md)). |

Paths are resolved against the config file's folder. Unknown keys are errors, so a typo never goes unnoticed.

## Secrets are references

A secret in the config is always a reference, resolved when the tool runs:

| Reference | Resolves to |
| --- | --- |
| `env:NAME` | The environment variable `NAME`. |
| `file:/run/secrets/name` | The file's content with one trailing newline trimmed, for example a Docker secret. |

`hound config validate --check-secrets <file>` also resolves every reference and reports each one that is missing, naming the reference but never a value. Resolved secrets are held in a type whose text form is `***`.

A value that looks like a secret fails validation: `ciso_assistant.token` or a `secrets` entry that is not a reference, a setting whose name contains `password`, `secret`, `token`, `api_key`, `private_key` or `credential`, a setting holding a private key, and a setting holding a run of 32 or more letters and digits.

## Collector types

| Type | What it does |
| --- | --- |
| `fake` | Fixed, fictional results for tests, demos and dry runs. Optional setting `tenant`. |

`hound collectors list` prints every registered type with the check IDs it reports and the settings and secrets it reads. A collector with a required setting or secret missing fails validation with `config.setting_missing` or `config.secret_missing`.

A collector is a class implementing `ICollector` from `EvidenceHound.Collectors.Abstractions`, registered with `services.AddCollector<T>()`. Two collectors with the same type stop the CLI at startup (`collector.type_duplicate`, exit 3).
