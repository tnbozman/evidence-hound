# Plan

This is the public, generic plan for the project. An adopting organisation keeps its own rollout plan (system list, owners, dates, committee) in a private repo.

## Problem

A small defence-industry business has to reach and then sustain cyber compliance, for example Essential Eight Maturity Level 2 under DISP (Australia) or NIST SP 800-171 under CMMC (US). It typically has an IT team and some software developers but no CISO or GRC team, and its compliance evidence lives in spreadsheets that go stale between audits. Whoever looks after compliance has 2 to 8 hours a week for it.

## Approach

1. **CISO Assistant is the system of record.** Self-hosted Community Edition on Docker. Frameworks (Essential Eight, NIST SP 800-171 and others) come from its built-in library.
2. **Use CISO Assistant's standard integration paths.** Spreadsheet registers move into the compliance repo's scope files, which `hound scope apply` writes to CISO Assistant over its REST API ([move off spreadsheets](adopt/migrate-from-spreadsheets.md)). Collectors send updates in the same shape as its dispatcher messages, over its REST API with a service-account token.
3. **Build a base, not a collector for everything.** The kit provides one standard way to update an audit. Each system gets a thin collector. Systems without a usable API use the manual-upload collector on a schedule.
4. **Stay framework-neutral.** Collectors emit neutral check results; one mapping file per framework turns them into requirement results.
5. **AI-assisted build, automated upkeep.** Claude Code implements sprint tasks by strict TDD inside a dev container; CI and GitHub Advanced Security close the loop. In operation, schedules, health records, backups and dependency updates keep it running with little manual effort.
6. **AI prepares, a person decides once a week.** The person's own coding agent drafts every decision, proposal and plan through `hound`, and comes as close as it can to approving everything itself; every human review is gathered into one guided weekly session (read, understand, accept or challenge, apply), which is the single place a person signs off. No model collects evidence, and no evidence is accepted without a person or a standing approval they signed ([design](design/guided-review.md)).

## Releases

| Release | Contents |
| --- | --- |
| 1 | Core, publisher, runner, collector SDK and test kit, manual-upload collector, the Microsoft 365 collector and the Sophos Central profile (Veeam was dropped in s09: backup evidence stays manual), CISO Assistant as configuration, a thin reference Docker deployment with scheduled runs (early, so evidence builds history), CISO Assistant users and settings as configuration, a tutorial for the private compliance repo, Essential Eight gap collectors (application control, vulnerability scan import, manual-upload templates), third parties as configuration, the evidence plan as recurring CISO Assistant tasks, one guided weekly review, an Essential Eight system security plan drafted from CISO Assistant records, AI governance on the Australian AI6 guidance, the completed Essential Eight mapping, a NIST SP 800-171 crosswalk, adoption guides, committee (operational) and board (governance) reporting, backups and hardening, NuGet packages, a signed release pipeline and a documentation site |
| 2 | ASD Essentials mapping, DISP membership readiness, risk, threats and BIA as configuration, incident reporting deadlines, supply-chain flow-down, CISO Assistant governance collector and metrics, CMMC Level 2 (NIST SP 800-171) mapping and its SSP and POA&M layout, SPRS score, annual affirmation and DFARS 7012 incident reporting, agent speed (one "what now" command, the week prepared in one step, skills for every feature, faster runs) |
| 3 | Security document generators for CMMC Level 2 and Essential Eight ML2 (ISM Guidelines for Cyber Security Documentation): security assessment report, continuous monitoring plan, cyber security policy and policy statements, risk assessment report, business continuity and recovery summary, supplier and supply chain report, change and configuration management plan, registers, document register with annual review, authorisation package |

Sprints are in [`docs/sprints/`](sprints/README.md).

## Adoption path for an organisation

| Step | What | Code needed |
| --- | --- | --- |
| 1. Stand up | Self-host CISO Assistant and the runner with `deploy/compose` | No |
| 2. Migrate | Move spreadsheet compliance and third-party registers into the compliance repo ([guide](adopt/migrate-from-spreadsheets.md)) | No |
| 3. Tier systems | Automate the 3 or 4 highest-value systems; put every other system on the manual-upload collector with an owner and interval | Config only |
| 4. Operate | Scheduled runs, collector health in CISO Assistant, manual uploads by owners | No |
| 5. Govern | Monthly committee report, quarterly board report, authorising-official decisions recorded in CISO Assistant | No |
| 6. Extend | Write a collector when a manual source becomes costly; add a framework mapping when a new obligation arrives | Small |

## People and governance (generic)

| Role | Responsibility |
| --- | --- |
| Authorising official | Accepts residual risk and signs off the compliance position; often the CTO when there is no CISO |
| Capability owner | Runs CISO Assistant, collectors and this kit |
| System owners | Manual evidence and remediation for their systems |
| Supplier owners | Third-party reviews and contract details |
| Independent reviewer | Periodic external assessment |

Each role has a guide in [`docs/operate/`](operate/README.md), with training in [`docs/training/`](training/README.md).

## Reporting: operation and governance

Reporting has two tiers with different audiences and questions. Both are generated from period snapshots of CISO Assistant data, so figures are reproducible and trends are reliable.

| | Security committee (operation) | Board (governance) |
| --- | --- | --- |
| Cadence | Monthly | Quarterly |
| Question answered | What changed, what is failing, what is overdue, who does what next? | Are we compliant and improving, what are the key risks, and what risk have we accepted? |
| Content | Collector health and evidence freshness; control status changes; exceptions and risk acceptances (new, expiring, expired); remediation (POA&M-style) progress; incidents; manual evidence and supplier reviews due; actions | Compliance position statement; maturity posture per framework with four-quarter trend; top risks against appetite; risk-acceptance decisions and expiries; assurance coverage; decisions needed |
| Length | As long as needed, every figure linked to CISO Assistant | Two pages plus appendix, no operational detail |
| Owner | Capability owner prepares; committee confirms actions | Authorising official presents; board endorses risk appetite and acceptances |
| Recorded in CISO Assistant | Actions as tasks and applied controls; exceptions | Risk-acceptance decisions; the report as evidence |

The committee's monthly reports roll up into the board's quarter. Delivered in sprint s12 (tasks s12-04 to s12-07); release 2 adds the Essentials view (s14) and NIST SP 800-171 SSP and POA&M views (s17).

## Security of the project itself

- Public repo with secret scanning, push protection, CodeQL, Dependabot, dependency review and OpenSSF Scorecard.
- An organisation-data deny-list check in CI blocks any adopter's real names or identifiers.
- Collectors are read-only, use least-privilege credentials resolved at runtime, and never store secrets.
- Releases are built from tags with provenance attestations and an SBOM.
