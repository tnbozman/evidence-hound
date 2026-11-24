# System security plan: Example corporate IT (DRAFT)

> **DRAFT.** Not approved: version 0.1 has no sign-off in `scope/ssp.yaml`.

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

Columns follow the ASD SSP annex template. Implementation status uses the ASD SSP annex template's terms (Effective, Ineffective, Not implemented, Not applicable, Not assessed); the SP 800-18 status beside it decides what the plan of action and milestones carries. Control type comes from the framework's reference controls.

| Control | Description | Strategies | Maturity levels | Control type | Responsible entity | Implementation status | SP 800-18 status | Statement | Responsible | POA&M |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| ISM-1807 | An automated method of asset discovery is used at least fortnightly to support the detection of assets for subsequent vulnerability scanning activities. | pa, pos | 1, 2, 3 | Technical, Documentation | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1808 | A vulnerability scanner with an up-to-date vulnerability database is used for vulnerability scanning activities. | pa, pos | 1, 2, 3 | Policy, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1698 | A vulnerability scanner is used at least daily to identify missing patches or updates for vulnerabilities in online services. | pa | 1, 2, 3 | Policy, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1699 | A vulnerability scanner is used at least weekly to identify missing patches or updates for vulnerabilities in office productivity suites, web browsers and their extensions, email clients, PDF software, and security products. | pa | 1, 2, 3 | Policy, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1876 | Patches, updates or other vendor mitigations for vulnerabilities in online services are applied within 48 hours of release when vulnerabilities are assessed as critical by vendors or when working exploits exist. | pa | 1, 2, 3 | Policy, Procedure | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1690 | Patches, updates or other vendor mitigations for vulnerabilities in online services are applied within two weeks of release when vulnerabilities are assessed as non-critical by vendors and no working exploits exist. | pa | 1, 2, 3 | Policy, Procedure | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1691 | Patches, updates or other vendor mitigations for vulnerabilities in office productivity suites, web browsers and their extensions, email clients, PDF software, and security products are applied within two weeks of release. | pa | 1, 2 | Policy, Procedure | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1905 | Online services that are no longer supported by vendors are removed. | pa | 1, 2, 3 | Procedure, Documentation | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1704 | Office productivity suites, web browsers and their extensions, email clients, PDF software, Adobe Flash Player, and security products that are no longer supported by vendors are removed. | pa | 1, 2, 3 | Procedure, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1701 | A vulnerability scanner is used at least daily to identify missing patches or updates for vulnerabilities in operating systems of internet-facing servers and internet-facing network devices. | pos | 1, 2, 3 | Policy, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1702 | A vulnerability scanner is used at least fortnightly to identify missing patches or updates for vulnerabilities in operating systems of workstations, non-internet-facing servers and non-internet-facing network devices. | pos | 1, 2, 3 | Policy, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1877 | Patches, updates or other vendor mitigations for vulnerabilities in operating systems of internet-facing servers and internet-facing network devices are applied within 48 hours of release when vulnerabilities are assessed as critical by vendors or when working exploits exist. | pos | 1, 2, 3 | Policy, Procedure | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1694 | Patches, updates or other vendor mitigations for vulnerabilities in operating systems of internet-facing servers and internet-facing network devices are applied within two weeks of release when vulnerabilities are assessed as non-critical by vendors and no working exploits exist. | pos | 1, 2, 3 | Policy, Procedure | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1695 | Patches, updates or other vendor mitigations for vulnerabilities in operating systems of workstations, non-internet-facing servers and non-internet-facing network devices are applied within one month of release. | pos | 1, 2 | Policy, Procedure | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1501 | Operating systems that are no longer supported by vendors are replaced. | pos | 1, 2, 3 | Procedure, Documentation | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1504 | Multi-factor authentication is used to authenticate users to their organisation’s online services that process, store or communicate their organisation’s sensitive data. | mfa | 1, 2, 3 | Technical | Consumer | Not implemented | Planned | - | it@example.com | POAM-ISM-1504 |
| ISM-1679 | Multi-factor authentication is used to authenticate users to third-party online services that process, store or communicate their organisation’s sensitive data. | mfa | 1, 2, 3 | Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1680 | Multi-factor authentication (where available) is used to authenticate users to third-party online services that process, store or communicate their organisation’s non-sensitive data. | mfa | 1, 2, 3 | Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1892 | Multi-factor authentication is used to authenticate users to their organisation’s online customer services that process, store or communicate their organisation’s sensitive customer data. | mfa | 1, 2, 3 | Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1893 | Multi-factor authentication is used to authenticate users to third-party online customer services that process, store or communicate their organisation’s sensitive customer data. | mfa | 1, 2, 3 | Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1681 | Multi-factor authentication is used to authenticate customers to online customer services that process, store or communicate sensitive customer data. | mfa | 1, 2, 3 | Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1401 | Multi-factor authentication uses either: something users have and something users know, or something users have that is unlocked by something users know or are. | mfa | 1, 2, 3 | Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1507 | Requests for privileged access to systems, applications and data repositories are validated when first requested. | rap | 1, 2, 3 | Policy, Procedure | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-0445 | Privileged users are assigned a dedicated privileged user account to be used solely for duties requiring privileged access. | rap | 1, 2, 3 | Procedure, Documentation | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1175 | Privileged user accounts (excluding those explicitly authorised to access online services) are prevented from accessing the internet, email and web services. | rap | 1, 2, 3 | Procedure, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1883 | Privileged user accounts explicitly authorised to access online services are strictly limited to only what is required for users and services to undertake their duties. | rap | 1, 2, 3 | Policy, Procedure | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1380 | Privileged users use separate privileged and unprivileged operating environments. | rap | 1, 2, 3 | Procedure, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1688 | Unprivileged user accounts cannot logon to privileged operating environments. | rap | 1, 2, 3 | Procedure, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1689 | Privileged user accounts (excluding local administrator accounts) cannot logon to unprivileged operating environments. | rap | 1, 2, 3 | Procedure, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-0843 | Application control is implemented on workstations. | ac | 1, 2, 3 | Policy, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1870 | Application control is applied to user profiles and temporary folders used by operating systems, web browsers and email clients. | ac | 1, 2, 3 | Policy, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1657 | Application control restricts the execution of executables, software libraries, scripts, installers, compiled HTML, HTML applications and control panel applets to an organisation-approved set. | ac | 1, 2, 3 | Policy, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1671 | Microsoft Office macros are disabled for users that do not have a demonstrated business requirement. | rmom | 1, 2, 3 | Procedure | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1488 | Microsoft Office macros in files originating from the internet are blocked. | rmom | 1, 2, 3 | Policy, Procedure | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1672 | Microsoft Office macro antivirus scanning is enabled. | rmom | 1, 2, 3 | Procedure, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1489 | Microsoft Office macro security settings cannot be changed by users. | rmom | 1, 2, 3 | Policy, Procedure | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1654 | Internet Explorer 11 is disabled or removed. | uah | 1, 2, 3 | Procedure | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1486 | Web browsers do not process Java from the internet. | uah | 1, 2, 3 | Policy, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1485 | Web browsers do not process web advertisements from the internet. | uah | 1, 2, 3 | Policy, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1585 | Web browser security settings cannot be changed by users. | uah | 1, 2, 3 | Policy, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1511 | Backups of data, applications and settings are performed and retained in accordance with business criticality and business continuity requirements. | rb | 1, 2, 3 | Policy, Procedure | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1810 | Backups of data, applications and settings are synchronised to enable restoration to a common point in time. | rb | 1, 2, 3 | Policy | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1811 | Backups of data, applications and settings are retained in a secure and resilient manner. | rb | 1, 2, 3 | Policy, Technical | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1515 | Restoration of data, applications and settings from backups to a common point in time is tested as part of disaster recovery exercises. | rb | 1, 2, 3 | Procedure | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1812 | Unprivileged user accounts cannot access backups belonging to other user accounts. | rb | 1, 2, 3 | Policy | Consumer | Not assessed | Not implemented | - | - | - |
| ISM-1814 | Unprivileged user accounts are prevented from modifying and deleting backups. | rb | 1, 2, 3 | Policy, Technical | Consumer | Not assessed | Not implemented | - | - | - |

## 14. Completion and approval dates

Version 0.1 is a draft. The authorising official (ceo@example.com) approves it by recording a sign-off in `scope/ssp.yaml`, through a reviewed pull request.

## 15. Ongoing system security plan maintenance

`hound ssp build` rebuilds this plan from CISO Assistant and the configuration repo. Review it at least once a year and after any significant change to the system.

## Appendix: plan of action and milestones

| Requirement | Status | Control | Owner | ETA | Control status |
| --- | --- | --- | --- | --- | --- |
| ISM-1504 | Planned | AC-ADMIN | it@example.com | 2026-12-31 | in_progress |

_DRAFT: not approved._
