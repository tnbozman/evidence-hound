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
| `manual` | Export files that an owner drops in a folder, for systems without an API. See [Manual sources](#manual-sources). |
| `m365` | Microsoft 365 through Microsoft Graph, read-only: MFA, Conditional Access, privileged roles, Intune device compliance and Office hardening. See [collectors/m365.md](collectors/m365.md). |

`hound collectors list` prints every registered type with the check IDs it reports and the settings and secrets it reads. A collector with a required setting or secret missing fails validation with `config.setting_missing` or `config.secret_missing`. Settings a collector describes as paths, such as `drop_folder`, are resolved against the config file's folder like every other path.

## Manual sources

A `manual` collector instance is one manual source. Its settings:

| Setting | Required | Meaning |
| --- | --- | --- |
| `drop_folder` | yes | Folder the owner drops export files in. |
| `patterns` | no | Comma-separated file patterns to accept, such as `*.txt,*.cfg`. Default: every file. |
| `checks` | yes | Comma-separated check IDs this source supports. |
| `due_days` | yes | Days after which fresh evidence is due, such as `30`. |
| `owner` | yes | Who owns the evidence and attests it. |

```yaml
collectors:
  - name: example-firewall-config
    type: manual
    schedule: "0 6 * * *"
    settings:
      drop_folder: ../.dev/manual/example-firewall-config
      patterns: "*.txt,*.cfg"
      checks: network.firewall.rules_reviewed, network.firewall.config_exported
      due_days: 30
      owner: Alex Example
```

Each run reads the drop folder and emits every accepted file dated within the last `due_days` as evidence, with SHA-256 hashes like any other evidence. The same content dropped twice is emitted once, under its newest name. The newest file decides each configured check:

| Newest file within the interval | Check result |
| --- | --- |
| Has an attestation with an outcome for the check | That outcome (`pass`, `fail`, `partial` or `not_applicable`), naming who attested, when, and the note |
| Has an attestation without an outcome for the check | `partial`: "evidence present, not attested for this check" |
| Has no attestation | `partial`: "evidence present, not attested" |
| None, but an older file exists | `error`: "evidence overdue since <date>" |
| None ever | `error`: "no evidence uploaded yet" |

A file is dated by its attestation's `date`, or by when it was written if nobody attested it. A missing drop folder or an invalid attestation fails the collector's run with `manual.drop_folder_missing` or `manual.attestation_invalid`.

An attestation is a sidecar next to the file, named `<file>.attest.yaml`:

```yaml
who: Alex Example
date: 2026-11-20            # or 2026-11-20T09:30:00Z
note: Rules reviewed with the network team.
outcomes:
  network.firewall.rules_reviewed: pass
  network.firewall.config_exported: pass
```

`hound manual upload` copies a file into a source's drop folder and asks whether to attest it. Prompts go to stderr; the copied file's path goes to stdout.

```
$ hound manual upload --config hound.yaml --source example-firewall-config firewall-export.txt
Attest this file now? [y/N]: y
Attested by [Alex Example]:
Outcome for network.firewall.rules_reviewed (pass, fail, partial, not_applicable) [pass]:
Outcome for network.firewall.config_exported (pass, fail, partial, not_applicable) [pass]:
Note (optional): Rules reviewed with the network team.
```

The command refuses a file that does not match the source's patterns, or whose name is already taken by different content (exit 3), and a non-attestable outcome answer (exit 2). A file whose content is already in the drop folder is not copied again.

A collector is a class implementing `ICollector` from `EvidenceHound.Collectors.Abstractions`, registered with `services.AddCollector<T>()`. Two collectors with the same type stop the CLI at startup (`collector.type_duplicate`, exit 3).
