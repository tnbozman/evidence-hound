# s13 ASD Essentials mapping

Status: Planned

Release: 2

Was s11-01 before the release 1 reorder. It is the first sprint of release 2 so that it can move back into release 1 on its own if the Essentials migration comes sooner.

**Goal:** an ASD Essentials for enterprise IT mapping ready for the migration, priority phase 1 first, derived from the completed Essential Eight mapping (s11-01). The Essential Eight 2023 audit stays the audit of record until the migration's cut-over.

### s13-01 ASD Essentials mapping file
- [ ] Done

**Story:** As a compliance owner, I want the reference checks mapped to the ASD Essentials for enterprise IT, so that a new deployment produces Essentials results on its first run, and an organisation still on the Essential Eight can run both during its transition.

See [the migration plan](../migrations/essential-eight-to-asd-essentials.md).

**Acceptance criteria**
- `mappings/asd-essentials-enterprise-it.yaml` targets `urn:intuitem:risk:framework:asd-essentials-enterprise-it` and names requirements by URN (s06-01), so each action is mapped per domain.
- `hound mapping derive` writes a draft Essentials mapping from the Essential Eight mapping and CISO Assistant's transition mapping. Each action row gets the union of its source controls' checks and is marked `draft: true`; publishing refuses draft rows until a person removes the mark.
- Every check ID from the reference and manual collectors maps to at least one priority phase 1 action; every phase 1 action either has checks or is listed under `manual_only` with a suggested manual source.
- `hound mapping coverage` (s11-01) also prints automated, manual-only and uncovered counts per Essentials domain and priority phase.

**Tests first**
- `EssentialsMappingTests.All_requirement_urns_exist_in_framework_snapshot`
- `EssentialsMappingTests.Every_reference_check_is_mapped`
- `DeriveTests.Action_gets_union_of_source_checks_marked_draft`
- `PublishTests.Draft_rows_are_not_published`
- `CoverageTests.Counts_per_essentials_domain_and_phase`

**Functional check**
- Against the local CISO Assistant with both libraries loaded: `hound run` with recordings fills the example Essentials and Essential Eight 2023 audits; the coverage report matches the audits.

## Discovered work

## Sprint review
