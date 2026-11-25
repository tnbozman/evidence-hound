# DISP membership readiness

Defence checks more than cyber security when a business applies for or keeps membership of the Defence Industry Security Program
(DISP): it looks at governance, personnel and physical security too. Evidence Hound holds those expectations as one more audit in
CISO Assistant, beside the Essential Eight, so one place shows what Defence will look at and what evidence answers it.

This is a readiness self-assessment, not the Defence Security Principles Framework (DSPF) itself. The library's requirements are short
paraphrases of the DSPF control areas, each citing its area. Read the DSPF and Defence's DISP guidance for the authoritative text and
for what your membership level asks, and check with your DISP contact.

## What it covers

| Area | Requirements | Answered by |
| --- | --- | --- |
| Governance | DISP-GOV-01 to 06: security officers, policy and plan, foreign ownership, control or influence, annual security report, supply chain, risk method | DISP manual sources; supplier records (s10); the Essentials cyber governance source for the risk method |
| Personnel security | DISP-PER-01 to 05: clearances, pre-employment screening, separation, awareness training, contact reporting | The clearances register source; the security awareness training source (shared with the ASD Essentials) |
| Physical security | DISP-PHY-01 to 03: visitors, access, facility security plan | The facility security source |
| Information security | DISP-INF-01 to 03: classification and handling, incident reporting, incident records | An incident register manual source with `incident.reported_to_asd` (the example compliance repo's `example-incident-register`); the annual security report source; handling is recorded by hand |
| Cyber security | DISP-CYB-01 to 08: one per Essential Eight strategy | Every check the Essential Eight mapping gives that strategy at maturity levels 1 and 2 |

Files: the library [`frameworks/disp.yaml`](../../frameworks/disp.yaml) and its snapshot, the mapping
[`mappings/disp.yaml`](../../mappings/disp.yaml), and the manual sources in `examples/manual-sources/disp/`
([index](../collectors/manual-templates.md#disp-membership-readiness-s14)).

## Add it to a new compliance repo

```console
$ hound init compliance --framework disp
Wrote Example Pty Ltd's compliance repo to /home/you/compliance: 15 files, audits EX-E8, EX-DISP.
```

`--framework disp` writes the library, its snapshot and mapping, declares the library in `scope/setup.yaml` (so `hound scope apply`
uploads and loads it like any custom library), writes the audit `scope/audits/<code>-disp.yaml` in the same periods as the Essential
Eight one, and adds it to `targets` in `hound.yaml`. One `hound run` then fills both audits from the same evidence.

## Add it to an existing repo

1. Copy `frameworks/disp.yaml`, `frameworks/disp.json` and `mappings/disp.yaml` into your repo.
2. Declare the library in `scope/setup.yaml`, as the example compliance repo does (the example repo has steps 1 to 3 done, and stops there):

   ```yaml
   frameworks:
     - urn: urn:evidence-hound:risk:framework:disp-membership
       library: urn:evidence-hound:risk:library:disp-membership
       file: ../frameworks/disp.yaml
       snapshot: ../frameworks/disp.json
   ```

3. Add an audit file with `framework: urn:evidence-hound:risk:framework:disp-membership` and `groups: ["M"]`
   (see `examples/compliance-repo/scope/audits/ex-disp-2026.yaml`), and a `targets` entry with `mapping: mappings/disp.yaml`.
4. Copy the manual sources you need from `examples/manual-sources/disp/` and `examples/manual-sources/essentials/security-awareness-training/`
   under `collectors:` in `hound.yaml` (a repo from `hound init` has `collectors: []`: replace it with `collectors:` first), set their
   owners, and point evidence plan rows at their runbooks.
5. `hound scope plan`, then `hound scope apply`, then `hound run`. `hound mapping coverage mappings/disp.yaml --framework frameworks/disp.json --manual hound.yaml`
   shows what each area still needs. Until the manual sources are in `hound.yaml`, coverage counts their checks as automated and warns
   if it finds no manual source at all.

## Keep personal details out

Clearance and screening records are sensitive. The runbooks ask for names, levels, dates and outcomes only: never clearance case
details, security questionnaires or identity documents. Evidence Hound stores what people send as evidence, so send summaries.
