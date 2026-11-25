# System security plan: Example CUI enclave (DRAFT)

> **DRAFT.** Not approved: version 0.1 has no sign-off in `scope/ssp.yaml`.

Programme `cmmc-level-2`, profile `nist-800-171`, version 0.1.

This plan follows NIST SP 800-18 Rev 1 Appendix A. It is the system security plan NIST SP 800-171 requirement 3.12.4 calls for: the system boundary, its environment, how each requirement is implemented, and its connections to other systems.

## Gaps

- Example laptops: no CMMC asset category; tag the system 'cmmc_asset_category' in scope/setup.yaml with one of cui, security_protection, contractor_risk_managed, specialized, out_of_scope.
- AC.L1-3.1.1: worth 5 points; CMMC allows only 1-point requirements on a POA&M (32 CFR 170.21(a)(2)(ii)); meet it before a Conditional Level 2 status.

## 1. System name and identifier

- Name: Example CUI enclave
- Boundary (perimeter): CUI enclave
- Audit: EX-CMMC-2026

## 2. System categorisation

- Classification: CUI

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

| System | Type | Owner | Description | CMMC asset category |
| --- | --- | --- | --- | --- |
| Example 365 | primary | it@example.com | Mail, files and identity. | CUI asset |
| Example laptops | supporting | it@example.com | Staff laptops. | - |

## 11. Interconnections and external service providers

| System | Solution | Supplier | Criticality |
| --- | --- | --- | --- |
| Example 365 | Managed hosting | Example Cloud Pty Ltd | 3 |

## 12. Applicable laws, regulations and policies

Privacy Act 1988; the Example Pty Ltd information security policy.

- Access control policy

## 13. Security controls (annex)

Every requirement CMMC Level 2 assesses (NIST SP 800-171 Rev 2), grouped by family. Status uses the SP 800-18 terms; a requirement with no row in the audit is not assessed and shown as Not implemented. A POA&M item marked "not allowed" cannot stay on a CMMC POA&M (32 CFR 170.21(a)(2)): see the gaps.

### Access control (AC)

| Requirement | Title | Level | Status | Statement | Responsible | Evidence | POA&M |
| --- | --- | --- | --- | --- | --- | --- | --- |
| AC.L1-3.1.1 | Authorized Access Control | L1 | Not implemented | AC-MFA: Every user signs in with MFA.<br>Two shared accounts remain. | it@example.com | policy | POAM-AC.L1-3.1.1 (not allowed) |
| AC.L1-3.1.2 | Transaction & Function Control | L1 | Implemented | AC-MFA: Every user signs in with MFA.<br>Roles limit what each user can do. | it@example.com | policy | - |
| AC.L2-3.1.3 | Control CUI Flow | L2 | Planned | - | it@example.com | policy | POAM-AC.L2-3.1.3 |
| AC.L2-3.1.4 | Separation of Duties | L2 | Not implemented | - | - | - | - |
| AC.L2-3.1.5 | Least Privilege | L2 | Not implemented | - | - | - | - |
| AC.L2-3.1.6 | Non-Privileged Account Use | L2 | Not implemented | - | - | - | - |
| AC.L2-3.1.7 | Privileged Functions | L2 | Not implemented | - | - | - | - |
| AC.L2-3.1.8 | Unsuccessful Logon Attempts | L2 | Not implemented | - | - | - | - |
| AC.L2-3.1.9 | Privacy & Security Notices | L2 | Not implemented | - | - | - | - |
| AC.L2-3.1.10 | Session Lock | L2 | Not implemented | - | - | - | - |
| AC.L2-3.1.11 | Session Termination | L2 | Not implemented | - | - | - | - |
| AC.L2-3.1.12 | Control Remote Access | L2 | Not implemented | - | - | - | - |
| AC.L2-3.1.13 | Remote Access Confidentiality | L2 | Not implemented | - | - | - | - |
| AC.L2-3.1.14 | Remote Access Routing | L2 | Not implemented | - | - | - | - |
| AC.L2-3.1.15 | Privileged Remote Access | L2 | Not implemented | - | - | - | - |
| AC.L2-3.1.16 | Wireless Access Authorization | L2 | Not implemented | - | - | - | - |
| AC.L2-3.1.17 | Wireless Access Protection | L2 | Not implemented | - | - | - | - |
| AC.L2-3.1.18 | Mobile Device Connection | L2 | Not implemented | - | - | - | - |
| AC.L2-3.1.19 | Encrypt CUI on Mobile | L2 | Not implemented | - | - | - | - |
| AC.L1-3.1.20 | External Connections | L1 | Not implemented | - | - | - | - |
| AC.L2-3.1.21 | Portable Storage Use | L2 | Not implemented | - | - | - | - |
| AC.L1-3.1.22 | Control Public Information | L1 | Not implemented | - | - | - | - |

### Awareness and training (AT)

| Requirement | Title | Level | Status | Statement | Responsible | Evidence | POA&M |
| --- | --- | --- | --- | --- | --- | --- | --- |
| AT.L2-3.2.1 | Role-Based Risk Awareness | L2 | Not implemented | - | - | - | - |
| AT.L2-3.2.2 | Role-Based Training | L2 | Not implemented | - | - | - | - |
| AT.L2-3.2.3 | Insider Threat Awareness | L2 | Not implemented | - | - | - | - |

### Audit and accountability (AU)

| Requirement | Title | Level | Status | Statement | Responsible | Evidence | POA&M |
| --- | --- | --- | --- | --- | --- | --- | --- |
| AU.L2-3.3.1 | System Auditing | L2 | Not implemented | - | - | - | - |
| AU.L2-3.3.2 | User Accountability | L2 | Not implemented | - | - | - | - |
| AU.L2-3.3.3 | Event Review | L2 | Not implemented | - | - | - | - |
| AU.L2-3.3.4 | Audit Failure Alerting | L2 | Not implemented | - | - | - | - |
| AU.L2-3.3.5 | Audit Correlation | L2 | Not implemented | - | - | - | - |
| AU.L2-3.3.6 | Reduction & Reporting | L2 | Not implemented | - | - | - | - |
| AU.L2-3.3.7 | Authoritative Time Source | L2 | Not implemented | - | - | - | - |
| AU.L2-3.3.8 | Audit Protection | L2 | Not implemented | - | - | - | - |
| AU.L2-3.3.9 | Audit Management | L2 | Not implemented | - | - | - | - |

### Configuration management (CM)

| Requirement | Title | Level | Status | Statement | Responsible | Evidence | POA&M |
| --- | --- | --- | --- | --- | --- | --- | --- |
| CM.L2-3.4.1 | System Baselining | L2 | Not implemented | - | - | - | - |
| CM.L2-3.4.2 | Security Configuration Enforcement | L2 | Not implemented | - | - | - | - |
| CM.L2-3.4.3 | System Change Management | L2 | Not implemented | - | - | - | - |
| CM.L2-3.4.4 | Security Impact Analysis | L2 | Not implemented | - | - | - | - |
| CM.L2-3.4.5 | Access Restrictions for Change | L2 | Not implemented | - | - | - | - |
| CM.L2-3.4.6 | Least Functionality | L2 | Not implemented | - | - | - | - |
| CM.L2-3.4.7 | Nonessential Functionality | L2 | Not implemented | - | - | - | - |
| CM.L2-3.4.8 | Application Execution Policy | L2 | Not implemented | - | - | - | - |
| CM.L2-3.4.9 | User-Installed Software | L2 | Not implemented | - | - | - | - |

### Identification and authentication (IA)

| Requirement | Title | Level | Status | Statement | Responsible | Evidence | POA&M |
| --- | --- | --- | --- | --- | --- | --- | --- |
| IA.L1-3.5.1 | Identification | L1 | Not implemented | - | - | - | - |
| IA.L1-3.5.2 | Authentication | L1 | Not implemented | - | - | - | - |
| IA.L2-3.5.3 | Multifactor Authentication | L2 | Not implemented | - | - | - | - |
| IA.L2-3.5.4 | Replay-Resistant Authentication | L2 | Not implemented | - | - | - | - |
| IA.L2-3.5.5 | Identifier Reuse | L2 | Not implemented | - | - | - | - |
| IA.L2-3.5.6 | Identifier Handling | L2 | Not implemented | - | - | - | - |
| IA.L2-3.5.7 | Password Complexity | L2 | Not implemented | - | - | - | - |
| IA.L2-3.5.8 | Password Reuse | L2 | Not implemented | - | - | - | - |
| IA.L2-3.5.9 | Temporary Passwords | L2 | Not implemented | - | - | - | - |
| IA.L2-3.5.10 | Cryptographically-Protected Passwords | L2 | Not implemented | - | - | - | - |
| IA.L2-3.5.11 | Obscure Feedback | L2 | Not implemented | - | - | - | - |

### Incident response (IR)

| Requirement | Title | Level | Status | Statement | Responsible | Evidence | POA&M |
| --- | --- | --- | --- | --- | --- | --- | --- |
| IR.L2-3.6.1 | Incident Handling | L2 | Not implemented | - | - | - | - |
| IR.L2-3.6.2 | Incident Reporting | L2 | Not implemented | - | - | - | - |
| IR.L2-3.6.3 | Incident Response Testing | L2 | Not implemented | - | - | - | - |

### Maintenance (MA)

| Requirement | Title | Level | Status | Statement | Responsible | Evidence | POA&M |
| --- | --- | --- | --- | --- | --- | --- | --- |
| MA.L2-3.7.1 | Perform Maintenance | L2 | Not implemented | - | - | - | - |
| MA.L2-3.7.2 | System Maintenance Control | L2 | Not implemented | - | - | - | - |
| MA.L2-3.7.3 | Equipment Sanitization | L2 | Not implemented | - | - | - | - |
| MA.L2-3.7.4 | Media Inspection | L2 | Not implemented | - | - | - | - |
| MA.L2-3.7.5 | Nonlocal Maintenance | L2 | Not implemented | - | - | - | - |
| MA.L2-3.7.6 | Maintenance Personnel | L2 | Not implemented | - | - | - | - |

### Media protection (MP)

| Requirement | Title | Level | Status | Statement | Responsible | Evidence | POA&M |
| --- | --- | --- | --- | --- | --- | --- | --- |
| MP.L2-3.8.1 | Media Protection | L2 | Not implemented | - | - | - | - |
| MP.L2-3.8.2 | Media Access | L2 | Not implemented | - | - | - | - |
| MP.L1-3.8.3 | Media Disposal | L1 | Not implemented | - | - | - | - |
| MP.L2-3.8.4 | Media Markings | L2 | Not implemented | - | - | - | - |
| MP.L2-3.8.5 | Media Accountability | L2 | Not implemented | - | - | - | - |
| MP.L2-3.8.6 | Portable Storage Encryption | L2 | Not implemented | - | - | - | - |
| MP.L2-3.8.7 | Removable Media | L2 | Not implemented | - | - | - | - |
| MP.L2-3.8.8 | Shared Media | L2 | Not implemented | - | - | - | - |
| MP.L2-3.8.9 | Protect Backups | L2 | Not implemented | - | - | - | - |

### Personnel security (PS)

| Requirement | Title | Level | Status | Statement | Responsible | Evidence | POA&M |
| --- | --- | --- | --- | --- | --- | --- | --- |
| PS.L2-3.9.1 | Screen Individuals | L2 | Not implemented | - | - | - | - |
| PS.L2-3.9.2 | Personnel Actions | L2 | Not implemented | - | - | - | - |

### Physical protection (PE)

| Requirement | Title | Level | Status | Statement | Responsible | Evidence | POA&M |
| --- | --- | --- | --- | --- | --- | --- | --- |
| PE.L1-3.10.1 | Limit Physical Access | L1 | Not implemented | - | - | - | - |
| PE.L2-3.10.2 | Monitor Facility | L2 | Not implemented | - | - | - | - |
| PE.L1-3.10.3 | Escort Visitors | L1 | Not implemented | - | - | - | - |
| PE.L1-3.10.4 | Physical Access Logs | L1 | Not implemented | - | - | - | - |
| PE.L1-3.10.5 | Manage Physical Access | L1 | Not implemented | - | - | - | - |
| PE.L2-3.10.6 | Alternative Work Sites | L2 | Not implemented | - | - | - | - |

### Risk assessment (RA)

| Requirement | Title | Level | Status | Statement | Responsible | Evidence | POA&M |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RA.L2-3.11.1 | Risk Assessments | L2 | Not implemented | - | - | - | - |
| RA.L2-3.11.2 | Vulnerability Scan | L2 | Not implemented | - | - | - | - |
| RA.L2-3.11.3 | Vulnerability Remediation | L2 | Not implemented | - | - | - | - |

### Security assessment (CA)

| Requirement | Title | Level | Status | Statement | Responsible | Evidence | POA&M |
| --- | --- | --- | --- | --- | --- | --- | --- |
| CA.L2-3.12.1 | Security Control Assessment | L2 | Not implemented | - | - | - | - |
| CA.L2-3.12.2 | Plan of Action | L2 | Not implemented | - | - | - | - |
| CA.L2-3.12.3 | Security Control Monitoring | L2 | Not implemented | - | - | - | - |
| CA.L2-3.12.4 | System Security Plan | L2 | Not implemented | - | - | - | - |

### System and communications protection (SC)

| Requirement | Title | Level | Status | Statement | Responsible | Evidence | POA&M |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SC.L1-3.13.1 | Boundary Protection | L1 | Not implemented | - | - | - | - |
| SC.L2-3.13.2 | Security Engineering | L2 | Not implemented | - | - | - | - |
| SC.L2-3.13.3 | Role Separation | L2 | Not implemented | - | - | - | - |
| SC.L2-3.13.4 | Shared Resource Control | L2 | Not implemented | - | - | - | - |
| SC.L1-3.13.5 | Public-Access System Separation | L1 | Not implemented | - | - | - | - |
| SC.L2-3.13.6 | Network Communication by Exception | L2 | Not implemented | - | - | - | - |
| SC.L2-3.13.7 | Split Tunneling | L2 | Not implemented | - | - | - | - |
| SC.L2-3.13.8 | Data in Transit | L2 | Not implemented | - | - | - | - |
| SC.L2-3.13.9 | Connections Termination | L2 | Not implemented | - | - | - | - |
| SC.L2-3.13.10 | Key Management | L2 | Not implemented | - | - | - | - |
| SC.L2-3.13.11 | CUI Encryption | L2 | Not implemented | - | - | - | - |
| SC.L2-3.13.12 | Collaborative Device Control | L2 | Not implemented | - | - | - | - |
| SC.L2-3.13.13 | Mobile Code | L2 | Not implemented | - | - | - | - |
| SC.L2-3.13.14 | Voice over Internet Protocol | L2 | Not implemented | - | - | - | - |
| SC.L2-3.13.15 | Communications Authenticity | L2 | Not implemented | - | - | - | - |
| SC.L2-3.13.16 | Data at Rest | L2 | Not implemented | - | - | - | - |

### System and information integrity (SI)

| Requirement | Title | Level | Status | Statement | Responsible | Evidence | POA&M |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SI.L1-3.14.1 | Flaw Remediation | L1 | Not implemented | - | - | - | - |
| SI.L1-3.14.2 | Malicious Code Protection | L1 | Not implemented | - | - | - | - |
| SI.L2-3.14.3 | Security Alerts & Advisories | L2 | Not implemented | - | - | - | - |
| SI.L1-3.14.4 | Update Malicious Code Protection | L1 | Not implemented | - | - | - | - |
| SI.L1-3.14.5 | System & File Scanning | L1 | Not implemented | - | - | - | - |
| SI.L2-3.14.6 | Monitor Communications for Attacks | L2 | Not implemented | - | - | - | - |
| SI.L2-3.14.7 | Identify Unauthorized Use | L2 | Not implemented | - | - | - | - |

## 14. Completion and approval dates

Version 0.1 is a draft. The authorising official (ceo@example.com) approves it by recording a sign-off in `scope/ssp.yaml`, through a reviewed pull request.

## 15. Ongoing system security plan maintenance

`hound ssp build` rebuilds this plan from CISO Assistant and the configuration repo. Review it at least once a year and after any significant change to the system.

## Appendix: plan of action and milestones

| Requirement | Status | Control | Owner | ETA | Control status |
| --- | --- | --- | --- | --- | --- |
| AC.L1-3.1.1 | Not implemented | AC-MFA | it@example.com | - | in_progress |
| AC.L2-3.1.3 | Planned | AC-FLOW | it@example.com | 2026-12-31 | in_progress |

CMMC does not allow these on a POA&M (32 CFR 170.21(a)(2)):

- AC.L1-3.1.1: worth 5 points; CMMC allows only 1-point requirements on a POA&M (32 CFR 170.21(a)(2)(ii)); meet it before a Conditional Level 2 status.

A Conditional CMMC Level 2 status also needs an SPRS score of at least 88 (0.8 of 110) and every item closed within 180 days (32 CFR 170.21(a)(2)(i) and 170.21(b)). `hound sprs score` works out the score.

_DRAFT: not approved._
