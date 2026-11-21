# s08 Sophos Central and Veeam reference collectors

Status: Planned

Was s07 before the release 1 reorder. It moved after the thin deployment so that Microsoft 365 evidence runs on a schedule first.

**Goal:** two more reference collectors, one SaaS (Sophos Central) and one on-premises (Veeam Backup & Replication), proving the SDK fits both shapes.

Projects: `src/Collectors/EvidenceHound.Collectors.Sophos`, `src/Collectors/EvidenceHound.Collectors.Veeam`. Recorded, fictional responses only.

### s08-01 Sophos Central access
- [ ] Done

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

### s08-02 Sophos checks
- [ ] Done

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

### s08-03 Veeam access
- [ ] Done

**Story:** As the collector, I want Veeam REST API access with a read-only account, so that backup jobs and restore points can be read.

**Acceptance criteria**
- OAuth password grant against the VBR REST API, API version header configurable.
- TLS verification on by default; a pinned certificate thumbprint option for self-signed servers instead of disabling verification.
- Paged jobs, sessions and restore points.

**Tests first**
- `VeeamAuthTests.Sends_api_version_header`
- `VeeamTlsTests.Pinned_thumbprint_accepts_matching_cert`
- `VeeamTlsTests.Mismatched_thumbprint_fails`

**Functional check**
- Run against recordings: jobs and restore points in evidence.

### s08-04 Veeam checks
- [ ] Done

**Story:** As a compliance owner, I want backup success, retention and restore testing measured, so that backup requirements are evidenced.

**Acceptance criteria**
- `backup.jobs.success_last_24h`: every enabled job succeeded in its last run within 24 hours (window configurable).
- `backup.retention.meets_policy`: restore points span at least the configured retention days.
- `backup.immutable_or_offline_copy`: at least one copy is immutable or on an offline or rotated repository (configured repository names).
- `backup.restore_tested`: a restore test session (SureBackup or instant recovery) within the configured interval; otherwise `Fail`, with the manual collector as the documented fallback.

**Tests first**
- Outcome tests per check; contract kit.

**Functional check**
- Both collectors run together in `hound run` against recordings; status shows both healthy.

### s08-05 Documentation
- [ ] Done

**Acceptance criteria**
- `docs/collectors/sophos.md` and `docs/collectors/veeam.md` as for M365, with the doc check-ID test.
- Every Sophos and Veeam check is mapped in `mappings/essential-eight-2023.yaml` (started in s06-02), and requirements they now cover are removed from `manual_only`.

**Tests first**
- Docs tests for both collectors.
- `E8MappingTests.Every_reference_check_is_mapped` extended to the Sophos and Veeam checks.

**Functional check**
- Docs build without warnings.

## Discovered work

## Sprint review
