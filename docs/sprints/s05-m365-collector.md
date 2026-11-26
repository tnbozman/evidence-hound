# s05 Microsoft 365 reference collector

Status: Done

**Goal:** the first real collector, covering identity, privileged access, device compliance and Office hardening through Microsoft Graph, read-only.

Project: `src/Collectors/EvidenceHound.Collectors.M365`. Uses `Azure.Identity` (client credentials) and `HttpClient` against Graph v1.0 REST; no Graph SDK. All tests use recorded, fictional Graph responses for `example.com`.

Required Entra app permissions (application, admin consent, all read-only): `Policy.Read.All`, `Reports.Read.All`, `RoleManagement.Read.Directory`, `DeviceManagementConfiguration.Read.All`, `DeviceManagementManagedDevices.Read.All`, `User.Read.All`.

### s05-01 Graph access and paging
- [x] Done

**Story:** As the collector, I want authenticated, paged, throttling-aware Graph calls, so that large tenants are collected reliably.

**Acceptance criteria**
- Token via `ClientSecretCredential` or `ClientCertificateCredential` (certificate preferred, documented).
- Follows `@odata.nextLink`; honours `Retry-After` on 429.
- Raw responses saved as evidence (JSON) with secrets and personal data minimised: user principal names hashed unless `includeIdentities: true`.

**Tests first**
- `GraphPagingTests.Follows_next_link_until_exhausted`
- `GraphPagingTests.Waits_retry_after_on_429`
- `PrivacyTests.Upns_are_hashed_by_default`

**Functional check**
- Run against the WireMock recording: three pages read, one evidence file written.
- Result (2026-11-21): against WireMock, `GraphPagingTests.Follows_next_link_until_exhausted` read three pages (three requests, five users in order) and `GraphEvidenceTests.One_file_holds_every_page_minimised` wrote one `users.json` recording `pages: 3` with every UPN hashed; 25 of 25 tests pass. [Evidence](https://github.com/tnbozman/evidence-hound/blob/d05f4d042885553cfd94a16a8189b535be63b84e/docs/evidence/s05/s05-01.md).

### s05-02 Multi-factor authentication checks
- [x] Done

**Story:** As a compliance owner, I want MFA coverage measured, so that the MFA requirements are assessed from live data.

**Acceptance criteria**
- `identity.mfa.registered_all_users`: from the authentication methods registration report; `Pass` when 100 % of enabled members are MFA-capable, `Partial` above a configurable threshold, else `Fail`; observation states counts.
- `identity.mfa.enforced_conditional_access`: a enabled Conditional Access policy requires MFA for all users with only documented exclusions (break-glass accounts named in config by object ID).
- `identity.legacy_auth_blocked`: a Conditional Access policy blocks legacy authentication clients.
- `identity.mfa.phishing_resistant_admins`: admin roles require a phishing-resistant authentication strength.

**Tests first**
- One test per outcome per check using recorded fixtures (`..._all_registered_passes`, `..._below_threshold_partial`, `..._excluded_unknown_account_fails`, etc.).

**Functional check**
- Collector run against recordings produces four check results with readable observations.
- Result (2026-11-21): the `m365` collector run against the recorded `example.com` tenant (five Graph requests, three of them user pages) reported the four checks, all `pass`, with observations such as "All 7 enabled member accounts are MFA-capable.", and wrote `users.json`, `mfa-registration.json` and `conditional-access-policies.json`. `hound collectors list` shows `m365`. [Evidence](https://github.com/tnbozman/evidence-hound/blob/d05f4d042885553cfd94a16a8189b535be63b84e/docs/evidence/s05/s05-02.md).

### s05-03 Privileged access checks
- [x] Done

**Story:** As a compliance owner, I want administrative privilege use measured, so that admin restrictions are evidenced.

**Acceptance criteria**
- `identity.privileged.global_admin_count`: between configured min and max (default 2 to 4).
- `identity.privileged.admins_cloud_only`: privileged role holders are cloud-only accounts, not synced.
- `identity.privileged.pim_eligible_not_permanent`: privileged roles assigned as eligible rather than permanent where PIM is licensed (`NotApplicable` otherwise).

**Tests first**
- Outcome tests per check from fixtures.

**Functional check**
- As above; results appear in the example audit after `hound run` against the WireMock-backed tenant.
- Result (2026-11-21): `M365RunFunctionalTests` runs `hound run` with the `m365` collector against the recorded tenant and a CISO Assistant container. EX-1.2 of `EXAMPLE-BASELINE-2026` (mapped in `tests/fixtures/m365-mapping.yaml` to the phishing-resistant and PIM checks) is `compliant` with three evidences linked. CI functional job: 8 of 8 passed. [Evidence](https://github.com/tnbozman/evidence-hound/blob/d05f4d042885553cfd94a16a8189b535be63b84e/docs/evidence/s05/s05-03.md).

### s05-04 Device and Office hardening checks
- [x] Done

**Story:** As a compliance owner, I want device compliance and Office macro settings measured, so that patching and macro requirements have evidence.

**Acceptance criteria**
- `endpoint.managed.compliant_ratio`: share of Intune-managed Windows devices compliant; thresholds configurable.
- `endpoint.os.supported_versions`: no managed device on an unsupported Windows build (list of supported builds in config).
- `apps.office.macros_blocked_from_internet`: a configuration profile or settings catalog policy blocks macros from the internet for all users.
- `endpoint.asr.office_rules_enabled`: attack surface reduction rules for Office are in block mode.

**Tests first**
- Outcome tests per check from fixtures.

**Functional check**
- Full M365 collector run against recordings: all checks reported, evidence manifest written, contract kit green.
- Result (2026-11-21): the run against the recorded tenant reported all 11 checks (all `pass`) from 11 Graph requests; through the runner's collect step it wrote nine evidence files and `_manifest.json`; `M365CollectorContractTests` passes all six contracts. [Evidence](https://github.com/tnbozman/evidence-hound/blob/d05f4d042885553cfd94a16a8189b535be63b84e/docs/evidence/s05/s05-04.md).

### s05-05 Documentation
- [x] Done

**Story:** As an operator, I want setup steps, so that I can register the Entra app with least privilege and run the collector.

**Acceptance criteria**
- `docs/collectors/m365.md`: app registration, permissions with justification, certificate setup, config example, every check ID with what it measures and its limits.

**Tests first**
- Docs test: every check ID declared in `Describe()` appears in the doc (fails if not).

**Functional check**
- A reviewer follows the doc on a test tenant and gets a successful dry run (record date, no tenant details).
- Result (2026-11-21): `DocsTests` (four tests) pass against `docs/collectors/m365.md`. **The reviewer's dry run on a test tenant is still owed**; record its date and outcome in [the evidence](https://github.com/tnbozman/evidence-hound/blob/d05f4d042885553cfd94a16a8189b535be63b84e/docs/evidence/s05/s05-05.md).

## Discovered work

- The registration report (`reports/authenticationMethods/userRegistrationDetails`) needs `AuditLog.Read.All`, not `Reports.Read.All` as the permission list above says. The collector asks for `AuditLog.Read.All`; `docs/collectors/m365.md` (s05-05) lists the permissions the collector actually uses. (Found in s05-02.)
- Settings catalog policies (Office macro and ASR settings) exist only in Graph **beta** (`beta/deviceManagement/configurationPolicies`), so that one request is beta although the plan says v1.0. Watch it for breaking changes. (Found in s05-04.)
- `$expand=settings,assignments` on settings catalog policies and `roleAssignments` without a `$filter` have only been exercised against recordings. Confirm both on a real test tenant during the s05-05 reviewer dry run. (Found in s05-04.)
- Office macro and ASR checks read Intune only. Settings applied through Group Policy, the Office Cloud Policy service or Defender for Endpoint security settings management are not seen, and exclusion groups are not subtracted from an all-users assignment. (Found in s05-04.)
- `hound collect --dry-run` against a real tenant has not been run from CI; there is no way to inject test credentials into the CLI other than the internal test seam. A recorded-tenant mode for demos may be worth adding. (Found in s05-03.)
- Conditional Access exclusions of guests or external users (`excludeGuestsOrExternalUsers`) are not read yet, so a policy that excludes them still counts as covering all users. (Found in s05-02.)

## Sprint review

**2026-11-21.** All five tasks shipped with red and green commits (refactors where there was something to tidy) and per-task evidence in `docs/evidence/s05/`. s04 had already merged, so the sprint started straight from `main`.

- **Graph access.** Client credentials through Azure.Identity (certificate preferred, secret as fallback), plain `HttpClient` against Graph, paging that only follows next links on the Graph host, `Retry-After` on 429 and 503, and every expected failure returned as a problem that names what to fix, such as the missing permission.
- **Eleven checks** across identity (MFA registration, Conditional Access MFA, legacy authentication, phishing-resistant admins), privileged access (Global Administrator count, cloud-only admins, PIM), devices (compliance ratio, supported builds) and Office (macros from the internet, ASR rules). Each is a pure function over parsed tenant data, tested from a recorded fictional `example.com` tenant.
- **Evidence** is one JSON file per Graph collection with identities hashed by default, stable across runs so unchanged data is not uploaded again.
- **Operator guide** with least-privilege permissions and certificate setup, kept honest by docs tests.

505 unit and contract tests, 38 script tests, 8 functional tests (CI) and lint pass.

What slipped: the reviewer's dry run against a real test tenant (s05-05 functional check) needs a person with a tenant; it is recorded as owed.

What the feedback taught us:
- The plan's permission list was wrong for the registration report (`AuditLog.Read.All`, not `Reports.Read.All`). Writing the permission into each Graph request, and testing that the guide lists every one, keeps the docs and the code from drifting.
- Settings catalog policies are beta-only in Graph. Reading the API surface before writing tests found this before any code depended on v1.0.
- gitleaks' generic API key rule fired on a shell line in the guide. Renaming the file in the example was enough; no allow-list entry was needed.
- Sharing one recorded tenant between the collector's tests, the contract tests and the functional test (linked source and fixtures) meant one fictional dataset to keep consistent, and edits per test instead of many near-identical fixture files.
