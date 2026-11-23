# Deployment configuration

One YAML file per deployment says how to reach CISO Assistant, where evidence is stored, which collectors run and which audit they update. Nothing organisation-specific lives in code. The fictional [`examples/example-pty-ltd.yaml`](../examples/example-pty-ltd.yaml) is a complete example, and [`schemas/config.schema.json`](../schemas/config.schema.json) describes every key for editors and other tools.

The config lives in your private compliance repo next to the scope files that say what CISO Assistant should hold; the template is [`examples/compliance-repo/`](../examples/compliance-repo/) (its `hound.yaml`), and [scope.md](scope.md) explains the scope files and the review workflow.

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
| `evidence.period_retention_years` | no | How many years the final run of each ended audit period is kept, however old (default 7). It is the evidence behind a closed record. |
| `scheduler.time_zone` | no | IANA time zone the collectors' cron schedules are read in, for example `Australia/Sydney` (default `UTC`). `"0 3 * * *"` then means 03:00 there, across daylight saving changes. |
| `scheduler.jitter_seconds` | no | The most each collector's scheduled runs are delayed by (default 300, at most 3600, 0 for none). Each collector gets its own stable delay, so collectors sharing a schedule do not call their APIs at the same moment. |
| `scope` | no | The compliance repo's scope folder, relative to the config file. A target that names an audit with a `cycle` there publishes to the period open on the run's date (see [scope.md](scope.md#audit-periods)), and those periods' final runs are kept as above. |
| `collectors[]` | yes (may be empty) | `name` (unique, lowercase letters, digits and hyphens), `type` (a known collector type), `schedule` (five-field cron, read in `scheduler.time_zone`), `settings` (plain values) and `secrets` (references). |
| `targets[]` | yes, or `target` | The audits results are published to. Each has the keys below. |
| `targets[].mapping` | yes | Framework mapping file, relative to the config file. |
| `targets[].compliance_assessment` | yes | The audit's `ref_id` in CISO Assistant, or the series `ref_id` of an audit with a `cycle` in `scope`. Each audit is targeted once. |
| `targets[].controls` | no | Control key to applied control `ref_id`. |
| `targets[].health_control` | no | Applied control `ref_id` that each collector's "collector health" evidence is linked to after every run (see [running.md](running.md)). |

Paths are resolved against the config file's folder. Unknown keys are errors, so a typo never goes unnoticed.

### Several audits from one run

One collection run can feed several audits, for example the Essential Eight and the ASD Essentials during a migration, or the Essentials and NIST SP 800-171 later. List them under `targets`:

```yaml
targets:
  - mapping: ../mappings/essential-eight-2023.yaml
    compliance_assessment: EX-E8-2026
    health_control: AC-AUTO-EVIDENCE
  - mapping: ../mappings/asd-essentials-enterprise-it.yaml
    compliance_assessment: EX-ESSENTIALS-2026
```

- Each evidence file is uploaded once and linked from every audit whose requirements cite it.
- An applied control configured by several targets is updated once.
- A failure publishing to one audit does not stop the others. `hound publish` and `hound run` report sent, skipped and failed items for each audit under `audits`.
- A single `target:` map is still accepted, and is read as a list of one. Use one form or the other, not both.

## Secrets are references

A secret in the config is always a reference, resolved when the tool runs:

| Reference | Resolves to |
| --- | --- |
| `env:NAME` | The environment variable `NAME`. |
| `file:/run/secrets/name` | The file's content with one trailing newline trimmed, for example a Docker secret. |

`hound config validate --check-secrets <file>` also resolves every reference and reports each one that is missing, naming the reference but never a value. Resolved secrets are held in a type whose text form is `***`.

A value that looks like a secret fails validation: `ciso_assistant.token` or a `secrets` entry that is not a reference, a setting whose name contains `password`, `secret`, `token`, `api_key`, `private_key` or `credential` (except a setting ending in `_url` that holds a web address, such as a profile's `token_url`, or one ending in `_path` that holds an absolute path), a setting holding a private key, and a setting holding a run of 32 or more letters and digits.

## Collector types

| Type | What it does |
| --- | --- |
| `fake` | Fixed, fictional results for tests, demos and dry runs. Optional setting `tenant`. |
| `manual` | Export files that an owner drops in a folder, for systems without an API. See [Manual sources](#manual-sources). |
| `m365` | Microsoft 365 through Microsoft Graph, read-only: MFA, Conditional Access, privileged roles, Intune device compliance and Office hardening. See [collectors/m365.md](collectors/m365.md). |
| `rest` | Any HTTP API described by a profile in the profiles folder, such as `sophos-central` for Sophos Central. See [collectors/rest.md](collectors/rest.md), [collectors/sophos.md](collectors/sophos.md) and, for infrastructure drift from an IaC pipeline's plan, [collectors/terraform-drift.md](collectors/terraform-drift.md). The old `sophos` type is retired (`config.collector_type_retired`). |

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
| Has an attestation, and the file changed after it was attested | `partial`: "evidence changed since it was attested" (when the attestation records the file's hash) |
| Has an attestation without an outcome for the check | `partial`: "evidence present, not attested for this check" |
| Has no attestation (with or without an agent's proposal) | `partial`: "evidence present, not attested" |
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

### Proposals from an agent: `hound manual propose` and `hound manual approve`

**No evidence without a person.** A coding assistant set up with `hound agent setup` can read a manual evidence file and propose its result, but only a person attests it. Runs never use a model, and they publish attested results only.

```
$ hound manual propose example-backup-restore-test restore-test-2026-11.txt --config hound.yaml \
    --result pass --observation "Restore test of 18 November 2026: 3 files restored, hashes match." \
    --supports "Restored: 3 files to a scratch folder, hashes match" --json
```

The agent's command takes the source, the file (a path, or the name of a file already in the drop folder), `--result pass|fail|partial` for every check of the source, `--check <check id>=<result>` for a check that differs, `--observation`, and at least one `--supports`: an extract or a location in the file that shows the result. It copies the file into the drop folder if it is not there, and writes `<file>.proposal.yaml` beside it with `proposed_by: agent`, the file's SHA-256, the outcomes, the observation and the supporting extracts. A proposal is pending: it is not an attestation, a run does not publish it or store it as evidence, and the check stays "evidence present, not attested". Proposing again replaces a pending proposal. The command refuses a file a person already attested, a file the source's patterns do not accept, and a proposal without `--supports` (exit 3).

```
$ hound manual approve --config hound.yaml
Your name, recorded as the attester: Sam Example

[1/1] example-backup-restore-test restore-test-2026-11.txt
  File: /var/lib/evidence-hound/manual/example-backup-restore-test/restore-test-2026-11.txt
  Proposed by agent: pass
  Restore test of 18 November 2026: 3 files restored, hashes match.
  Supports: Restored: 3 files to a scratch folder, hashes match
  Attest (a), change (c), reject (r) or stop (s): a
Attested 1 of 1 proposals. The next 'hound run' publishes what you attested.
```

`hound manual approve [--source <source>]` walks a person through every pending proposal. Open the file and check it against what the agent quotes, then answer:

- `a` attests the proposal as it stands.
- `c` changes it: the outcome of each check (Enter keeps the proposed one), then the observation; what you enter is attested.
- `r` rejects it, with a reason. The proposal keeps `rejected_by`, the date and the reason, and is not asked again; no attestation is written.
- `s` stops; the rest wait.

Attesting writes `<file>.attest.yaml` with your name, the time, the outcomes, the observation as the note, `proposed_by: agent` and `file_sha256`, and removes the proposal. The next run publishes it as "Proposed by agent, attested pass by Sam Example on 2026-11-20: <file> (sha256 <first 12 characters>)." with the file and the attestation attached. If the file changes after that, the check is `partial` until the file is attested again. An attestation entered without a proposal reads as before.

**It needs a person at a terminal.** When its input or output is redirected, as when an agent runs it or answers are piped in, `hound manual approve` refuses (exit 2) and changes nothing.

A collector is a class implementing `ICollector` from `EvidenceHound.Collectors.Abstractions`, registered with `services.AddCollector<T>()`. Two collectors with the same type stop the CLI at startup (`collector.type_duplicate`, exit 3).

## No language model here

A deployment config never sets up a language model. An `llm` section, or any value that refers to the onboarding key (`llm_api_key`), is
refused with `config.llm_in_runner`. Model settings belong in `onboarding.yaml`, which only `hound onboard` reads; see
[onboarding.md](onboarding.md).
