# s14 ASD Essentials mapping

Status: In progress

Release: 2

Was s12-01 before the release 1 reorder. It is the first sprint of release 2 so that it can move back into release 1 on its own if the Essentials migration comes sooner.

**Goal:** an ASD Essentials for enterprise IT mapping ready for the migration, priority phase 1 first, derived from the completed Essential Eight mapping (s12-01). The Essential Eight 2023 audit stays the audit of record until the migration's cut-over.

### s14-01 ASD Essentials mapping file
- [ ] Done

**Story:** As a compliance owner, I want the reference checks mapped to the ASD Essentials for enterprise IT, so that a new deployment produces Essentials results on its first run, and an organisation still on the Essential Eight can run both during its transition.

See [the migration plan](../migrations/essential-eight-to-asd-essentials.md).

**Acceptance criteria**
- `mappings/asd-essentials-enterprise-it.yaml` targets `urn:intuitem:risk:framework:asd-essentials-enterprise-it` and names requirements by URN (s06-01), so each action is mapped per domain.
- `hound mapping derive` writes a draft Essentials mapping from the Essential Eight mapping and CISO Assistant's transition mapping. Each action row gets the union of its source controls' checks and is marked `draft: true`; publishing refuses draft rows until a person removes the mark.
- Every check ID from the reference and manual collectors maps to at least one priority phase 1 action; every phase 1 action either has checks or is listed under `manual_only` with a suggested manual source.
- `hound mapping coverage` (s12-01) also prints automated, manual-only and uncovered counts per Essentials domain and priority phase.

**Tests first**
- `EssentialsMappingTests.All_requirement_urns_exist_in_framework_snapshot`
- `EssentialsMappingTests.Every_reference_check_is_mapped`
- `DeriveTests.Action_gets_union_of_source_checks_marked_draft`
- `PublishTests.Draft_rows_are_not_published`
- `CoverageTests.Counts_per_essentials_domain_and_phase`

**Functional check**
- Against the local CISO Assistant with both libraries loaded: `hound run` with recordings fills the example Essentials and Essential Eight 2023 audits; the coverage report matches the audits.
- Result (2026-11-25): `hound mapping coverage mappings/asd-essentials-enterprise-it.yaml --framework frameworks/asd-essentials-enterprise-it.json --groups 1 --manual examples/manual-sources --manual examples/example-essential-eight.yaml` printed the four phase 1 domains (governance 3 automated, 6 manual; visibility 1, 11; boundary 11, 26; identity 11, 24) and `Total: 26 automated, 67 manual, 0 uncovered`, so 93 phase 1 rows, 92 mapped and IT.GOV-01.1 recorded by hand. `hound mapping derive` from the Essential Eight mapping wrote 55 draft rows and listed 38 without checks, which the reviewed mapping fills from the new templates, Microsoft 365 (legacy authentication) and the ciso-assistant profile (supplier assurance). The run against CISO Assistant is `TwoAuditsFunctionalTests` (now with the real Essentials mapping: changed plus unchanged rows of EX-ESSENTIALS-2026 equal the mapping's 92 rows), run by CI's functional job, as CISO Assistant's images cannot be pulled in this cloud session.
- Notes: the custom library sits in `frameworks/` beside the other libraries, not a new `libraries/` folder. The reference mapping's rows were checked with AI help and the draft marks removed; its header says to review it in your own copy, as the 800-171 crosswalk does.

### s14-02 DISP membership readiness beyond the Essential Eight (added 2026-11-24)
- [ ] Done

Added at the maintainer's request (2026-11-24): Defence checks DISP membership itself, which covers governance, personnel and physical
security as well as cyber security.

**Story:** As a small supplier applying for or keeping DISP membership, I want the governance, personnel, physical and cyber security
expectations held as an audit beside the Essential Eight, so that one place shows what Defence will check and what evidence answers it.

**Acceptance criteria**
- A custom framework library in `libraries/` for DISP membership readiness, written from the public Defence Security Principles
  Framework control areas (governance, personnel, physical, information and cyber security), with requirement text paraphrased and the
  source cited; loaded through the scope like any custom library.
- A mapping reuses existing evidence where it applies: the Essential Eight checks for the cyber area, supplier records (s10) for the
  supply chain, and the incident register (s15-07) for reporting.
- Manual source templates for what no system holds: security officer appointment, clearances register review, security awareness
  training records, facility and visitor controls, and the annual security report to Defence.
- `hound init --framework disp` adds the audit beside the Essential Eight one.

**Tests first**
- `DispLibraryTests.Every_requirement_has_a_source_area_and_no_copied_text`
- `DispMappingTests.Cyber_area_reuses_essential_eight_checks`
- `ExampleManualSourcesTests.Every_template_config_validates` (new templates)

**Functional check**
- Apply the example DISP audit to the local CISO Assistant; one `hound run` fills its cyber rows from the Essential Eight evidence.
- Result (2026-11-25): `hound mapping validate mappings/disp.yaml --framework frameworks/disp.json` printed `valid: 24 requirements` (25 in the library, DISP-INF-01 recorded by hand under `manual_only`); `hound init /tmp/x --yes --framework disp` wrote 15 files, audits EX-E8 and EX-DISP, and the repo validates (`InitCommandTests`). The example compliance repo declares the library and the audit EX-DISP-2026, so the functional fixture's `hound scope apply` uploads and loads it; `DispRunFunctionalTests` then runs the recorded Microsoft 365 tenant against EX-E8-2026 and EX-DISP-2026 and checks DISP-CYB-07 cites the same conditional access evidence as ISM-1504. Run by CI's functional job.
- Notes: the library is in `frameworks/` like AI6 (not a new `libraries/` folder). It has one group, M (membership): the DSPF's levels of membership are not modelled, so the guide says to check your level's obligations. The security awareness training template is shared with the ASD Essentials. The incident register is the example's `example-incident-register` manual source until the s15 register lands. Guide: `docs/adopt/disp-readiness.md`.

### s14-03 Rehearse trial, onboarding and sustaining GRC (standing, every sprint)
- [ ] Done

As [s12-09](s12-e8-mapping-and-adoption.md#s12-09-rehearse-trial-onboarding-and-sustaining-grc-standing-every-sprint), including this sprint's new commands and guides.

### s14-04 Improve: quality and flow (standing, about 15% of each sprint)
- [ ] Done

As [s12-10](s12-e8-mapping-and-adoption.md#s12-10-improve-quality-and-flow-standing-about-15-of-each-sprint).

## Discovered work

## Sprint review
