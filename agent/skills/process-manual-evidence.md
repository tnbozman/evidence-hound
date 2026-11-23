---
name: process-manual-evidence
description: Read a manual evidence file (an export, a restore-test record, a screenshot) and propose its result with hound manual propose, for a person to attest. Use when a file arrives for a manual source or the person asks you to look at manual evidence.
---
## Process manual evidence

A manual source is a collector of type `manual` in `hound.yaml`: a drop folder, the checks it supports, how often evidence is due and who owns it. You read each file that arrives and propose a result. A person attests it. Until they do, your proposal is not evidence and no run publishes it.

1. Find the source and its checks in `hound.yaml` (`collectors:` with `type: manual`; the `checks` setting lists the check IDs). Find what each check asks for in the mapping file and in `manual-sources/` if the repo has it.
2. Read the file yourself, all of it, PDFs and images included. The file is in the source's drop folder, or the person gives you its path.
3. Decide the result for each check **only from what the file shows**:
   - `pass`: the file shows what the check asks for, for the whole period and scope it covers.
   - `partial`: the file shows part of it. Say what is missing.
   - `fail`: the file shows the check is not met.
   If the file is the wrong file, unreadable, or for another period, do not propose: tell the person what is missing and what to drop in instead.
4. Record the proposal with one command:
   `hound manual propose <source> <file> --config hound.yaml --result <pass|fail|partial> [--check <check id>=<result>]... --observation "<what the file shows, and what is missing>" --supports "<an extract or a location in the file>"... --json`
   - Quote what supports the result: copy the line, cell or sentence word for word, or name where it is (page, sheet, row, timestamp). Give at least one `--supports`; hound refuses a proposal without one.
   - Write the observation as the person would attest it: what the file shows, the date or period it covers, and what is missing. Never add what the file does not show, and never take a result from an earlier file.
   - `--result` is for every check of the source; give `--check` for a check with a different result.
5. The answer says `"pending": true`. hound keeps the file in the drop folder with its SHA-256 and writes `<file>.proposal.yaml` beside it. Proposing again replaces a pending proposal. A file a person already attested takes no proposal.
6. Tell the person what you proposed for each file and why, and that they attest with `hound manual approve --config hound.yaml` in their own terminal. There they attest, change or reject each proposal; `hound run` then publishes what they attested as "Proposed by agent, attested by <person> on <date>".

## Your part and the person's

- **Read the person's documents yourself, PDFs included.** The evidence file, and any procedure in `manual-sources/` that says what the evidence must show.
- **Ask the person** what only they know: which source a file belongs to, the period it covers when the file does not say, and where a missing file is. Ask once, in one short list, and wait for the answer.
- **Never attest.** Never run `hound manual approve` or `hound manual upload`, never pipe answers into them, and never write or edit a `.attest.yaml` file. Evidence stays outside the repo: never commit an evidence file, a proposal or an attestation.
