# Release notes

## Release 3 (1.0.0)

Release 3 is the first published release of Evidence Hound, and the one the maintainer treats as the product. Releases 1 and 2 were
built and tested but never tagged, so release 3 carries everything from sprints s00 to s19 under version 1.0.0. The
[changelog](https://github.com/tnbozman/evidence-hound/blob/main/CHANGELOG.md) lists every feature and fix; this page says what a
person adopting it needs to know.

### What it is for

A small business with one GRC member and two to eight hours a week, reaching and keeping Essential Eight Maturity Level 2, with CMMC
Level 2, DISP readiness, the ASD Essentials and AI6 as further programmes from the same evidence. CISO Assistant is the system of
record; `hound` writes its configuration from a git repository, collects evidence on a schedule, publishes the results, and puts
everything that needs a person into one guided weekly review. Start with [getting started](getting-started.md).

### What changed in s19, the review before release

- **Four mapping claims are narrower, so results are more honest** ([CISO review](reviews/2026-ciso-review.md)):
  - A vulnerability the vendor rates critical on an internet-facing service must be patched within 48 hours, as ISM-1876 and ISM-1877
    say, not only one with a working exploit. Expect `vuln.internet_facing.patched_in_window` to fail sooner.
  - The Office attack surface reduction rules pass only when they block for all users or all devices. A policy assigned to some
    groups is partial.
  - ISM-1657 now also needs the yearly application control ruleset review (`appcontrol.rulesets_validated`), which attests compiled
    HTML, HTML applications and control panel applets.
  - Each CMMC row that covers part of its practice says what the person assesses (`rest:` in the mapping), and the observation in CISO
    Assistant names it.
- **`hound next` on a new repo** lists onboarding as the first step instead of "nothing needs doing".
- **One route through the documentation**, from the trial to the first weekly review; the onboarding tutorial moved to
  `docs/tutorials/`.
- **Simpler code behind the same behaviour**: the command-line shell, the scope pipeline and the tests were refactored with the
  output of every command unchanged ([engineering review](reviews/2026-engineering-review.md)).

### Removed, and what replaces it

| Removed | Instead |
| --- | --- |
| The Kafka transport (`transport: kafka`, `kafka_bootstrap_servers`, `HOUND_TRANSPORT=kafka`) | The REST API, the default. A config that still names Kafka is refused with `config.transport_removed`. |
| The evidence records in `docs/evidence/` and `examples/manual-exports/` | Kept in git history; the guides that cited them link to the last commit that had them. |
| The hidden options of the removed built-in model (`--no-model`, `--show-requests`, and the old `--config` on `explain`, `onboard`, `onboard environment`, `onboard profile` and `skill draft`) | Nothing: each did nothing since the model was removed. A script that still passes one now stops with a usage error; delete the option. Model help comes from your coding assistant (`hound agent setup`). |
| The onboarding assistant design (`docs/design/onboarding-assistant.md`) | [Agent-driven onboarding](design/agent-driven-onboarding.md), which replaced it in s09. |

### Known limitations

An assessor will ask for these, and `hound` does not give them:

- **CMMC assessment objectives.** `hound` assesses the 110 practices of NIST SP 800-171; a C3PAO assesses its 320 objectives
  (SP 800-171A). Record each objective's answer in CISO Assistant. Every CMMC result is at most partially compliant until a person
  does.
- **Configuration, not tests.** Collectors read how a control is configured. The ASD assessment process rates a test of the control
  (trying the macro, running the unapproved program) higher; keep those test results as manual evidence.
- **What the collectors see.** Microsoft 365, Intune-managed Windows devices, firewalls managed in Sophos Central, and what your
  vulnerability scanner reaches. Servers, macOS and other services need a REST profile or a manual source, or a row that passes
  overstates. Decide each in the capability map (`hound onboard environment`).
- **ISM-1883 by counts.** Few Global Administrators and roles activated only when needed show privileged access is limited, not that
  each account is required; the yearly privileged access review is where a person justifies each.
- **SPRS.** `hound sprs score` computes the score and `sprs affirmation-pack` prepares the affirmation; the affirming official posts
  both in SPRS.
- **DFARS incident records.** `hound incident report-pack --dfars` assembles the 72-hour report and reminds you to keep images of the
  affected systems for 90 days; keeping them is yours.

### Upgrade an existing compliance repo

For a repo made with a pre-release build:

1. Pin the new version: the runner image `ghcr.io/tnbozman/evidence-hound:1.0.0` in `deploy/deploy.env`, and the .NET tool with
   `dotnet tool update --global EvidenceHound.Cli --version 1.0.0`.
2. Remove `--no-model` and `--show-requests` from any script. Remove any `transport: kafka` or `kafka_bootstrap_servers` from `hound.yaml`, and `HOUND_TRANSPORT` from the runner's environment.
   `hound config validate hound.yaml` says what is left.
3. Copy the shipped mappings you use over your copies, or make the same changes by hand: `mappings/essential-eight-2023.yaml` (ISM-1657
   adds `appcontrol.rulesets_validated`) and `mappings/cmmc-2.0.yaml` (a `rest:` on each row). If you map ISM-1657, add the
   [application control review](collectors/manual-templates.md) manual source when you do not have it yet.
4. Run `hound scope plan scope --config hound.yaml`, then `hound run --config hound.yaml --dry-run`, and read what changes: expect the
   attack surface reduction rows to turn partial where the policy is assigned to groups, and critical internet-facing findings to fail
   after 48 hours.
5. Re-run `hound agent setup` so your agent's skills match this release.
