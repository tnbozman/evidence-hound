# Collector operator

You run hound's runner container next to CISO Assistant. It collects evidence on a schedule, so most months you do nothing. This guide
covers the checks, the usual faults and credential rotation.

## Check it is healthy (5 minutes, weekly)

```console
$ hound status --config hound.yaml
```

Every collector should have a run in the last day or two with outcome `succeeded`. The committee report marks a collector stale after 7
days without a run, and that reaches managers. Details: [running collectors](../running.md).

## Run it by hand

```console
$ hound run --config hound.yaml --dry-run      # collect and plan, send nothing
$ hound run --config hound.yaml --only <name>  # one collector
```

## Troubleshoot

| What you see | Likely cause | Do |
| --- | --- | --- |
| `failed` with `401` or `403` | The collector's credential expired or lost a permission | Rotate it (below); check the permissions in its collector guide under [`docs/collectors/`](../collectors/) |
| `failed` with a timeout | The service was down or the network blocked it | Run it again with `--only`; check the firewall allows the host |
| `succeeded` but `0 sent` for weeks | Nothing changed; that is normal. Only changes are sent | Nothing |
| `publish failed` | CISO Assistant is down, or hound's token expired | `hound config validate hound.yaml --check-secrets`; renew the token in CISO Assistant |
| A requirement flips between results | Two sources write it, or someone edits it by hand | `hound status` shows drift; ask the GRC member which source is right |

## Rotate a credential

Credentials are never in the repo: `hound.yaml` names a reference (`env:NAME` or `file:/run/secrets/name`), and the container reads it
at run time ([configuration](../configuration.md)).

1. Create the new credential in the service (an app secret, an API key, a CISO Assistant token) with the same permissions.
2. Replace the secret's value where the reference points: the Docker secret file or the environment variable of the runner.
3. `hound config validate hound.yaml --check-secrets` resolves every reference without printing a value.
4. `hound run --config hound.yaml --only <name>` and check it succeeds.
5. Revoke the old credential in the service.

Rotate when a credential is about to expire, when someone who knew it leaves, and at least once a year.

## Upgrade

Upgrade the runner image by tag, run `hound run --dry-run` first, then let the schedule carry on. When you upgrade CISO Assistant, check
the framework snapshots with the GRC member ([mapping files](../mapping-files.md#framework-snapshots)).
