# Board report: 2026-Q4 (October to December 2026)

As at 2026-12-31. Figures come from the quarter-end snapshots in CISO Assistant; the monthly committee reports hold the operational detail.

## 1. Compliance position

Proposed by management for the authorising official to confirm.

- **Essential Eight: off track**. 2 of 3 requirements compliant at Maturity Level Two. 1 got worse since last quarter (E8-2). 1 non-compliant. Audit of record: [EX-E8-2026](https://ciso.example.com/compliance-assessments/ca-e8).
- **AI governance (AI6 Foundations): on track**. 1 of 1 requirements compliant at Foundations. Audit of record: [EX-AI6-2026](https://ciso.example.com/compliance-assessments/ca-ai6).

## 2. Posture

Compliant requirements out of those assessed in each group, per parent, at the end of each quarter.

### Essential Eight

| Maturity Level One | 2026-Q1 | 2026-Q2 | 2026-Q3 | 2026-Q4 |
| --- | --- | --- | --- | --- |
| Multi-factor authentication | 0/1 | 0/1 | 1/1 | 1/1 |
| rb | 1/1 | 1/1 | 1/1 | 1/1 |
| **All** | 1/2 | 1/2 | 2/2 | 2/2 |

| Maturity Level Two | 2026-Q1 | 2026-Q2 | 2026-Q3 | 2026-Q4 |
| --- | --- | --- | --- | --- |
| Multi-factor authentication | 0/2 | 0/2 | 1/2 | 1/2 |
| rb | 1/1 | 1/1 | 1/1 | 1/1 |
| **All** | 1/3 | 1/3 | 2/3 | 2/3 |

### AI governance (AI6 Foundations)

| Foundations | 2026-Q1 | 2026-Q2 | 2026-Q3 | 2026-Q4 |
| --- | --- | --- | --- | --- |
| 1 | 1/1 | 1/1 | 1/1 | 1/1 |
| **All** | 1/1 | 1/1 | 1/1 | 1/1 |

Register: 2 ai_systems. screening: normal 1, elevated 1.

## 3. Key risks

The highest residual risks.

| Risk | Level (current → residual) | Above appetite | Owner | Treatment | Target |
| --- | --- | --- | --- | --- | --- |
| [R-2 Supplier breach exposes drawings](https://ciso.example.com/risk-scenarios/r-2) | High → High | not recorded | Example CTO | transfer | 2027-06-30 |
| [R-1 Ransomware on file server](https://ciso.example.com/risk-scenarios/r-1) | High → Medium | not recorded | Example CTO | mitigate | 2027-03-31 |
| [R-3 Phishing leads to account takeover](https://ciso.example.com/risk-scenarios/r-3) | Medium → Low | not recorded | Example IT lead | mitigate | 2026-12-31 |
| [R-4 Laptop theft](https://ciso.example.com/risk-scenarios/r-4) | Medium → Low | not recorded | Example IT lead | mitigate | not set |
| [R-5 Insider misuse](https://ciso.example.com/risk-scenarios/r-5) | Low → Low | not recorded | Example CTO | accept | not set |

## 4. Risk-acceptance decisions

Accepted on the board's behalf this quarter. Anything valid for more than 12 months, or with no expiry, needs the board's endorsement.

| Item | Kind | Accepted by | On | Expires | |
| --- | --- | --- | --- | --- | --- |
| [SE-CAD CAD workstation without application control](https://ciso.example.com/security-exceptions/se-1) | security exception | Example CTO | 2026-10-14 | 2028-04-01 | **board endorsement** |
| [RA-KIOSK Legacy kiosk](https://ciso.example.com/risk-acceptances/ra-1) | risk acceptance | Example CTO | 2026-11-03 | 2027-05-01 |  |

## 5. Assurance

- Coverage of Essential Eight at Maturity Level Two: 1 of 3 requirements (33%) checked automatically, 2 attested by people, 0 unanswered.
- Coverage of AI governance (AI6 Foundations) at Foundations: not measured: no mapping is configured for the audit.
- Evidence freshness: 1 of 2 collectors succeeded in the last 7 days of the quarter.
- Supplier assurance: 1 of 2 supplier reviews due by the quarter's end are complete.
- Independent review: hound does not track it; record the last independent review as evidence in CISO Assistant and state it here.

## 6. Decisions and asks of the board

1. Confirm the compliance position of each programme (section 1).
2. Endorse SE-CAD CAD workstation without application control, accepted by Example CTO until 2028-04-01 (section 4).
3. Ask management for a recovery plan for Essential Eight by the next quarter.

## Appendix: operational detail

For reference; the committee reports hold the detail and the actions.

| Collector | Last run (UTC) | Outcome | Problems |
| --- | --- | --- | --- |
| example-m365 | 2026-12-31 03:00 | Succeeded | - |
| example-sophos | 2026-12-01 03:00 | Failed | Sophos Central refused the token (401). |

Overdue manual evidence and supplier reviews: 2 (Backups: restore test, Example backup owner, due 2026-10-15; Example Mail review 2026, Example GRC lead, due 2026-11-30).
Open remediation items: 0, of which overdue: 0.
