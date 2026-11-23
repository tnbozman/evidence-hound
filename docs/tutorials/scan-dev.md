# Fictional scan exports

`make scan-dev` writes vulnerability scan exports for a fictional Example Pty Ltd estate. You get one export per scope, dated from today, so the `vuln-scan` collector and its patching checks can be tried without a scanner. `make trial` uses them. You can also use them to work on the checks. Nothing leaves the machine.

```bash
make scan-dev                                          # the compliant estate, into .dev/scans
make scan-dev ARGS="--variant gaps"                    # every deviation below
make scan-dev ARGS="--deviation browser-patch-overdue --out /tmp/scans"
make scan-dev ARGS="--today 2026-11-23"                # date the exports from another day
```

It writes one folder per scope under `--out`, and replaces that folder's exports each time, so the export it just wrote is always the current scan. Then it prints the four `vuln-scan` sources' settings (a name, then `key=value` pairs) for a `hound.yaml`. See [the collector's guide](../collectors/vuln-scan.md).

## What the estate holds

| Scope | Folder and format | In the compliant estate |
| --- | --- | --- |
| `internet_facing` | `internet-facing`, Nessus | `web-01`, `vpn` and `mail.example.com`, scanned today. One medium web server flaw, patched upstream 3 days ago, with no known exploit. |
| `common_apps` | `laptops`, a Microsoft Defender vulnerability management CSV | Four laptops, read today. One browser flaw on `laptop-02`, first seen 5 days ago. |
| `other_apps` | `other-apps`, Nessus | `app-01` and `file-01`, scanned 2 days ago. One backup agent flaw, patch out 10 days ago. |
| `operating_systems` | `operating-systems`, Nessus | The four laptops, `app-01` and `file-01`, scanned yesterday. One missing operating system update on `file-01`, out 12 days ago. |

Hosts are `*.example.com` and addresses are documentation ranges (`203.0.113.0/24` and `192.0.2.0/24`). Every scan finishes at 02:00 UTC on its day.

## The two variants

The compliant estate passes all eleven `vuln.*` checks. The `gaps` estate applies every deviation below, and each one fails only the check it names.

| Deviation | What changes | Check and expected outcome |
| --- | --- | --- |
| `internet-facing-scan-stale` | The internet-facing scan last ran 3 days ago, where it must run daily. | `vuln.internet_facing.scanned_in_interval`: fail |
| `exploited-web-flaw-unpatched` | The web server flaw has a public exploit, and its patch came out 6 days ago, past the 48 hours. | `vuln.internet_facing.patched_in_window`: fail |
| `unsupported-vpn-appliance` | The VPN appliance runs a version its vendor no longer supports. | `vuln.internet_facing.unsupported_removed`: fail |
| `browser-patch-overdue` | The laptop browser flaw was first seen 20 days ago, past the two weeks. | `vuln.common_apps.patched_in_window`: fail |
| `os-update-overdue` | The file server's operating system update came out 45 days ago, past the month. | `vuln.operating_systems.patched_in_window`: fail |
| `unsupported-laptop-os` | One laptop runs an operating system version its vendor no longer supports. | `vuln.operating_systems.unsupported_removed`: fail |

`ScanDevEstateTests` runs the `vuln-scan` collector over the exports for each variant and each deviation, so this table and the estate cannot drift apart.

## In the trial

`make trial`, step 15, does the following:

1. Writes the compliant exports into the trial workspace's `scans` folder.
2. Adds the four scan sources.
3. Runs them dry, then publishes.
4. Offers to write the gaps estate and collect again, so you can watch the patching rows change in CISO Assistant.

See [the trial guide](local-trial.md).

## Adding a host, a finding or a deviation

- The estate is described once, in `src/EvidenceHound.ScanDev/ScanDevEstate.cs`. `ScanExports.cs` turns it into Nessus XML and Defender CSV.
- A deviation has a name, a sentence, the checks it fails with their expected outcome, and a change to the estate. Add it to `ScanDevEstate.Deviations`, and add a row to the table above.

## What it cannot show

- **A real scanner's output.** That means odd plugin output, very large exports, and Tenable.sc or Tenable.io exports with first-seen dates. The collector's parser tests and a run against your own exports cover those.
- **Defender's other export shapes.** Only the software vulnerabilities export, through the column map, is covered.
