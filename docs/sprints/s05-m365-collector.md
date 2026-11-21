# s05 Microsoft 365 reference collector

Status: In progress

**Goal:** the first real collector, covering identity, privileged access, device compliance and Office hardening through Microsoft Graph, read-only.

Project: `src/Collectors/EvidenceHound.Collectors.M365`. Uses `Azure.Identity` (client credentials) and `HttpClient` against Graph v1.0 REST; no Graph SDK. All tests use recorded, fictional Graph responses for `example.com`.

Required Entra app permissions (application, admin consent, all read-only): `Policy.Read.All`, `Reports.Read.All`, `RoleManagement.Read.Directory`, `DeviceManagementConfiguration.Read.All`, `DeviceManagementManagedDevices.Read.All`, `User.Read.All`.

### s05-01 Graph access and paging
- [ ] Done

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

### s05-02 Multi-factor authentication checks
- [ ] Done

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

### s05-03 Privileged access checks
- [ ] Done

**Story:** As a compliance owner, I want administrative privilege use measured, so that admin restrictions are evidenced.

**Acceptance criteria**
- `identity.privileged.global_admin_count`: between configured min and max (default 2 to 4).
- `identity.privileged.admins_cloud_only`: privileged role holders are cloud-only accounts, not synced.
- `identity.privileged.pim_eligible_not_permanent`: privileged roles assigned as eligible rather than permanent where PIM is licensed (`NotApplicable` otherwise).

**Tests first**
- Outcome tests per check from fixtures.

**Functional check**
- As above; results appear in the example audit after `hound run` against the WireMock-backed tenant.

### s05-04 Device and Office hardening checks
- [ ] Done

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

### s05-05 Documentation
- [ ] Done

**Story:** As an operator, I want setup steps, so that I can register the Entra app with least privilege and run the collector.

**Acceptance criteria**
- `docs/collectors/m365.md`: app registration, permissions with justification, certificate setup, config example, every check ID with what it measures and its limits.

**Tests first**
- Docs test: every check ID declared in `Describe()` appears in the doc (fails if not).

**Functional check**
- A reviewer follows the doc on a test tenant and gets a successful dry run (record date, no tenant details).

## Discovered work

## Sprint review
