# System security plans

`hound ssp build --programme <id>` prints a programme's system security plan (SSP). It uses one layout for every framework: **NIST SP 800-18 Rev 1, Appendix A** ([Guide for Developing Security Plans for Federal Information Systems](https://nvlpubs.nist.gov/nistpubs/legacy/sp/nistspecialpublication800-18r1.pdf)). The only part that changes with the framework is the controls annex (section 13):

| `profile` in `ssp.yaml` | Annex |
| --- | --- |
| `ism` | ISM controls for the Essential Eight, each listed once with the strategies it serves (for example ISM-1815 under `mfa, rap, ac, uah`). |
| `nist-800-171` | NIST SP 800-171 requirements for CMMC Level 2 (s17). |

Assessors and primes recognise this layout. hound does not use a separate layout per framework, such as the ASD Blueprint SSP template.

## How the plan meets ISM-0041

ISM-0041 asks for "an overview of the system (covering the system's purpose, the system boundary and how the system is managed) as well as an annex that covers applicable security controls".

| ISM-0041 asks for | Section of the plan | Comes from |
| --- | --- | --- |
| The system's purpose | 9. General description and purpose | `sections.purpose` (you write it) |
| The system boundary | 1. System name and identifier (perimeter), 10. System environment (inventory), 11. Interconnections and external service providers | `perimeter` in `ssp.yaml`; systems, solutions and suppliers in CISO Assistant; `sections.environment` |
| How the system is managed | 3 to 6 (owner, authorising official, contacts, assignment of security responsibility) | `roles` in `ssp.yaml`; `sections.management` |
| An annex of applicable controls | 13. Security controls (annex) | The programme's framework snapshot and its audit in CISO Assistant |

## Sections

| Section | Content |
| --- | --- |
| Gaps | Anything an assessor would not accept yet. Listed first; `--strict` exits 1 while any remain. |
| 1. System name and identifier | Name, boundary (perimeter) and audit of record. |
| 2. System categorisation | `classification`. |
| 3. System owner | `roles.system_owner`. |
| 4. Authorising official | `roles.authorising_official`. |
| 5. Other designated contacts | `roles.security_contact`. |
| 6. Assignment of security responsibility | `sections.management`. |
| 7. System operational status | `operational_status`. |
| 8. System type | `system_type`. |
| 9. General description and purpose | `sections.purpose`. |
| 10. System environment | `sections.environment`, then the inventory from CISO Assistant. |
| 11. Interconnections and external service providers | Solutions and suppliers from CISO Assistant. |
| 12. Applicable laws, regulations and policies | `sections.laws_and_policies`, then the `documents`. |
| 13. Security controls (annex) | One row per control in the programme's groups: applicability, status, statement, responsible party and the POA&M item that carries it. A control with no row in the audit reads *Not implemented*. |
| 14. Completion and approval dates | The version; the authorising official approves the plan in CISO Assistant. |
| 15. Ongoing system security plan maintenance | Rebuild with `hound ssp build`; review at least once a year and after significant change. |
| Appendix: plan of action and milestones | The same items as `hound ssp poam`. |

## The ISM annex's columns

The `ism` annex follows the columns of ASD's Blueprint *System Security Plan Annex Template* (the template the ISM's documentation guideline points to), next to the SP 800-18 status the plan and POA&M use:

| Column | Comes from |
| --- | --- |
| Control, Description | The framework snapshot (`hound framework snapshot` keeps each requirement's full text). |
| Strategies | The parent nodes the control is listed under. An ISM control that serves several strategies appears once. |
| Maturity levels | The implementation groups the control is in. |
| Control type | *Policy*, *Procedure*, *Technical* or *Documentation*, from the reference controls CISO Assistant's library links the requirement to (ids starting `pol.`, `proc.`, `tech.` and `doc.`). |
| Responsible entity | `responsible_entity` on a linked control in `controls.yaml`, if set. Otherwise *Shared* when a linked control covers a system a supplier's solution provides, else *Consumer*. |
| Implementation status | The ASD template's terms, from the requirement's result in CISO Assistant (see below). |
| SP 800-18 status, Statement, Responsible, POA&M | As described above. |

| Result in CISO Assistant | Implementation status (ASD) | SP 800-18 status |
| --- | --- | --- |
| `compliant` | Effective | Implemented |
| `partially_compliant` | Ineffective | Partially implemented |
| `non_compliant` | Not implemented | Planned or Not implemented |
| `not_applicable` | Not applicable | Not applicable |
| `not_assessed`, or no row | Not assessed | Planned or Not implemented |

*Partially compliant* maps to *Ineffective*, the template's nearest term; the maintainer reviews this choice. The template's *Alternate control* and *No visibility* have no CISO Assistant result, so hound does not use them yet.

A section file that does not exist yet prints as "_Section `name` is not written yet._", so a draft still builds.

`--json` gives the same plan with every value and its source, including the annex (`annex[]`, with `poam` linking each unmet row to its POA&M item).
