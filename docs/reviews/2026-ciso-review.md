# CISO review, November 2026

A CISO's review of Evidence Hound before release 3 (s19-07). It asks one question: does `hound` get a small business with one GRC
member and two to eight hours a week to Essential Eight Maturity Level 2 or CMMC Level 2, and keep it there, and what will an
assessor ask for that it does not give?

It walks the fictional Example Pty Ltd (`examples/compliance-repo/`) through a year, checks every automated claim the Essential Eight
and CMMC mappings make against the code of the check behind it, and ranks what to cut and what is missing. Every gap that would fail
an assessment is either fixed in s19 or written into the release notes as a known limitation.

Reviewed 2026-11-26 against the branch after s19-06.

## Verdict

`hound` is fit for release 3 for its target: a small business whose estate is Microsoft 365, Intune-managed Windows laptops, a
firewall managed in Sophos Central and a vulnerability scanner, working towards Essential Eight ML2, with CMMC Level 2 as a second
programme from the same evidence. The year below takes the GRC member a little over 2 hours a week on average, inside the 2 to 8 hour
budget. Collection, mapping, publishing and the reports are deterministic, and nothing is approved without a person.

Four mapping claims were wider than their checks (fixed below), and one guide and one cheat sheet line were out of date (fixed). What
remains are limits of scope, not defects, and the release notes say each one plainly. The largest is that a CMMC assessor assesses the
320 objectives of NIST SP 800-171A, while `hound` records each of the 110 practices: the person answers the objectives in CISO
Assistant.

## A year at Example Pty Ltd

Times are the GRC member's, from the procedure (`docs/govern/continuous-compliance-procedure.md`), the minutes `hound next` estimates
for each item, and the walk-through below. Every `hound` command in the walk-through answered in under a second on the example repo.

| Step | What `hound` does | What the person does | Their time | What an assessor asks for that `hound` does not give |
| --- | --- | --- | --- | --- |
| Onboarding (month 1) | `hound init` writes the repo; `hound agent setup` the agent kit; the agent drafts suppliers, systems and each requirement's decision; `scope plan` and `scope apply` write CISO Assistant | Approves each draft with `hound approve` or the guided review; sets owners; uploads the first manual evidence | 12 to 20 hours over four weeks | Evidence that each Essential Eight control was *tested*, such as an attempt to run a macro from the internet (the ASD assessment process rates configuration evidence below a test). `hound` reads configuration. |
| Weekly review | Collectors run on schedule; `hound next` and `review prepare` list what is due with a recommendation for each; accepted items are applied in one batch | Reads and accepts or challenges each item in `hound review --for 60m` | 30 to 60 minutes | Nothing missing for the weekly cycle. |
| Monthly committee | `report snapshot` and `report committee` from CISO Assistant and the run store | Presents the report; records decisions | 1 hour | Minutes of the committee's decisions are the person's to keep. |
| Quarterly board | `report board` from the last four quarter-end snapshots | Presents it; the authorising official confirms positions | 2 hours a quarter | Nothing missing. |
| An incident (INC-2026-01) | `incident obligations` lists each report owed and its deadline; `incident report-pack --dfars` assembles the 72-hour DFARS report; `irp build` the plan | Decides severity; makes the reports (DIBNet needs a medium assurance certificate); runs the lessons-learned review | 2 to 6 hours | The forensic image and the 90-day media preservation DFARS 252.204-7012 asks for are outside `hound`. |
| A supplier change | `supplier draft`, the supplier review and flow-down findings; `supplier list --data cui` answers the prime | Approves the supplier before the system is registered; files the provider's authorisation (such as a FedRAMP Moderate equivalent) | 1 to 2 hours | Nothing missing once the authority and its evidence are recorded on the solution. |
| Annual affirmation | `sprs score` with each deduction; `sprs affirmation-pack`; `ssp build` and `ssp poam` | Reviews the score; the affirming official affirms and posts the score in SPRS | 4 hours | `hound` does not post to SPRS; the person does. |
| Assessment | The audit of record in CISO Assistant with every result's evidence and history; the SSP, POA&M and IRP; `scope export --with-files` | Walks the assessor through it; answers what is manual | 2 to 3 days with the assessor | For CMMC, the 320 assessment objectives: `hound` assesses the 110 practices, so each objective is the person's. For the Essential Eight, test evidence (above). |

Across the year that is about 110 hours (onboarding 16, weekly reviews 39, committee 12, board 8, an incident 4, three supplier
changes 5, the affirmation 4, the assessment 20), a little over 2 hours a week.

### Walk-through on the example repo

Run on a copy of `examples/compliance-repo/` with `HOUND_TODAY=2027-02-15`, offline:

| Command | Result | Time |
| --- | --- | --- |
| `hound init <folder> --org "Example Pty Ltd" -y --json` | A new repo, no audit until a system is onboarded | 0.4 s |
| `hound config validate hound.yaml` | valid | 0.3 s |
| `hound scope validate scope --config hound.yaml` | 1 domain, 7 controls, 6 audits, 1 risk study, 2 metrics | 0.6 s |
| `hound next --repo . --config hound.yaml --json` | The week's work, with the connection not checked offline | 0.6 s |
| `hound review pending --repo . --json` and `review prepare` | Nothing waiting on the person | 0.6 s each |
| `hound programme list --scope scope --json` | The programmes with their audits of record | 0.5 s |
| `hound supplier list --data cui --repo .` | No solution lists CUI: the example records CUI on its systems' `cmmc_asset_category` tags, not on a supplier | 0.3 s |
| `hound irp build --programme essential-eight` | The plan, gaps first | 0.6 s |

The steps that read CISO Assistant (`incident obligations`, `incident report-pack`, `ssp poam`, `sprs score`, the reports) could not run
here: this environment's network policy blocks the CISO Assistant images. They run against a real instance in the functional suite in CI
(`IncidentFunctionalTests`, `SprsFunctionalTests`, `SspFunctionalTests`, `ReportFunctionalTests`), and the sprint review records that
run.

The walk-through found the cheat sheet listing `--config` and `--format` on `hound irp build`, which takes neither. It is fixed, and a
test now checks every option the cheat sheet names against the command's help.

## The mappings' claims

Every Essential Eight ML1 and ML2 requirement row whose checks come from a collector (60 rows across the two levels), and every CMMC
row marked `covers: part` (32 practices), was read against the code of each check behind it.

### Fixed in s19

| Finding | What an assessor would have found | Fix |
| --- | --- | --- |
| ISM-1876 and ISM-1877 give 48 hours when the vendor rates a vulnerability critical *or* a working exploit exists. `vuln.internet_facing.patched_in_window` used the 48-hour window only for an exploit. | A critical finding on an internet-facing service ten days old, published as compliant. | Critical findings use the short window too (`PatchChecksTests.Critical_finding_on_an_internet_facing_service_uses_hours_window`). |
| ISM-1667 to ISM-1673 apply to every workstation. `endpoint.asr.office_rules_enabled` passed when the blocking policy was assigned to any group. | Office rules blocking on a pilot group only, published as compliant. | It passes only when the rules block for all users or all devices, and is partial for some groups (`OfficeChecksTests.Asr_rules_blocked_only_for_some_groups_partial`). |
| ISM-1657 lists compiled HTML, HTML applications and control panel applets beside executables, libraries, scripts and installers. The check reads the last four; the row's comment said the ruleset review covered the rest, but the row did not include it. | Three file types nobody looked at, published as compliant. | The row includes `appcontrol.rulesets_validated`, attested yearly (`E8MappingTests.Application_control_file_types_include_the_ruleset_review_for_the_types_no_collector_reads`). |
| Every CMMC row is `covers: part`, and the published observation said "a person assesses the rest" without saying what the rest is. | A partially compliant practice with no hint of what is missing. | A row's `rest:` says what the person assesses, and the observation names it. All 32 rows have one (`CmmcMappingTests.Every_row_covering_part_names_what_the_person_adds`). |

### Correct, with a limit the release notes state

- **The collectors see only what they read.** The Essential Eight rows for MFA, privileged access, patching and application control
  answer for Microsoft 365, Intune-managed Windows devices, Sophos Central firewalls and what the scanner reaches. Each row's comment
  says so. An organisation with servers, macOS or other online services must add a REST profile or a manual source for them, or the
  row overstates. The capability map (`hound onboard environment`) is where that is decided.
- **ISM-1883 is answered by counts.** Few Global Administrators, roles activated only when needed, and few Sophos Central Super
  Admins show that privileged accounts are limited; they do not show that each is required. The yearly privileged access review
  (`privileged.requests.validated`) is where a person justifies each, and an assessor will ask for it.
- **`endpoint.asr.office_rules_enabled` stands for four requirements.** When one Office rule is not blocking, all four rows are
  partial, even those whose own rule blocks. It understates, never overstates.
- **ISM-1695 uses Intune compliance.** It stands for monthly operating system patching only when the compliance policy sets a minimum
  version that tracks monthly updates; the operating system scan in the same row shows what is still missing.

The checks for MFA (all users and all applications, no exclusions but documented break-glass accounts), application control (all
devices or every listed group), unsupported software and the scan intervals (daily, weekly, fortnightly) match their requirements.

## Features a small business does not need

Ranked by how much they cost to keep against how few small businesses use them. Removals go to the maintainer in the s19-02 list;
nothing here is removed without a yes.

1. **The retired model options** (`--no-model`, `--show-requests`, the old `--config` on six commands): hidden, unused since s09.
   Remove in release 3 (engineering review E8).
2. **The step commands `hound collect`, `hound plan` and `hound publish`.** `hound run` does all three, and the agent kit uses `run`.
   They are useful for debugging a failed publish, so keep them, but take them off the cheat sheet's first page.
3. **Two onboarding entry points** (`hound onboard <system>` and `hound onboard environment`). Both are needed: the capability map
   decides what provides each mitigation, and system onboarding decides each requirement. Keep both; s19-08 puts them on one route.
4. **The older example configs** (`examples/example-pty-ltd.yaml`, `examples/example-essential-eight.yaml`): many tests read them.
   Fold into the compliance repo later (s19-D4); nothing for a user to decide.

DISP readiness, the AI register and the ASD Essentials mapping were each asked for by the maintainer and serve a defence-industry
small business; they stay.

## Features a small business needs that are missing

Ranked by what an assessor would fail first.

1. **CMMC assessment objectives.** A C3PAO assesses each of the 320 objectives in NIST SP 800-171A; `hound` maps the 110 practices.
   Until objectives are mapped, the person records each objective's answer in CISO Assistant. Release notes: known limitation.
2. **Control tests for the Essential Eight.** The ASD assessment process rates a test (try the macro, run the unapproved binary)
   above configuration. A manual source per mitigation for the test result, with its runbook, would lift the evidence. Planned work.
3. **Collectors beyond Microsoft 365 and Sophos.** Servers, macOS and other SaaS need REST profiles; the profile tooling exists but no
   profile ships for them. Release notes: known limitation.
4. **The DFARS forensic image and media preservation** (90 days after an incident report). `hound` tracks the report, not the image.
   The DFARS report pack reminds the person of both ([incidents](../incidents.md)); keeping them is the person's.

## Part B: the document generators

Verdict: **reorder and cut; move after release 3.** The documents an assessor of a small business asks for first already exist: the SSP
(s11, s17), the POA&M (s11-03) and the IRP (s15-09). Of the rest, the ones `hound`'s data largely fills and an assessor of a small
business asks for are, in order: policy statements (s19b-04), the continuous monitoring plan (s19b-03), the change and configuration
management plan (s19b-10) and the risk assessment report (s19b-05), with the catalogue (s19b-01) only once three of them exist. Cut the
security assessment report (s19b-02, the assessor writes it), the authorisation package (s19b-09, a government authorisation a small
business does not run), the business continuity summary (s19b-06, the BIA covers it) and the supplier report (s19b-07,
`supplier list` covers it). The document register (s19b-08) and registers (s19b-11) follow the catalogue. The maintainer decides; the
verdict is recorded in [s19b-security-documents.md](../sprints/s19b-security-documents.md).
