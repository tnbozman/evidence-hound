# Runbook: User application hardening

Essential Eight: User application hardening. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. Export the browser, Office and PDF reader policies (Group Policy, Intune or the vendor's console).
2. Mark where each follows ASD's and the vendor's hardening guidance, and where it does not and why.
3. Show that users cannot change these settings, and that Internet Explorer 11 is disabled or removed.

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every 6 months. The source is overdue 183 days after the last attested file.

## Who attests

The endpoint owner, who manages application policies. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source user-application-hardening <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `browser.ie11_disabled` | ISM-1654 | Internet Explorer 11 is disabled or removed. |
| `browser.java_blocked` | ISM-1486 | Browsers do not process Java from the internet. |
| `browser.ads_blocked` | ISM-1485 | Browsers do not process web advertisements from the internet. |
| `browser.hardened` | ISM-1412 | Browsers are hardened using ASD and vendor guidance. |
| `browser.settings_locked` | ISM-1585 | Users cannot change browser security settings. |
| `office.ole_blocked` | ISM-1542 | Office cannot activate OLE packages. |
| `office.hardened` | ISM-1859 | Office productivity suites are hardened using ASD and vendor guidance. |
| `office.settings_locked` | ISM-1823 | Users cannot change Office security settings. |
| `pdf.child_processes_blocked` | ISM-1670 | PDF software cannot create child processes. |
| `pdf.hardened` | ISM-1860 | PDF software is hardened using ASD and vendor guidance. |
| `pdf.settings_locked` | ISM-1824 | Users cannot change PDF software security settings. |
