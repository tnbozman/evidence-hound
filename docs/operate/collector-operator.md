# Collector operator

You run hound's runner container next to CISO Assistant. It collects evidence on a schedule, so most months you do nothing. This guide
covers the checks, the usual faults and credential rotation.

## Check it is healthy (5 minutes, weekly)

```console
$ hound status --config hound.yaml
```

Every collector should have a run in the last day or two with outcome `succeeded`. `hound status` shows only the outcome and counts; the
cause of a failure is in the output of `hound run --only <name>`. The committee report marks a collector stale after 7
days without a run, and that reaches managers. Details: [running collectors](../running.md).

## Run it by hand

```console
$ hound run --config hound.yaml --dry-run      # collect and plan, send nothing
$ hound run --config hound.yaml --only <name>  # one collector
```

## Troubleshoot

Run `hound run --config hound.yaml --only <name>` and read its output:

| What you see | Likely cause | Do |
| --- | --- | --- |
| An HTTP `401` or `403` from the service | The collector's credential expired or lost a permission | Rotate it (below); check the permissions in its collector guide under [collectors](../collectors/README.md) |
| A timeout | The service was down or the network blocked it | Run it again; check the firewall allows the host |
| `0 sent` in the PUBLISHED column for weeks | Nothing changed; that is normal. Only changes are sent | Nothing |
| Publishing to CISO Assistant fails | CISO Assistant is down, or hound's token expired | `hound config validate hound.yaml --check-secrets`; renew the token in CISO Assistant |
| A requirement flips between results | Two sources write it, or someone edits it by hand | `hound scope plan scope --config hound.yaml` shows the drift; ask the GRC member which source is right |

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

Upgrade the runner image by tag, run `hound run --config hound.yaml --dry-run` first, then let the schedule carry on. A dry run collects
and plans but does not prove CISO Assistant is reachable; `hound scope plan scope --config hound.yaml` does. When you upgrade CISO Assistant, check
the framework snapshots with the GRC member ([mapping files](../mapping-files.md#framework-snapshots)).
