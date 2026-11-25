# Security committee report: November 2026

Period 2026-11-01 to 2026-11-30, as at the end of the month. Snapshot taken 2026-11-30 18:00 UTC; compared with 2026-10.

## 1. Summary

- Requirement results changed since last month: 2 (1 better, 1 worse).
- Exceptions and risk acceptances expiring within 60 days: 1. Expired this month: 1.
- Remediation items overdue: 2.
- Manual evidence and supplier reviews overdue: 2 (1 for a second month).
- Decisions needed: confirm, change or drop the proposed actions in section 8 (8).

## 2. Collector health

| Collector | Last run (UTC) | Outcome | Checks passed | Checks failed | Problems |
| --- | --- | --- | --- | --- | --- |
| example-m365 | 2026-11-30 03:00 | Succeeded | 18 | 2 | - |
| example-sophos | 2026-11-12 03:00 (stale) | Failed | 0 | 0 | Sophos Central refused the token (401). |

Stale: no run in the 7 days before the end of the month, so its evidence may be past its freshness window.

## 3. Control status changes

### Essential Eight

Audit of record: [EX-E8-2026](https://ciso.example.com/compliance-assessments/ca-e8).

| Group | Compliant | Partially compliant | Non-compliant | Not assessed | Not applicable |
| --- | --- | --- | --- | --- | --- |
| Maturity Level One | 1 | 0 | 1 | 0 | 0 |
| Maturity Level Two | 1 | 0 | 1 | 1 | 0 |

#### Multi-factor authentication

- [E8-1](https://ciso.example.com/requirement-assessments/e8-1): non-compliant → compliant

#### rb

- [E8-3](https://ciso.example.com/requirement-assessments/e8-3): compliant → non-compliant

### AI governance (AI6 Foundations)

Audit of record: [EX-AI6-2026](https://ciso.example.com/compliance-assessments/ca-ai6).

| Group | Compliant | Partially compliant | Non-compliant | Not assessed | Not applicable |
| --- | --- | --- | --- | --- | --- |
| Foundations | 0 | 1 | 0 | 0 | 0 |

No result changed.

Register: 2 ai_systems. screening: normal 1, elevated 1.

### Applied controls

- [AC-MFA MFA for all users](https://ciso.example.com/applied-controls/ac-mfa): in_progress → active

## 4. Exceptions and risk acceptances

### New

- [RA-KIOSK](https://ciso.example.com/acceptances/ra-kiosk) Legacy kiosk (risk acceptance): accepted by Example CTO on 2026-11-03, expiry 2027-05-01.

### Expiring within 60 days

- [SE-PRINTER](https://ciso.example.com/acceptances/se-printer) Old printer (security exception): approved by Example CTO on 2026-01-10, expiry 2027-01-10.

### Expired

- [RA-FAX](https://ciso.example.com/acceptances/ra-fax) Fax gateway (risk acceptance): accepted by Example CTO on 2025-11-01, expiry 2026-11-20.

## 5. Remediation

Open: 3. Completed this month: 1 ([FND-2](https://ciso.example.com/findings/fnd-2)). Overdue: 2.

### Overdue

| Item | Owner | Due | Days overdue | Status |
| --- | --- | --- | --- | --- |
| [FND-1 Unsupported OS on two laptops](https://ciso.example.com/findings/fnd-1) | Example IT lead | 2026-10-20 | 41 | in_progress |
| [FND-3 Backup job failing on file server](https://ciso.example.com/findings/fnd-3) | Example backup owner | 2026-11-10 | 20 | in_progress |

### Open, by owner

| Item | Owner | Due | Days overdue | Status |
| --- | --- | --- | --- | --- |
| [FND-4 Missing firewall log forwarding](https://ciso.example.com/findings/fnd-4) | Example IT lead | 2026-12-15 | - | to_do |

## 6. Incidents

Opened: 2. Closed: 1.

| Incident | Severity | Reported | Closed | Status |
| --- | --- | --- | --- | --- |
| [INC-7 Phishing email clicked](https://ciso.example.com/incidents/in-7) | 3 | 2026-11-05 | 2026-11-08 | closed |
| [INC-8 Lost laptop](https://ciso.example.com/incidents/in-8) | 2 | 2026-11-25 | - | ongoing |

## 7. Manual evidence and third parties

| Item | Kind | Owner | Due | State |
| --- | --- | --- | --- | --- |
| [Backups: restore test](https://ciso.example.com/tasks/21) | manual evidence | Example backup owner | 2026-10-15 | **overdue, second month** |
| [Privileged access review](https://ciso.example.com/tasks/24) | manual evidence | Example IT lead | 2026-11-28 | overdue |
| [Example Cloud review 2026](https://ciso.example.com/tasks/25) | supplier review | Example GRC lead | 2026-12-10 | due |

Overdue items are carried into section 8 until they are completed: the committee is the escalation after the reminder emails.

## 8. Actions

Proposed for the committee to confirm, change or drop. Record the confirmed ones in CISO Assistant as tasks.

| # | Action | Owner | Due | From |
| --- | --- | --- | --- | --- |
| 1 | Complete Backups: restore test (overdue a second month) | Example backup owner | 2026-12-14 | section 7 |
| 2 | Complete Privileged access review | Example IT lead | 2026-12-14 | section 7 |
| 3 | Complete FND-1 Unsupported OS on two laptops or agree a new due date | Example IT lead | 2026-12-14 | section 5 |
| 4 | Complete FND-3 Backup job failing on file server or agree a new due date | Example backup owner | 2026-12-14 | section 5 |
| 5 | Renew or close RA-FAX Fax gateway: it expired on 2026-11-20 | Example CTO | 2026-12-14 | section 4 |
| 6 | Review SE-PRINTER Old printer before it expires | Example CTO | 2027-01-10 | section 4 |
| 7 | Fix collector example-sophos: Sophos Central refused the token (401). | Collector operator | 2026-12-07 | section 2 |
| 8 | Resolve data.authority_expired: scope/setup.yaml line 40: supplier SUP-EXMAIL's export control authority expired on 2026-11-01. | Cyber GRC lead | 2026-12-30 | scope warning |
