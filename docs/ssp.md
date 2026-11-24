# System security plans

For a walkthrough, see [Your system security plan](govern/system-security-plan.md).

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

## Versions, sign-off and publishing

The plan is a controlled document. Until the authorising official signs a version, every copy says so: the title ends in "(DRAFT)", a banner under the title and the last line say it is not approved, and the Word file carries a "DRAFT: not approved" header on every page.

1. **Build the draft and keep its snapshot.** `hound ssp build --programme <id> --snapshot-out ssp-<id>-<version>.json` prints the draft and writes the snapshot: everything the plan was built from (the model, each value with its source, and the section texts). The log gives the draft's SHA-256 and the snapshot's. The version is `version:` in `scope/ssp.yaml`. Keep the snapshot and any generated plan **out of the repo**: they hold the organisation's system details.
2. **Review.** For a new version, `hound ssp diff <old snapshot> <new snapshot>` lists only what changed: boundary and inventory first (they usually need a new major version), then suppliers, statuses, statements and sections.
3. **Sign.** The authorising official records the sign-off under the plan in `scope/ssp.yaml`, by pull request like an audit period:

   ```yaml
   sign_offs:
     - {version: "1.0", by: ao@example.com, date: "2026-11-24", sha256: <draft SHA-256>, snapshot: <snapshot SHA-256>}
   ```

   `by` must be the plan's `authorising_official` (ISM-0047); both hashes are 64 lower-case hex digits, as the log printed them.
4. **Rebuild the signed version.** `hound ssp build --programme <id> --from-snapshot ssp-<id>-1.0.json` rebuilds the plan from the snapshot without contacting CISO Assistant. When the snapshot still gives the reviewed draft, it prints the approved plan: no draft marks, and "Approved by … SHA-256 of the reviewed draft: …". When it does not, it stops with `ssp.signoff_hash_mismatch`. A live build of a signed version whose data has since changed prints a draft and warns.
5. **Publish.** `hound ssp publish --programme <id> --snapshot ssp-<id>-1.0.json` uploads `ssp-<id>-1.0.zip` (the signed plan, its POA&M, the snapshot, and CISO Assistant's own Word audit report for the programme's audit) as a new revision of the evidence `SSP-<ID>`, "System security plan: <system name>". It refuses a version with no sign-off (`ssp.not_signed`). If the audit report cannot be downloaded, the package goes without it and the log says why.

`--format docx --out <file>` also writes the plan as a Word file (headings, lists and tables from the same text). The same plan gives the same file.

## Control descriptions drafted by your own agent

A requirement that is implemented but has no implementation statement is a gap, usually because its controls have no description. hound has no model, but your own coding agent can draft the descriptions:

1. `hound ssp suggest --programme <id>` prints a JSON brief: each control with no description behind such a gap, with its name, owner and systems, and the requirements it serves (description, status and evidence names). It holds names only, never evidence content, and sends nothing anywhere. `--from-snapshot` reads a snapshot instead of CISO Assistant.
2. The agent drafts each description and writes it with `hound ssp describe --control <ref> --text "<description>"`. That puts the text in `scope/controls.yaml` under a `# drafted with AI: review before merging` comment. Nothing goes to CISO Assistant.
3. A person accepts, edits or rejects each description in the pull request. Once merged, `hound scope apply` sends the accepted text to CISO Assistant, and the next `ssp build` uses it as the statement.

Neither command runs on a schedule.
