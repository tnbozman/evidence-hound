# s08 Sophos Central reference collector

Status: In progress

Was s07 before the release 1 reorder. It moved after the thin deployment so that Microsoft 365 evidence runs on a schedule first.

**Goal:** a second reference collector, Sophos Central, proven on a real tenant.

Veeam (s08-03 and s08-04) moved to s09 on 2026-11-22 at the person's request, so this sprint focuses on Sophos.

Also in this sprint, ready for the first real configuration: scope plans show their compliance impact (s08-06), and plans get Terraform-style exit codes and saved plan files (s08-07). The hands-on trial on a real Sophos Central tenant (s08-08), done together with a person, proves Evidence Hound works end to end outside recordings. The sprint ends with a Microsoft 365 step in the same local trial (s08-10), tried on a test tenant.

Projects: `src/Collectors/EvidenceHound.Collectors.Sophos`. Recorded, fictional responses only.

### s08-01 Sophos Central access
- [x] Done

**Story:** As the collector, I want Sophos Central authentication and tenant discovery, so that endpoint and firewall data can be read.

**Acceptance criteria**
- OAuth client credentials against Sophos ID; `whoami` determines tenant, partner or organisation; tenant ID setting required for partner and organisation credentials.
- Region-specific data API base URL from `whoami`.
- Paged endpoint and firewall listing.

**Tests first**
- `SophosAuthTests.Tenant_credential_uses_whoami_data_region`
- `SophosAuthTests.Partner_credential_without_tenant_id_fails_clearly`
- `SophosPagingTests.Reads_all_pages`

**Functional check**
- Run against recordings: endpoints and firewalls listed into evidence.
- Result (2026-11-22): `dotnet test --project tests/EvidenceHound.Collectors.Sophos.Tests` passed 21 tests against the recorded fictional tenant: the data region came from whoami, 5 endpoints were read over 2 pages and 1 firewall on 1 page. The evidence files themselves come with the collector in s08-02. Evidence: [`docs/evidence/s08/s08-01.md`](../evidence/s08/s08-01.md).

### s08-02 Sophos checks
- [x] Done

**Story:** As a compliance owner, I want endpoint protection and firewall health measured, so that those controls have live evidence.

**Acceptance criteria**
- `endpoint.protection.healthy_ratio`: share of endpoints with good health.
- `endpoint.protection.tamper_protection_enabled`: all endpoints.
- `network.firewall.firmware_supported`: firewalls on a supported firmware version (list in config).
- `endpoint.protection.stale_agents`: no endpoint not seen for more than N days.

**Tests first**
- Outcome tests per check from fixtures; contract kit.

**Functional check**
- `hound run --only sophos-example` against recordings updates the example audit.
- Result (2026-11-22): `hound run --only sophos-example --no-publish` against a local stub of the recordings succeeded with 4 checks passed and `endpoints.json` and `firewalls.json` stored (host names hashed). The publish to the example audit is the functional test `SophosRunFunctionalTests` (EX-4.1 compliant, `firewalls.json` attached), run in CI. Evidence: [`docs/evidence/s08/s08-02.md`](../evidence/s08/s08-02.md).

### s08-05 Documentation
- [x] Done

**Acceptance criteria**
- `docs/collectors/sophos.md` as for M365, with the doc check-ID test.
- Every Sophos check is mapped in `mappings/essential-eight-2023.yaml` (started in s06-02), and requirements they now cover are removed from `manual_only`.

**Tests first**
- Docs tests for the Sophos collector.
- `E8MappingTests.Every_reference_check_is_mapped` extended to the Sophos checks.

**Functional check**
- Docs build without warnings.
- Result (2026-11-22): the docs are plain Markdown with no build step, so the check is that they render and link cleanly: every relative link and heading anchor in the 99 Markdown files under `docs/`, `examples/` and `README.md` resolves (a script over the tree found none broken). `DocsTests` (Sophos) checks that `docs/collectors/sophos.md` names every check ID, and `E8MappingTests.Every_reference_check_is_mapped` covers the Sophos checks. Both are in the unit suite (954 passed).

### s08-06 Compliance impact in scope plans
- [x] Done

**Story:** As the reviewer of a compliance pull request, I want `hound scope plan` to say which framework requirements change result and why, not only which objects and fields change, so that I approve the compliance effect of a change, the way a Terraform user reads "Changes to Outputs" below the resource diff.

**Acceptance criteria**
- The plan has a **Changes to compliance** section after the change table and before Drift, for each audit whose open period has rows that change result. It is built from the framework snapshot (each requirement's title, its parent as the strategy or domain, its implementation groups) and the rows of the open period, so `hound` reads every row of each open audit, not only rows the scope declares (GET only).
- Per audit, one line per selected implementation group (for the Essential Eight, each maturity level): requirements compliant before and after, out of the in-group total, such as `Maturity Level Two: 41 → 42 compliant of 107`.
- Then each row whose result changes: requirement ref and title, strategy, old → new result, and why: a hand-written assessment in the audit file, the `unlisted` rule, or a control or document linked to the row.
- A result that goes backwards (from compliant or partially compliant to anything lower, or to not assessed) is listed first and marked `⚠`, and the summary line says how many there are. This makes visible, for example, `scope apply` resetting a result that `hound publish` wrote (s06 discovered work).
- JSON plans add `compliance[]`, each `{audit, groups[{group, name, before, after, total}], rows[{requirement, title, parent, old, new, why, regresses}]}`. `scope apply` puts the same `compliance` in its report, so the record of each apply carries the effect it had.
- Rows that collector runs own are not changed by the scope plan and are left out of this section; their impact stays with `hound plan`.
- A plan with no result changes prints no section, so an unchanged scope still plans to nothing.

**Tests first**
- `ComplianceImpactTests.Counts_compliant_per_group_before_and_after`
- `ComplianceImpactTests.Changed_row_names_title_strategy_and_reason`
- `ComplianceImpactTests.Regression_is_flagged_and_listed_first`
- `ComplianceImpactTests.Run_owned_rows_are_left_out`
- `ScopePlanCommandTests.Markdown_has_changes_to_compliance_section` and `..._json_has_compliance` (recorded responses)

**Functional check**
- Against the local CISO Assistant with the example compliance repo: change one hand-written assessment to `compliant` and set one `unlisted` row back, run `hound scope plan examples/compliance-repo/scope --config examples/compliance-repo/hound.yaml`, and record the section showing one gain and one `⚠` regression with the right ML counts.
- `docs/scope.md` Plan section shows an example of the new section.
- Result (2026-11-22): in CI against a fresh CISO Assistant seeded with the example repo (`scripts/check-scope-plans.sh`, [trial job](https://github.com/tnbozman/evidence-hound/actions/runs/44998421402/job/137134420701)), ISM-1486 set back to non-compliant and ISM-1485 added as compliant gave "**EX-E8-2026**: 2 result changes, 1 regression ⚠", ML1 1 → 1 of 48 and ML2 1 → 1 of 107, with the regression first. Evidence: [`docs/evidence/s08/s08-06.md`](../evidence/s08/s08-06.md).

### s08-07 Terraform-style exit codes and saved plans
- [x] Done

**Story:** As an operator used to Terraform, I want `hound scope plan` to report "changes pending" in its exit code and to save a plan that `hound scope apply` then applies exactly, so that pipelines can gate on a plan and what is applied is what was reviewed.

**Acceptance criteria**
- `hound scope plan --detailed-exitcode` exits 0 when the plan is empty, 2 when it has changes or drift, and 1 on any error, as Terraform does. Without the flag the exit codes stay as they are today. With the flag a usage error also exits 1, so that 2 always means changes.
- `hound scope plan --out <file>` also writes the plan to a file: the changes, the drift, the compliance impact (s08-06), the CISO Assistant URL, the date the plan was made for (`HOUND_TODAY` or today), a SHA-256 of the scope files and deployment config, and, for each object the plan changes or deletes, a fingerprint of the state it read. The file never holds the token or any document's content (only hashes), and the docs say not to commit it.
- `hound scope apply <file>` applies only the changes in that file, in order, without planning again. It refuses, writes nothing and exits 3 with `scope.plan_stale` when the scope files or config hash differently, the URL differs, or any object it would change no longer has the state it read. This matches Terraform's "Saved plan is stale". `hound scope apply <dir>` is unchanged.
- The example compliance repo's workflow plans with `--out` in the apply job and applies that file once the protected environment is approved.

**Tests first**
- `ScopePlanCommandTests.Detailed_exitcode_is_0_when_empty_and_2_with_changes`
- `ScopePlanCommandTests.Saved_plan_holds_no_token`
- `SavedPlanTests.Round_trips_changes_drift_and_fingerprints`
- `ScopePlanCommandTests.Apply_saved_plan_without_replanning` (GET calls only for the stale check)
- `ScopePlanCommandTests.Apply_refuses_a_stale_plan_when_scope_or_instance_changed` (in the plan tests, which hold the recorded CISO Assistant)

**Functional check**
- Against the local CISO Assistant: `hound scope plan examples/compliance-repo/scope --config examples/compliance-repo/hound.yaml --out plan.json --detailed-exitcode` exits 2, then `hound scope apply plan.json` writes the plan. Repeat it after editing one object in the UI between plan and apply, and record the `scope.plan_stale` refusal.
- Result (2026-11-22): in CI against a fresh CISO Assistant seeded with the example repo (`scripts/check-scope-plans.sh`, [trial job](https://github.com/tnbozman/evidence-hound/actions/runs/44998421402/job/137134420701)): `--detailed-exitcode --out` exited 2 (6 updates, 2 drifted); after an edit to the ISM-1486 row through the API, `scope apply plan.json` exited 3 with `scope.plan_stale` naming `assessment/EX-E8-2026/ISM-1486` and wrote nothing; a fresh saved plan applied with 6 written, 0 failed. Evidence: [`docs/evidence/s08/s08-07.md`](../evidence/s08/s08-07.md).

### s08-08 Hands-on trial: real Sophos Central tenant
- [ ] Done

Do this task last in the sprint: it needs s08-01, s08-02, s08-05, s08-06, s08-07 and s08-09 merged into the sprint branch. It runs through the interactive local trial (s08-09) in the person's dev container, against the CISO Assistant running there; the s07 thin deployment host is an alternative, not a prerequisite. "The host" in the steps below is whichever the person uses.

**Story:** As the maintainer, I want to run Evidence Hound for real against a home Sophos Central tenant with an XGS firewall, from configuration to results in CISO Assistant, so that I know the product works end to end and not only against recordings.

**Who does what.** Steps marked **(person)** need the person's hands: their tenant, their credentials and their host. Claude prepares commands, reads output the person pastes back, fixes defects on the sprint branch, and writes the evidence. Credentials, tenant IDs, host names, IP addresses, serial numbers and screenshots with any of those never go in this repo, in chat or in commit text.

**Steps**
1. **(person)** In Sophos Central, create a read-only API credential for the collector (service principal with the least role that reads endpoints and firewalls), following `docs/collectors/sophos.md`. Note any place the guide was wrong or unclear.
2. **(person)** On the host from the s07 thin deployment, store the client ID and secret as Docker secrets files, never in git.
3. Claude drafts, and **(person)** adds to their private compliance repo: a `sophos` collector in `hound.yaml` with `file:` secret references, the supported firmware list and the stale-agent threshold, and its mapping to the Essential Eight audit.
4. **(person)** Run `hound config validate --check-secrets` and `hound run --only <collector> --dry-run` on the host, and paste the output (with identifiers removed).
5. **(person)** Run `hound scope validate`, then `hound scope plan --out plan.json --detailed-exitcode`. Together, read the plan's change table and its **Changes to compliance** section (s08-06), and check that it shows no unexpected `⚠` regression.
6. **(person)** Run `hound scope apply plan.json`, then a real `hound run --only <collector>` (or let the s07 schedule run it), then `hound status`.
7. **(person)** In CISO Assistant, check that the Sophos evidence is attached, the mapped requirements show the results the checks gave, and a second `hound scope plan` plans to nothing (so `scope apply` does not reset what the run published).
8. Claude records any defect as a failing test and a fix on the sprint branch, or as discovered work if it is out of scope, and the person re-runs the failing step.

**Acceptance criteria**
- One real collection run against the tenant succeeds: endpoints and the firewall are read, and all four Sophos checks give an outcome (`Pass`, `Fail` or `NotApplicable`, not an error).
- The results appear on the mapped Essential Eight requirements in CISO Assistant, with the evidence attached.
- The saved plan applied without `scope.plan_stale`, and its compliance section matched what CISO Assistant showed afterwards.
- A second scope plan after the run is empty.
- Every defect found has a regression test from a new recording made with fictional data, or a discovered-work entry.
- `docs/collectors/sophos.md` is corrected wherever the person found it wrong or unclear.

**Tests first**
- None up front: this task is a manual trial. Each defect it finds starts with a failing test before its fix.

**Functional check**
- The trial itself. Record in `docs/evidence/s08/s08-08.md` only: the date, the CISO Assistant and `hound` versions, the shape of the setup ("a home Sophos Central tenant with an XGS firewall", the number of endpoints and firewalls), each step's outcome, each check's outcome, the compliance counts before and after, and the defects found with links to their fixes. No names, tenant IDs, host names, IP addresses, serial numbers, credentials or screenshots.

### s08-09 Guided setup in hound, and the local trial
- [x] Done

Added 2026-11-22 at the person's request, as the route for the s08-08 trial. Changed the same day, by the person's choice: the guided steps live in `hound` itself (wizards that scaffold a new organisation and add systems and collectors), and `make trial` only does the checks a container cannot do before it hands over to them. Build it before s08-08, so the person can start testing early.

**Story:** As the GRC lead setting up a new organisation, or a maintainer testing from my dev container with CISO Assistant already running, I want `hound` to ask me the questions and write the compliance repo files (a new organisation, a system, a manual evidence source, a collector such as Sophos), and `make trial` to run those wizards in a container next to my CISO Assistant, so that I start from valid files instead of editing YAML by hand, and can try the whole product without a server and without breaking what is already running.

**Acceptance criteria**
- **Wizards ask, options answer.** Every question has an option and a default, shown in brackets. `--yes` asks nothing: it takes the options given and the defaults. Prompts go to stderr, so output stays clean. A wizard checks its result the way `config validate` and `scope validate` do before it writes anything; a result that would not validate is not written, and the problems are reported as usual (exit 3).
- **`hound init <folder>`** scaffolds a new organisation's compliance repo: asks the organisation name, a short code, the CISO Assistant URL, the time zone, the Essential Eight maturity level to assess, the audit's due date and the owner's email. It writes `hound.yaml` (no collectors yet), `scope/setup.yaml` (the root folder, the Essential Eight framework with its snapshot, one domain and its perimeter), `scope/controls.yaml` (the health control), `scope/audits/<code>-e8-<year>.yaml` (the audit at that level, with no hand-written results yet and `unlisted: {result: not_assessed}`; `scope new-audit` lists every requirement when the team wants that), `mappings/essential-eight-2023.yaml`, `frameworks/essential-eight-2023.json`, the template's `.gitignore` and a short `README.md` with the next steps. It refuses a folder that is not empty. The result passes `config validate` and `scope validate` as written.
- **`hound add system --scope <folder>`** adds a system (name, ref ID, type, owner, description, parents) to a domain in `scope/setup.yaml` (`--domain`, asked only when there is more than one), and to the systems of the audits named with `--audit`.
- **`hound add control --scope <folder>`** declares an applied control (ref ID, name, owner, category, systems) in `scope/controls.yaml`, and with `--audit` links it to that audit's requirements (`--requirements`).
- **`hound add collector <type> --config <file>`** adds a collector of any registered type, asking for its name, schedule and each required setting from the collector's own description (`--set key=value` gives any setting, optional ones included), and a reference for each secret. With `--secrets-dir <folder>`, it reads each secret's value instead, without echo on a terminal, writes it to `<folder>/<name>_<secret>` (folder mode 700, file mode 600) and references that file; the value is never printed, logged or put on a command line. `hound add collector manual` is how a manual evidence source is added.
- **Edits keep the file.** An added item goes at the end of its list in the file's own indentation; comments and every other line are left as they were. An empty `[]` list or a missing key is handled.
- **`make trial`** (`scripts/trial.sh`, helpers in `scripts/lib/trial.sh`) keeps only what must run outside a container: Docker answers; a CISO Assistant that is already running is found and reused, never started twice; if none is running, ports 8000 and 8443 are checked and the trial stops with what holds them, otherwise it offers `make ca-up`; the runner image is built from the working tree; the API token goes into a secrets file, never on a command line. Every `hound` command then runs as `docker run --rm` on CISO Assistant's network, with the workspace `.dev/trial/` (git-ignored) mounted. It shows each command before it runs it, asks before anything that changes state, can be stopped and re-run, and skips steps already done.
- **The trial's steps** are the wizards: (1) `hound init` (Enter through the questions for Example Pty Ltd), then `scope validate`, `scope plan`, `scope apply`; (2) manual evidence and controls: `hound add system` (the backup service), `hound add control` (daily backups, linked to the backup requirements), `hound add collector manual`, `hound manual upload` of an example record with an attestation, `hound run --only <it>`, `hound status`, and where to look in the CISO Assistant UI; (3) Sophos: `hound add collector sophos --secrets-dir /trial/secrets`, `config validate --check-secrets`, a dry run, a real run and `status`. `HOUND_TRIAL_YES=1` answers every question with the defaults and stops before Sophos. `make trial-clean` removes the workspace (CISO Assistant is left running).

**Tests first**
- `YamlEditTests`: appends to a block list in its indentation, keeping comments; replaces an empty `[]`; adds a missing key; quotes values that YAML would read as something else.
- `InitCommandTests`: `--yes` writes a repo that passes `config validate` and `scope validate`; answers on stdin set the organisation, code, level and owner; a folder that is not empty is refused.
- `AddSystemCommandTests`: adds the system to the domain and the named audit, keeping the file's comments; a duplicate name is refused and nothing is written.
- `AddControlCommandTests`: declares the control and links it to the audit's requirements; a requirement the framework does not have is refused and nothing is written.
- `AddCollectorCommandTests`: asks for the required settings from the collector's description; `--set` gives an optional one; with `--secrets-dir` the secret is written mode 600, referenced as `file:` and never in the output or logs; a duplicate name is refused and nothing is written.
- `scripts/tests/trial.bats`: port check, CISO Assistant detection and network choice as before; secrets are never in a `docker run` command line.

**Functional check**
- In this repo's CI with a fresh local CISO Assistant: `HOUND_TRIAL_YES=1 make trial` runs preflight, `hound init`, applies the scope, adds a manual source, publishes its evidence and stops at the Sophos step; a second run skips the finished steps and plans no changes. Record the transcript (no tokens) in `docs/evidence/s08/s08-09.md`.
- Result (2026-11-22): in CI ([trial job](https://github.com/tnbozman/evidence-hound/actions/runs/44998421402/job/137134420701)) the first run went from preflight through `hound init`, the scope, the manual source and its published evidence, and stopped before Sophos; the second skipped every finished step and planned "0 to create, 0 to update, 0 to delete", the 8 drifted objects being the dev seed's own. Two defects found on the way were fixed test-first. Evidence: [`docs/evidence/s08/s08-09.md`](../evidence/s08/s08-09.md).
- **(person)** Runs `make trial` in their dev container up to the Sophos step; that run is the start of s08-08.

### s08-10 Microsoft 365 in the local trial
- [ ] Done

Added 2026-11-22 at the person's request: the M365 collector exists since s05, so the trial gets an M365 step after Sophos. Do it after s08-08. The person has no Microsoft 365 tenant of their own, so the route to a real one is part of the task (below).

**Story:** As the maintainer, or a GRC lead trying Evidence Hound, I want `make trial` to walk me through adding a Microsoft 365 tenant after Sophos, from the app registration to results in CISO Assistant, so that I see the M365 checks work end to end and not only against recordings.

**Which tenant.** A throwaway test tenant, never a work one: the Microsoft 365 Developer Program's E5 sandbox where the person qualifies for it, otherwise a Microsoft 365 Business Premium trial (it has Entra ID P1 and Intune, which the Conditional Access and device checks need). The tutorial says how to get each, what the checks need from it, and to cancel the trial afterwards. The collector itself is tested against the s05 recordings, as before.

**Acceptance criteria**
- **The trial's M365 step**, after Sophos: says what an app registration needs (the Graph application permissions in `docs/collectors/m365.md`, admin consent, a certificate preferred over a client secret) and how to make a test certificate locally; runs `hound add collector m365 --secrets-dir /trial/secrets` (tenant ID, client ID and supported Windows builds asked, the rest defaulted), then `config validate --check-secrets`, a dry run, a real run after asking, and `status`; and says where to look in CISO Assistant. It skips itself once done, and an unattended run (`HOUND_TRIAL_YES=1`) stops before it, as it does before Sophos.
- **A multi-line secret can be added.** `hound add collector` takes `--secret-file <secret>=<path>` and copies that file into the secrets folder (mode 600) and references it, so a PEM certificate and key never go through a prompt or a command line value. Without it, a secret is asked as today.
- **The tutorial** (`docs/tutorials/local-trial.md`) gets an M365 section like the Sophos one, and `docs/collectors/m365.md` links to it.
- Nothing from the tenant goes in the repo, in chat or in commit text: no tenant or client IDs, domains, user names, device names or screenshots with any of those.

**Tests first**
- `AddCollectorCommandTests.Secret_file_is_copied_mode_600_and_referenced`
- `AddCollectorCommandTests.M365_asks_only_its_required_settings`
- `scripts/tests/trial.bats`: the M365 step is skipped when unattended and when an m365 collector is already in `hound.yaml`.

**Functional check**
- In CI: the unattended trial still stops before Sophos and M365.
- **(person)** With a test tenant: `make trial` through the M365 step. Record in `docs/evidence/s08/s08-10.md` only the date, the versions, the kind of tenant (developer sandbox or Business Premium trial), each step's outcome, each check's outcome, and the defects found with links to their fixes.

### s08-11 Sophos collector reads network devices only
- [x] Done

Added 2026-11-22 from the s08-08 trial, at the person's request. The trial tenant manages an XGS firewall (and maybe switches) and no endpoints, so the three endpoint checks always failed with "No endpoints were found" and fed ISM-1672 (Office macro antivirus scanning), which Sophos network devices say nothing about. The person decided Sophos Central is a network device source in Evidence Hound; endpoint protection evidence comes from the endpoint's own antivirus (Microsoft Defender through the Microsoft 365 collector, later).

**Story:** As a compliance owner whose Sophos Central manages network devices, I want the Sophos collector to read only those, so that it never reports endpoint protection checks that cannot apply.

**Acceptance criteria**
- The collector reads only `firewall/v1/firewalls`; it no longer requests `endpoint/v1/endpoints` or saves `endpoints.json`.
- It describes and reports one check, `network.firewall.firmware_supported`; `endpoint.protection.healthy_ratio`, `tamper_protection_enabled` and `stale_agents` are gone, with the settings only they used (`healthy_pass_percent`, `healthy_partial_percent`, `stale_agent_days`). Configs that still set them keep loading.
- The default and example Essential Eight mappings move ISM-1672 to `manual_only`, with an antivirus and AMSI policy export as its source.
- `docs/collectors/sophos.md`, the local trial tutorial and the credential role text say the collector reads firewalls, not endpoints.

**Tests first**
- `SophosCollectorTests.Reads_only_firewalls`: one check, one evidence file, no request to the endpoints API.
- `SophosCollectorTests.Describes_its_checks_settings_and_secrets`: one check and no endpoint settings.
- `CollectorsCommandTests`: `hound collectors` lists the one Sophos check.
- `E8MappingTests`: ISM-1672 is `manual_only`.

**Functional check**
- `make trial` Sophos step against the person's tenant: one check, `firewalls.json` only, ISM-1501 still fed.
- Result (2026-11-22): rebuilt the trial image and ran `hound run --config hound.yaml --only sophos --dry-run` in the trial container against the person's tenant (a home Sophos Central tenant with one XGS firewall and no endpoints). Collector version 2.0.0, one evidence file (`firewalls.json`), one check, no problems, nothing sent. The check failed correctly: the firewall had moved from 21.5 to 22.0 firmware since the earlier run, and the trial config lists only 21.5 as supported. Unit suite 950 passed (with the dev container's `HOUND_CA_URL` unset; see discovered work), lint clean.

### s08-12 Sophos Central admins: MFA and roles
- [ ] Done

**Story:** As a compliance owner, I want the Sophos Central admins, their roles and MFA read, so that privileged access to the console that manages our network devices has live evidence.

**Acceptance criteria**
- First, confirm against the Sophos Central API documentation what a Service Principal Read-Only credential can read: the admins list and role assignments (Common API) and whether MFA enforcement for admins is exposed. Record what is and is not available in `docs/collectors/sophos.md`; a check whose data is not exposed becomes a manual source instead.
- Checks, as the API allows: every admin's sign-in requires MFA (ISM-1173); Super Admin limited to a configured maximum (ISM-1508); admins and the evidence list hashed unless `include_identities`.
- The mapping feeds the requirements with `all_pass` alongside the Microsoft 365 checks where they share a requirement.

**Tests first**
- Outcome tests per check from new fictional recordings; paging and `sophos.forbidden` for the new list.

**Functional check**
- Dry run against the person's tenant, outcomes recorded without names.

**What the API shows (checked Nov 22 against the Common API v1 spec and the Sophos Central help)**
- `GET common/v1/admins` (paged by `page`/`pageSize`/`pageTotal`) gives each admin's profile (name, email) and `roleAssignments[].roleId`. `GET common/v1/roles` gives each role's `name` and `type` (`predefined` or `custom`).
- Admins' MFA is not in the API. Sophos Central requires MFA of every admin by design ("Sophos Central admins must sign in with multi-factor authentication"), so ISM-1173 for Sophos admins is documented as manual evidence in `docs/collectors/sophos.md`, not a check.
- ISM-1508 is ML3 only (outside the ML2 audit), so the Super Admin limit maps to ISM-1883 (privileged accounts of online services strictly limited), next to the Microsoft 365 Global Administrator checks.

**Result**
- `network.management.super_admin_count`: pass when at most `super_admin_max` (default 2) admins hold the predefined Super Admin role; `error` when no such role is listed. New evidence `admins.json` (names, emails, tenant name hashed) and `roles.json`. Collector version 2.1.0.
- Functional check: waits for a dry run on the person's tenant, which also confirms the Read-Only role can read admins and roles and the predefined role's exact name.

### s08-13 Firewall firmware update timeliness
- [ ] Done

**Story:** As a compliance owner, I want to know when a firewall has had a firmware update available for longer than allowed, so that ISM-1694 (internet-facing devices patched within two weeks) has live evidence, not only "still supported".

**Acceptance criteria**
- First, confirm in the Sophos Central Firewall API how to read available firmware upgrades for a managed firewall and when each was released. If release dates are not exposed, the check reports an available upgrade without age, and the task says what evidence still has to be manual.
- `network.firewall.firmware_current`: pass when no firewall has an upgrade outstanding for more than `firmware_update_days` (default 14); fail lists the counts, not names.
- Mapped to ISM-1694 for the perimeter firewall.

**Tests first**
- Outcome tests from recordings: up to date, upgrade within the window, upgrade overdue, no firewalls (`not_applicable`).

**Functional check**
- Dry run against the person's tenant.

### s08-14 Sophos switches
- [ ] Done

**Story:** As a compliance owner with Sophos switches, I want them read like firewalls, so that non-internet-facing network devices (ISM-1501, ISM-1695) have live evidence.

**Acceptance criteria**
- First, confirm whether Sophos Central exposes managed switches and their firmware through a public API a read-only credential can use. If it does not, close the task with that finding in `docs/collectors/sophos.md` and keep switches a manual source.
- If it does: a `switches.json` evidence list and `network.switch.firmware_supported` against `supported_switch_firmware`; no switches is `not_applicable`.

**Tests first**
- Outcome tests from fictional recordings, as for firewalls.

**Functional check**
- Dry run against the person's tenant, if it has switches.

## Discovered work

- s08-06: the framework snapshot holds only assessable nodes, so the compliance section shows a requirement's strategy as its parent URN's last part (`RB`, `UAH`), not the strategy's name. Adding parent names to `hound framework snapshot` would show "Regular backups".
- The recorded tenant helpers order page files by name, so `users.2.json` sorts before `users.json` and is served as the first page. `RecordedSophos` orders by page number; `RecordedTenant` (M365) still has the old order. Its tests pass because they only count pages and items, but a page-order assertion would fail. Fix when M365 is next touched.
- The cloud build session cannot pull CISO Assistant's images (the `ghcr.io` blob host is blocked there), so functional tests against CISO Assistant run in CI only.
- s08-08 trial: `scripts/trial.sh` copies the token from the shared seed only when `.dev/ca.env` is missing, so after CISO Assistant is reseeded it keeps a stale token and `scope plan` fails with HTTP 401. Refresh from the shared file whenever it exists, or check the token in preflight and say it is stale.
- s08-08 trial: `hound init`'s default compliance owner `it@example.com` is not a user in the dev CISO Assistant (only `admin@example.com` is), so taking every default as the trial says fails `AC-AUTO-EVIDENCE` with "No user in CISO Assistant matches". Make the seed create the user, or the trial's default match the seeded one.
- s08-08 trial: every successful HTTP attempt is logged at `info` by the resilience pipeline (one line per request, hundreds per apply), burying the output. Log it at `debug`, or only retries and failures.
- s08-08 trial: the suggested answers for `hound add control` read as one answer per comma ("ISM-1511, ISM-1515" looks like two, and "Enter" sits mid-list), and the ref ID has no default, so answers shift and the wizard fails. List one answer per prompt in prompt order.
- s08-08 trial: when a later wizard in step 5 fails, re-running repeats `hound add system` and stops with `add.system_exists`, so the trial cannot recover. Give each `add` its own done marker.
- s08-08 trial: evidence is linked to requirements only, so a declared control such as AC-BACKUP shows no evidence in CISO Assistant although its requirement has some. Decide whether to also link evidence to the applied controls that cover the requirement (as the health control is), or say clearly in the trial where the evidence is.
- s08-08 trial: a requirement moved from collector checks to `manual_only` shows "the unlisted rule" as the reason in the scope plan's compliance table. Say "moved to manual" or similar.
- `PublishCommandTests.Without_a_url_exits_three` fails in the dev container, because the container sets `HOUND_CA_URL` and the test expects no address. The test should clear the variable for its own process (or the command take its environment injected), so `make test` is green in the dev container.

## Sprint review
