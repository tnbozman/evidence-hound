# System security plan: Example corporate IT

Programme `essential-eight`, profile `ism`, version 0.1.

This plan follows NIST SP 800-18 Rev 1 Appendix A. The overview (sections 1 to 12) and the annex (section 13) meet ISM-0041: the system's purpose, its boundary, how it is managed, and the applicable controls.

## Gaps

No gaps.

## 1. System name and identifier

- Name: Example corporate IT
- Boundary (perimeter): Corporate IT
- Audit: EX-E8-2026

## 2. System categorisation

- Classification: OFFICIAL

## 3. System owner

it@example.com

## 4. Authorising official

ceo@example.com

## 5. Other designated contacts

- Security contact: IT team

## 6. Assignment of security responsibility

The IT team runs it; changes go through the weekly review.

## 7. System operational status

operational

## 8. System type

general support system

## 9. General description and purpose

Example corporate IT runs the company's mail, files and laptops.

## 10. System environment

One office and its cloud services.

| System | Type | Owner | Description |
| --- | --- | --- | --- |
| Example 365 | primary | it@example.com | Mail, files and identity. |

## 11. Interconnections and external service providers

| System | Solution | Supplier | Criticality |
| --- | --- | --- | --- |
| Example 365 | Managed hosting | Example Cloud Pty Ltd | 3 |

## 12. Applicable laws, regulations and policies

Privacy Act 1988; the Example Pty Ltd information security policy.

- Identity and access policy

## 13. Security controls (annex)

| Control | Title | Listed under | Applicability | Status | Statement | Responsible | POA&M |
| --- | --- | --- | --- | --- | --- | --- | --- |
| ISM-1807 | An automated method of asset discovery is used at least fortnightly to support… | pa, pos | Applicable | Not implemented | - | - | - |
| ISM-1808 | A vulnerability scanner with an up-to-date vulnerability database is used for… | pa, pos | Applicable | Not implemented | - | - | - |
| ISM-1698 | A vulnerability scanner is used at least daily to identify missing patches or… | pa | Applicable | Not implemented | - | - | - |
| ISM-1699 | A vulnerability scanner is used at least weekly to identify missing patches or… | pa | Applicable | Not implemented | - | - | - |
| ISM-1876 | Patches, updates or other vendor mitigations for vulnerabilities in online… | pa | Applicable | Not implemented | - | - | - |
| ISM-1690 | Patches, updates or other vendor mitigations for vulnerabilities in online… | pa | Applicable | Not implemented | - | - | - |
| ISM-1691 | Patches, updates or other vendor mitigations for vulnerabilities in office… | pa | Applicable | Not implemented | - | - | - |
| ISM-1905 | Online services that are no longer supported by vendors are removed. | pa | Applicable | Not implemented | - | - | - |
| ISM-1704 | Office productivity suites, web browsers and their extensions, email clients… | pa | Applicable | Not implemented | - | - | - |
| ISM-1701 | A vulnerability scanner is used at least daily to identify missing patches or… | pos | Applicable | Not implemented | - | - | - |
| ISM-1702 | A vulnerability scanner is used at least fortnightly to identify missing… | pos | Applicable | Not implemented | - | - | - |
| ISM-1877 | Patches, updates or other vendor mitigations for vulnerabilities in operating… | pos | Applicable | Not implemented | - | - | - |
| ISM-1694 | Patches, updates or other vendor mitigations for vulnerabilities in operating… | pos | Applicable | Not implemented | - | - | - |
| ISM-1695 | Patches, updates or other vendor mitigations for vulnerabilities in operating… | pos | Applicable | Not implemented | - | - | - |
| ISM-1501 | Operating systems that are no longer supported by vendors are replaced. | pos | Applicable | Not implemented | - | - | - |
| ISM-1504 | Multi-factor authentication is used to authenticate users to their… | mfa | Applicable | Planned | - | it@example.com | POAM-ISM-1504 |
| ISM-1679 | Multi-factor authentication is used to authenticate users to third-party… | mfa | Applicable | Not implemented | - | - | - |
| ISM-1680 | Multi-factor authentication (where available) is used to authenticate users to… | mfa | Applicable | Not implemented | - | - | - |
| ISM-1892 | Multi-factor authentication is used to authenticate users to their… | mfa | Applicable | Not implemented | - | - | - |
| ISM-1893 | Multi-factor authentication is used to authenticate users to third-party… | mfa | Applicable | Not implemented | - | - | - |
| ISM-1681 | Multi-factor authentication is used to authenticate customers to online… | mfa | Applicable | Not implemented | - | - | - |
| ISM-1401 | Multi-factor authentication uses either: something users have and something… | mfa | Applicable | Not implemented | - | - | - |
| ISM-1507 | Requests for privileged access to systems, applications and data repositories… | rap | Applicable | Not implemented | - | - | - |
| ISM-0445 | Privileged users are assigned a dedicated privileged user account to be used… | rap | Applicable | Not implemented | - | - | - |
| ISM-1175 | Privileged user accounts (excluding those explicitly authorised to access… | rap | Applicable | Not implemented | - | - | - |
| ISM-1883 | Privileged user accounts explicitly authorised to access online services are… | rap | Applicable | Not implemented | - | - | - |
| ISM-1380 | Privileged users use separate privileged and unprivileged operating… | rap | Applicable | Not implemented | - | - | - |
| ISM-1688 | Unprivileged user accounts cannot logon to privileged operating environments. | rap | Applicable | Not implemented | - | - | - |
| ISM-1689 | Privileged user accounts (excluding local administrator accounts) cannot logon… | rap | Applicable | Not implemented | - | - | - |
| ISM-0843 | Application control is implemented on workstations. | ac | Applicable | Not implemented | - | - | - |
| ISM-1870 | Application control is applied to user profiles and temporary folders used by… | ac | Applicable | Not implemented | - | - | - |
| ISM-1657 | Application control restricts the execution of executables, software… | ac | Applicable | Not implemented | - | - | - |
| ISM-1671 | Microsoft Office macros are disabled for users that do not have a demonstrated… | rmom | Applicable | Not implemented | - | - | - |
| ISM-1488 | Microsoft Office macros in files originating from the internet are blocked. | rmom | Applicable | Not implemented | - | - | - |
| ISM-1672 | Microsoft Office macro antivirus scanning is enabled. | rmom | Applicable | Not implemented | - | - | - |
| ISM-1489 | Microsoft Office macro security settings cannot be changed by users. | rmom | Applicable | Not implemented | - | - | - |
| ISM-1654 | Internet Explorer 11 is disabled or removed. | uah | Applicable | Not implemented | - | - | - |
| ISM-1486 | Web browsers do not process Java from the internet. | uah | Applicable | Not implemented | - | - | - |
| ISM-1485 | Web browsers do not process web advertisements from the internet. | uah | Applicable | Not implemented | - | - | - |
| ISM-1585 | Web browser security settings cannot be changed by users. | uah | Applicable | Not implemented | - | - | - |
| ISM-1511 | Backups of data, applications and settings are performed and retained in… | rb | Applicable | Not implemented | - | - | - |
| ISM-1810 | Backups of data, applications and settings are synchronised to enable… | rb | Applicable | Not implemented | - | - | - |
| ISM-1811 | Backups of data, applications and settings are retained in a secure and… | rb | Applicable | Not implemented | - | - | - |
| ISM-1515 | Restoration of data, applications and settings from backups to a common point… | rb | Applicable | Not implemented | - | - | - |
| ISM-1812 | Unprivileged user accounts cannot access backups belonging to other user… | rb | Applicable | Not implemented | - | - | - |
| ISM-1814 | Unprivileged user accounts are prevented from modifying and deleting backups. | rb | Applicable | Not implemented | - | - | - |

## 14. Completion and approval dates

Version 0.1. The authorising official (ceo@example.com) approves the plan in CISO Assistant; the approval date is recorded there.

## 15. Ongoing system security plan maintenance

`hound ssp build` rebuilds this plan from CISO Assistant and the configuration repo. Review it at least once a year and after any significant change to the system.

## Appendix: plan of action and milestones

| Requirement | Status | Control | Owner | ETA | Control status |
| --- | --- | --- | --- | --- | --- |
| ISM-1504 | Planned | AC-ADMIN | it@example.com | 2026-12-31 | in_progress |
