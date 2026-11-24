# s18 Collector console (optional)

Status: Planned

Release: 2 (optional)

Was s15 before the release 1 reorder.

**Goal:** a small web console to register collectors and see collector and CISO Assistant integration health, built on the run status data from s03. CISO Assistant remains the place for compliance, evidence, risks and third parties.

Stack: ASP.NET Core minimal API in `src/EvidenceHound.Console.Api`, Vue 3 + Vite + TypeScript in `web/console`, EF Core with SQLite (Postgres option), OpenID Connect sign-in.

### s18-01 Console API (read-only)
- [ ] Done

**Acceptance criteria**
- Endpoints: list collector instances with latest run; run history per instance; integration health (CISO Assistant reachable, token expiry, recent rejected messages); manual sources due and overdue.
- OIDC authentication; roles viewer, operator, admin; every request authorised; audit log of actions.
- No evidence content is ever returned.

**Tests first**
- `ConsoleApiTests.Unauthenticated_request_is_401`
- `ConsoleApiTests.Viewer_can_read_status`
- `ConsoleApiTests.Evidence_content_is_never_returned`

**Functional check**
- `curl` with a test token returns the example deployment's status.

### s18-02 Status pages
- [ ] Done

**Acceptance criteria**
- Vue 3 pages: overview (traffic light per collector and the integration), collector detail (runs, failures, checks), manual sources due.
- Accessible (axe checks), works at phone width.

**Tests first**
- Vitest component tests; Playwright end-to-end smoke against the API with fixtures.

**Functional check**
- Open the console in the dev container's forwarded port and see the example deployment.

### s18-03 Registration and controls
- [ ] Done

**Acceptance criteria**
- Operators add, edit, enable and disable collector instances, run now and dry run; settings validated against each collector's `Describe()`; secrets entered only as references.
- Changes written to the registry and picked up by the scheduler without restart.

**Tests first**
- `RegistryTests.Inline_secret_is_rejected`
- `SchedulerTests.Picks_up_new_instance`
- Playwright: register a fake collector and run it.

**Functional check**
- Register the manual collector from the UI and see its first run.

## Discovered work

## Sprint review
