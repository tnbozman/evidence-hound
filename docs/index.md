# Evidence Hound

*Sniffs out compliance evidence from your systems and fetches it into CISO Assistant, on schedule, every time.*

Small businesses in the defence industry must reach and then keep cyber compliance, such as Essential Eight Maturity Level 2 (DISP,
Australia) or NIST SP 800-171 (CMMC, US), usually without a CISO or a GRC team, and with a few hours a week for it. Evidence Hound
makes that manageable with open-source tools:

- **[CISO Assistant](https://github.com/intuitem/ciso-assistant-community)**, self-hosted, is the system of record: audits, controls,
  evidence, suppliers, risks and tasks.
- **`hound`** collects evidence from your systems on a schedule, maps it to each framework's requirements and keeps CISO Assistant's
  audits current, so compliance is continuous rather than a scramble before each assessment.
- **Your own coding agent** (Claude Code or GitHub Copilot) drafts the decisions, supplier reviews and evidence proposals; you accept
  or challenge them in **one guided review a week**. No model collects evidence, and nothing is accepted without a person.

## Where to start

New here? [Getting started](getting-started.md) is one route from a trial on your laptop to your first weekly review. The rest of
the site is reference that route links to.

| You want to | Read |
| --- | --- |
| See it work on your laptop in an hour or two | [Getting started](getting-started.md), then the [local trial](tutorials/local-trial.md) |
| Run it for real next to CISO Assistant | [First deployment](tutorials/first-deployment.md), then [backups](deploy/backups.md) and [hardening](deploy/hardening.md) |
| Move your registers in | [Onboarding](onboarding.md) and [moving off spreadsheets](adopt/migrate-from-spreadsheets.md) |
| Know what to do each week as the GRC member | [GRC member guide](operate/grc-member.md) and [the weekly review](manual-evidence.md#the-weekly-review) |
| Report to the committee and the board | [Reporting](reporting.md), [security committee](govern/security-committee.md), [board](govern/board-reporting.md) |
| Answer a prime's NIST SP 800-171 questions | [Answer a prime's questionnaire](adopt/answer-a-prime-questionnaire.md) |
| Govern your use of AI | [AI governance primer](ai-grc-primer.md) |
| Connect a system | [Collectors](collectors/README.md), or [write a profile](tutorials/write-a-profile.md) for any REST API |

Every command and its options are on the [cheat sheet](cheatsheet.md). What is in this release, its known limitations and how to
upgrade are in the [release notes](release-notes.md).

## How it fits together

```text
 your systems ──> collectors ──> check results ──> framework mappings ──> CISO Assistant audits
 (Microsoft 365,   (scheduled,     (framework-       (Essential Eight,      (results, evidence,
  Sophos, scans,    read-only)      neutral)          AI6, crosswalks)        controls, tasks)
  manual uploads)                                                                  │
                                                                                   v
                       you, once a week: hound review  <── your agent drafts   committee and board reports
```

Your organisation's configuration (systems, tenants, audits, evidence) lives in your own private compliance repo, never in this
project. Evidence Hound is Apache-2.0; CISO Assistant is AGPL-3.0 and is used over its API only.
