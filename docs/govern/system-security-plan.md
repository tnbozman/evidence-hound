# Your system security plan

A system security plan (SSP) says what a system is, where its boundary is, who is responsible for it, and how each control the framework asks for is met. Assessors read it first. ISM-0041 asks for one for each system; CMMC Level 2 asks for one too (NIST SP 800-171 3.12.4).

hound builds most of the plan from what you already keep up to date: the records in CISO Assistant and the scope files in your repo. People write the parts that records cannot hold, and the authorising official signs each version. This guide walks through the whole cycle with the fictional example in `examples/compliance-repo`. The reference for every command and column is [ssp.md](../ssp.md).

## What the plan is built from

| Part of the plan | Comes from |
| --- | --- |
| System name, version, classification, roles, documents | `scope/ssp.yaml` |
| Boundary | The perimeter `ssp.yaml` names, and the systems in it (CISO Assistant assets) |
| Inventory and owners | CISO Assistant assets |
| External services and suppliers | CISO Assistant third parties (solutions and their entities) |
| Each requirement's status | The audit of record's results in CISO Assistant |
| Implementation statements | The descriptions of the applied controls linked to each requirement, then the row's observation |
| Responsible party | The owners of those controls |
| Annex (section 13) | The framework, in the ASD SSP annex template's columns, one row per control |
| POA&M appendix | Every requirement not yet met, with its control, owner and ETA |

Every value carries its source (`hound ssp build --json`), so a reviewer can check where it came from. The layout is NIST SP 800-18 Rev 1, Appendix A, for every framework; for the Essential Eight (`profile: ism`) the annex lists ISM controls by strategy.

## What people write or draw

hound cannot generate these. They need someone who knows the business:

- **Sections** of the plan, as Markdown files in your repo named under `sections:` in `ssp.yaml`: the system's purpose, its environment, how it is managed, and the laws and policies that apply. The example's are in `examples/compliance-repo/ssp/`.
- **Boundary and network diagrams, and data flows.** Draw them yourself. A Mermaid diagram in the environment section, like the example's, stays readable as text and shows in GitHub; a picture exported from a drawing tool works too.
- **Policies and procedures.** Name them under `documents:`; they are the documents your audits already list.
- **Physical and personnel arrangements**: offices, access, vetting and training, in the sections where they apply.
- **Signatures.** Only the authorising official approves a version, and does so in `ssp.yaml` by pull request (below).

A section file you have not written yet prints as "not written yet", so you can build a draft before everything is done.

## Build a draft

```sh
hound ssp build --programme essential-eight --strict
```

The draft says DRAFT in its title, in a banner and at the end. `--strict` exits non-zero while any gap remains: an implemented requirement with no statement, a requirement with nobody responsible, or one marked not applicable without a reason. The gaps are listed first. Close them in CISO Assistant or the scope, then build again.

`--format docx --out ssp-essential-eight-0.1.docx` also writes a Word file; every page of a draft carries a DRAFT header.

## Drafting descriptions with your own agent

When many controls have no description, your own coding agent can draft them. hound has no model; it only gives your agent the facts and writes its text where you review it:

```sh
hound ssp suggest --programme essential-eight            # JSON brief: controls to describe, and what they are for
hound ssp describe --control AC-BACKUP --text "..."      # writes the draft to scope/controls.yaml, marked "drafted with AI"
```

Read every drafted description in the pull request: accept it, edit it or delete it. Once merged, `hound scope apply` sends it to CISO Assistant, and the next build uses it.

## Review and approval

1. Build the draft with its snapshot, the record of exactly what it was built from:

   ```sh
   hound ssp build --programme essential-eight --snapshot-out ssp-essential-eight-1.0.json > ssp-essential-eight-1.0.md
   ```

   The log prints the draft's SHA-256 and the snapshot's. Keep both files out of the repo: the example's `.gitignore` ignores `ssp-*`.
2. The reviewers (the system owner and security contact) read the draft and fix what is wrong at its source, then rebuild.
3. The authorising official approves by adding a sign-off under the plan in `scope/ssp.yaml`, in a pull request:

   ```yaml
   sign_offs:
     - {version: "1.0", by: ceo@example.com, date: "2026-12-01", sha256: <draft SHA-256>, snapshot: <snapshot SHA-256>}
   ```

   `hound scope validate` checks that `by` is the authorising official and that the hashes look right.
4. Rebuild the signed plan from the snapshot. It must give the same draft, or hound refuses:

   ```sh
   hound ssp build --programme essential-eight --from-snapshot ssp-essential-eight-1.0.json
   ```

5. Publish it to CISO Assistant, the system of record:

   ```sh
   hound ssp publish --programme essential-eight --snapshot ssp-essential-eight-1.0.json
   ```

   This uploads one zip (the signed plan, its POA&M, the snapshot and CISO Assistant's own Word audit report) as a new revision of the evidence `SSP-ESSENTIAL-EIGHT`. An unsigned version is refused.

## Versions

Change `version:` in `ssp.yaml` for each new version: a minor version (1.1) for updated statuses and statements, a major version (2.0) when the boundary or the inventory changes. To see what a reviewer needs to read:

```sh
hound ssp diff ssp-essential-eight-1.0.json ssp-essential-eight-1.1.json
```

The diff lists boundary and inventory changes first, then suppliers, statuses, statements and sections. Review the plan at least once a year and whenever the boundary, a system or a supplier changes.
