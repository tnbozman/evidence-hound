# Vulnerability scan collector

The `vuln-scan` collector reads the exports your vulnerability scanner already makes. You drop them in a folder, and it turns them into structured scan evidence. It works with any scanner, because it reads files and never calls the scanner. It reads two formats:

- **Nessus v2** (`.nessus`), as Nessus, Tenable.io and Tenable.sc export it.
- **CSV**, from any scanner, through a column map. A Microsoft Defender vulnerability management export works with the map below.

Each instance covers one **scope**, the way the Essential Eight patching requirements group software:

| Scope | What its scans cover |
| --- | --- |
| `internet_facing` | Internet-facing services. |
| `common_apps` | Office productivity suites, web browsers and their extensions, email clients, PDF software and security products on workstations. |
| `other_apps` | Other applications on workstations, servers and network devices. |
| `operating_systems` | Operating systems of workstations, servers and network devices. |

So a business that scans its internet-facing servers with Nessus and its laptops with Defender has two instances, one per scope. Internet-facing scans cover both the applications and the operating systems of internet-facing services, servers and network devices; `operating_systems` is for the rest.

## Settings

| Setting | Required | Meaning |
| --- | --- | --- |
| `drop_folder` | yes | Folder the exports are dropped in. A relative path is resolved against the config file's folder. |
| `format` | yes | `nessus` reads `*.nessus` files; `csv` reads `*.csv` files with the column settings below. |
| `scope` | yes | One of `internet_facing`, `common_apps`, `other_apps`, `operating_systems`. |
| `owner` | yes | Who owns the scans and fixes what they find. |
| `scan_interval_days` | no | Most days between scans. Default, for maturity level 2: 1 for `internet_facing`, 7 for `common_apps`, 14 otherwise. |
| `patch_window_days` | no | Most days a finding may stay open after its patch is published. Default: 14 for `internet_facing` and `common_apps`, 30 otherwise. |
| `exploited_patch_window_hours` | no | Most hours a finding with a known exploit may stay open. Default: 48 for `internet_facing`, otherwise the patch window. |
| `column_host` | for csv | The column with the host name. |
| `column_finding_id` | for csv | The column that identifies the finding, such as a plugin ID or a CVE. |
| `column_severity` | for csv | The severity column: `info`, `low`, `medium` (or `moderate`), `high`, `critical`, or `0` to `4`. |
| `column_name` | no | The finding's title or the software's name. Default: the finding ID. |
| `column_cve` | no | CVE IDs, separated by commas, semicolons or spaces. |
| `column_first_seen` | no | When the finding was first seen. |
| `column_patch_published` | no | When the vendor published the patch. |
| `column_exploit_available` | no | Whether an exploit exists. Empty, `false`, `no`, `n`, `0`, `none`, `NoExploit` and `not available` read as no; anything else as yes. |
| `column_unsupported` | no | Whether the vendor no longer supports the software. Read as yes or no in the same way. |
| `column_scan_date` | no | When the scan saw the finding; the latest value is the scan date. Default: when the export file was written. |

Column names match the header ignoring case. Dates are ISO 8601 (`2026-11-20`, `2026-11-20T06:00:00Z`) and read as UTC unless they carry an offset.

```yaml
collectors:
  - name: internet-facing-scans
    type: vuln-scan
    schedule: "0 7 * * *"
    settings:
      drop_folder: ../scans/internet-facing
      format: nessus
      scope: internet_facing
      owner: Sam Example
  - name: laptop-vulnerabilities
    type: vuln-scan
    schedule: "0 7 * * *"
    settings:
      drop_folder: ../scans/defender
      format: csv
      scope: common_apps
      owner: Sam Example
      # A Microsoft Defender vulnerability management export.
      column_host: DeviceName
      column_finding_id: CveId
      column_severity: VulnerabilitySeverityLevel
      column_name: SoftwareName
      column_cve: CveId
      column_first_seen: FirstSeenTimestamp
      column_exploit_available: ExploitabilityLevel
      column_scan_date: LastSeenTimestamp
```

Defender's export has no patch publication date and no unsupported flag. Its `ExploitabilityLevel` is `NoExploit`, `ExploitIsPublic`, `ExploitIsVerified` or `ExploitIsInKit`, so every level except `NoExploit` counts as an exploit being available.

## Dropping an export

Copy the export into the drop folder yourself, or use `hound manual upload`, which works for scan sources too:

```bash
hound manual upload --config hound.yaml --source internet-facing-scans internet-facing-2026-11-16.nessus
```

It refuses a file that is not the source's format. If the same content is already in the folder, under any name, it copies nothing. It does not ask you to attest the file, because the scan itself is the evidence.

## How a run reads the folder

- Every export of the source's format is read. The same content dropped twice is read once, under its newest name.
- The **current scan** is the export whose scan ran last. That is the scan date inside the export, not the file's date, so a copied old export never replaces a newer scan.
- The older exports stay in the folder as history. Each run lists them all in `scan-history.json`.
- If any export cannot be read, the run fails with `vuln_scan.export_invalid`, naming the file and line. Nothing is published from a partial read. Fix or remove the file and run again.

From a Nessus export:

- Each `ReportHost` is a host, named by its `host-fqdn` tag when it has one.
- Each `ReportItem` above severity 0 is a finding. Informational items are left out.
- A finding's ID is its `pluginID`, and its title is its `pluginName`. It also reads the item's `cve` elements, `exploit_available` and `patch_publication_date`.
- A finding is unsupported when it has `unsupported_by_vendor` set to true, or its plugin name says "Unsupported" or "SEoL".
- The scan date is when the last host finished: `HOST_END_TIMESTAMP`, or else `HOST_END`, read as UTC.
- `.nessus` files have no first-seen date.
- Document type definitions are refused, so an export cannot pull in other files.

## Evidence

| File | Contents |
| --- | --- |
| The current export | The export itself, unchanged. |
| `scan.json` | The current scan, parsed: scope, owner, file, SHA-256, scan date, hosts, and each finding with host, finding ID, name, severity, CVEs, first seen, patch published, exploit available and unsupported. |
| `scan-history.json` | Every export read, newest first: file, SHA-256, scan date, number of hosts and number of findings. |
| `patch-assessment.json` | The policy used, and every overdue finding (host, finding ID, name, severity, exploit, what its age counts from, since when, its age and window in hours) and every unsupported one. The checks name at most 20; this file has them all. |

An empty drop folder gives only `scan-history.json`, with no exports.

## Checks

Each instance reports the checks of its scope, judged from the current scan:

| Check | Pass when | Otherwise |
| --- | --- | --- |
| `vuln.<scope>.scanned_in_interval` | The current scan is at most `scan_interval_days` calendar days old. | `fail`, with the scan's age and owner; `fail` when no export has been dropped yet. |
| `vuln.<scope>.patched_in_window` | No open finding is older than `patch_window_days`, or `exploited_patch_window_hours` when an exploit is available. | `fail`, naming up to 20 overdue findings and counting the rest; `error` with no scan yet. |
| `vuln.<scope>.unsupported_removed` | No finding is marked unsupported. | `fail`, naming the hosts and up to 20 findings; `error` with no scan yet. |

So there are eleven check IDs:

- `vuln.internet_facing.scanned_in_interval`, `vuln.internet_facing.patched_in_window`, `vuln.internet_facing.unsupported_removed`
- `vuln.common_apps.scanned_in_interval`, `vuln.common_apps.patched_in_window`, `vuln.common_apps.unsupported_removed`
- `vuln.other_apps.scanned_in_interval`, `vuln.other_apps.patched_in_window`
- `vuln.operating_systems.scanned_in_interval`, `vuln.operating_systems.patched_in_window`, `vuln.operating_systems.unsupported_removed`

`other_apps` has no unsupported check. Removing unsupported applications other than the common ones is a maturity level 3 requirement (ISM-0304), and Evidence Hound's Essential Eight mapping covers levels 1 and 2.

A finding's age is counted up to the scan date, from the first of these that is known:

1. When its patch was published.
2. When it was first seen.
3. The earliest export in the folder that shows the same host and finding ID.

The Essential Eight counts from the patch's release, so a scanner that gives the publication date is judged most fairly. A `.nessus` file has no first-seen date, so keep older exports in the folder to give findings their age.

Every open finding counts, whatever its severity, because the Essential Eight windows do not depend on severity. Informational Nessus items are not findings.

## In the Essential Eight mapping

| Check | Requirements |
| --- | --- |
| `vuln.internet_facing.scanned_in_interval` | ISM-1698 (online services, daily), ISM-1701 (operating systems of internet-facing servers and network devices, daily) |
| `vuln.internet_facing.patched_in_window` | ISM-1690 and ISM-1876 (online services: two weeks, 48 hours with an exploit), ISM-1877 (their operating systems, 48 hours with an exploit) |
| `vuln.internet_facing.unsupported_removed` | ISM-1905 (unsupported online services removed) |
| `vuln.common_apps.scanned_in_interval` | ISM-1699 (weekly) |
| `vuln.common_apps.patched_in_window` | ISM-1691 (two weeks) |
| `vuln.common_apps.unsupported_removed` | ISM-1704 |
| `vuln.other_apps.scanned_in_interval` | ISM-1700 (fortnightly) |
| `vuln.other_apps.patched_in_window` | ISM-1693 (one month) |
| `vuln.operating_systems.scanned_in_interval` | ISM-1702 (fortnightly) |
| `vuln.operating_systems.patched_in_window` | ISM-1695 (one month), with Intune compliance |
| `vuln.operating_systems.unsupported_removed` | ISM-1501, with the Microsoft 365 operating system check and the firewall firmware check |

All the rows use `all_pass`. So a deployment that maps ISM-1695 or ISM-1501 needs an `operating_systems` scan source, or those rows stay not assessed. ISM-1694, the two-week window for operating systems of internet-facing servers and network devices, is mapped to the firewall firmware check in s09-15, and will take `vuln.internet_facing.patched_in_window` too.

## Problems

| Code | Meaning |
| --- | --- |
| `vuln_scan.setting_missing`, `vuln_scan.setting_invalid` | A setting is missing or has a value the collector cannot use. All are reported at once. |
| `vuln_scan.drop_folder_missing` | The drop folder does not exist. |
| `vuln_scan.export_invalid` | An export cannot be read. The message names the file and line. |
