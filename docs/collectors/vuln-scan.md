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

So a business that scans its internet-facing servers weekly with Nessus and its laptops with Defender has two instances, one per scope.

## Settings

| Setting | Required | Meaning |
| --- | --- | --- |
| `drop_folder` | yes | Folder the exports are dropped in. A relative path is resolved against the config file's folder. |
| `format` | yes | `nessus` reads `*.nessus` files; `csv` reads `*.csv` files with the column settings below. |
| `scope` | yes | One of `internet_facing`, `common_apps`, `other_apps`, `operating_systems`. |
| `owner` | yes | Who owns the scans and fixes what they find. |
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
| The current export | The export itself, unchanged. A Nessus export is stored as `<name>.nessus.xml`, because CISO Assistant refuses the `.nessus` extension. |
| `scan.json` | The current scan, parsed: scope, owner, file, SHA-256, scan date, hosts, and each finding with host, finding ID, name, severity, CVEs, first seen, patch published, exploit available and unsupported. |
| `scan-history.json` | Every export read, newest first: file, SHA-256, scan date, number of hosts and number of findings. |

An empty drop folder gives only `scan-history.json`, with no exports.

## Checks

None yet. The patching checks that judge these scans (scan frequency, patch timeliness and unsupported software, per scope) come in s09-04.

## Problems

| Code | Meaning |
| --- | --- |
| `vuln_scan.setting_missing`, `vuln_scan.setting_invalid` | A setting is missing or has a value the collector cannot use. All are reported at once. |
| `vuln_scan.drop_folder_missing` | The drop folder does not exist. |
| `vuln_scan.export_invalid` | An export cannot be read. The message names the file and line. |
