---
name: build-the-ssp
description: Build a programme's system security plan and its POA&M with hound, find the gaps, draft control descriptions for review, and prepare a signed version. Use when the person asks for the SSP, the POA&M, what is not yet met, or which requirements of another framework the evidence answers.
---
## Build the system security plan

The guide is [docs/ssp.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/ssp.md).

1. Find the programme: `hound programme list --scope scope/ --json` lists each programme's id, framework, groups, audit of record and
   requirement counts.
2. Build the plan, gaps first: `hound ssp build --programme <id> --scope scope/ --config hound.yaml --json`. Each value names its source.
   Tell the person the gaps in order: requirements with no statement, no owner, or not applicable without a justification.
3. Draft what is missing, for review: `hound ssp suggest --programme <id> --scope scope/ --config hound.yaml` lists the controls with no
   description. For each, read the control and its systems, then write a description with
   `hound ssp describe --control <ref_id> --text "<what it does and on which systems>" --scope scope/`. It is marked for review in the
   pull request; say what you based it on.
4. List what is not yet met: `hound ssp poam --programme <id> --scope scope/ --config hound.yaml --json`.
5. See how much the mapping answers: `hound mapping coverage mappings/<framework>.yaml` (by collectors, by people, or nothing), and, for
   another framework such as NIST SP 800-171, `hound mapping crosswalk --config hound.yaml` (advisory; writes nothing).
6. A version to sign: `hound ssp build --programme <id> --snapshot-out <file>` records what it was built from. Compare two versions with
   `hound ssp diff <before> <after>`. Only after the authorising officer signs does the person run
   `hound ssp publish --programme <id> --snapshot <file>`; never run it yourself.

Every figure in the plan comes from hound. Do not add, round or estimate one.
