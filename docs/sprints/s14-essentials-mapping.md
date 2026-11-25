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

**Rehearsal log (2026-11-25).** A novice GRC operator with a few hours a week followed the migration guide, the DISP readiness guide,
`hound init --framework disp`, the template runbooks, the manual evidence and weekly review steps and the cheat sheet, from source in the
cloud session, including as a non-root user with the default evidence root. 19 findings; fixed here (red, green):

- `hound mapping derive --out` silently replaced the reviewed reference mapping (the guide told you to). It now refuses an existing file
  without `--force`, and the guide derives to a new file.
- Coverage counted draft rows as covered. A Draft column now counts rows only draft rows answer; `hound mapping validate` says how many
  rows are draft. A `--manual` path with no manual source in it is reported, as every attested check then reads as automated.
- Any `NAME_FILE` variable from another tool pointing at a file the user cannot read crashed every command. Such files are now skipped.
- A user who cannot write the default evidence root got "Unexpected failure" and a stack-trace hint. It now says to set `evidence.root`
  or `chown` the folder; guided setup says the default needs root or the container.
- The README `hound init --framework disp` writes now has a DISP step (copy its manual sources, check coverage); `init --help` and
  guided setup list `--framework` and the DISP files.
- Docs: the migration guide's elided coverage command, "four" for five ISM controls, and a maintainer step written as a user step; the
  DISP guide's incident register source and the example repo stopping at step 3; `collectors: []` to replace before pasting a template;
  the first deployment tutorial's file names for an `init` repo; the cheat sheet PDF re-rendered.
- Already fixed this sprint: the per-parent coverage table names each Essentials principle with its domain.

Carried to discovered work: findings 8 (exit codes), 13 to 15.

**Functional check**
- Result (2026-11-25): with a draft from `derive`, `hound mapping coverage <draft> --framework frameworks/asd-essentials-enterprise-it.json
  --groups 1` prints `Total: 0 automated, 0 manual, 55 draft, 38 uncovered` (`MappingCoverageCommandTests`); deriving over an existing
  file exits 2 and keeps it (`MappingDeriveCommandTests`).

### s14-04 Improve: quality and flow (standing, about 15% of each sprint)
- [ ] Done

As [s12-10](s12-e8-mapping-and-adoption.md#s12-10-improve-quality-and-flow-standing-about-15-of-each-sprint).

- Triage of s13's Discovered work:
  - Release pins stamped by hand: done. `scripts/stamp-release.sh x.y.z` moves `HOUND_RUNNER_IMAGE`, `EVIDENCE_HOUND_REF` and the
    tutorial's image to a release (bats tests; `docs/releasing.md`).
  - Non-root user with the default evidence root: proven by this sprint's rehearsal, which found the unexplained failure (fixed in s14-03).
  - nuget.org trusted publishing: stays as discovered work.
- Dependencies: Dependabot updates folded into the sprint branch: test tools with xunit.v3 aligned on 4.0.3 (also in the collector
  template, which Dependabot now watches), Confluent.Kafka, CycloneDX, code coverage, the runtime image digest, Caddy, Redpanda in the dev
  stack, markdownlint-cli2 0.24, mkdocs-material. Decided 2026-11-25: stay on .NET 10 LTS and PostgreSQL 16, so the .NET 11 SDK image,
  Microsoft.Extensions 11 and PostgreSQL 18 (which cannot open an existing data folder without a dump and restore) were closed, and
  Dependabot ignores those majors.
- The coverage report's per-parent table names each Essentials principle with its domain, as the same principle sits in several domains.
- Cheat sheet PDF re-rendered for `mapping derive` and `init --framework`.

### s14-05 Fill rows whose checks come from several collectors (added 2026-11-25)
- [ ] Done

**Story.** The DISP cyber rows and many Essentials rows join checks from several collectors (Microsoft 365 and a manual source, say).
A run only filled a row one collector fully reported, so those rows were never filled. CI's functional tests found it.

**Acceptance criteria**
- Each collector run saves its latest check results and evidence list beside its runs.
- A run fills a row when its own checks and the latest saved results of the other configured collectors together answer every check, and
  at least one check is its own. Each check counts its newest result; the row cites every contributing collector's evidence.
- A row no combination fully answers is still left alone, so no collector marks another's row as not assessed.

**Tests to write first**
- Ownership with other collectors' results; a runner test where two collectors on separate runs fill one row with both evidence.

## Discovered work

- s14-03: `hound mapping coverage` and `validate` exit 0 with uncovered or draft rows; a `--fail-on-uncovered` (and draft) flag would let
  CI gate on them. Coverage also counts as automated a check that no configured collector gives; reading collector check IDs from the
  config would show those as unsourced.
- s14-03: `hound add collector manual` writes `due_days` as a quoted string, and its "Next:" hint suggests `config validate
  --check-secrets`, which fails before CISO Assistant has a token.
- s14-03: `hound manual upload` takes `--source`, `hound manual propose` a positional source; upload prints `(attested: False)`.
- s14-03: `hound review pending` recommends `attest` from a `partial` proposal, but `review recommend --answer` takes accept or decline.
- s14-03: an incident register manual source template (the example repo has one); s15's incident register may replace it.
- s14-03: `hound config validate` could check that the evidence root is writable.
- s14-04: nuget.org trusted publishing (OIDC), carried from s13.

## Sprint review
