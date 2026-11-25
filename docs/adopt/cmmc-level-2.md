# CMMC Level 2 and the SPRS score

This guide is for a small defence supplier that holds controlled unclassified information (CUI) and must self-assess against CMMC
Level 2, which is the 110 requirements of NIST SP 800-171 Rev 2, and keep its score current in the Supplier Performance Risk System
(SPRS). It uses the same collectors and evidence as the Essential Eight: no collector changes.

## 1. Add the CMMC Level 2 audit

CISO Assistant ships the CMMC 2.0 library. Declare it in `scope/setup.yaml` and add an audit that selects both groups, because the
library tags its 17 Level 1 practices `L1` only:

```yaml
frameworks:
  - urn: urn:intuitem:risk:framework:cmmc-2.0
    library: urn:intuitem:risk:library:cmmc-2.0
    snapshot: ../frameworks/cmmc-2.0.json
```

```yaml
# scope/audits/ex-cmmc-2026.yaml
ref_id: EX-CMMC-2026
name: CMMC Level 2 2026
framework: urn:intuitem:risk:framework:cmmc-2.0
groups: [L1, L2]
domain: Example Pty Ltd
perimeter: Corporate IT
```

Then add a target to `hound.yaml`, so each run fills it beside the Essential Eight audit:

```yaml
targets:
  - mapping: mappings/cmmc-2.0.yaml
    compliance_assessment: EX-CMMC-2026
```

The example compliance repo has all three: `examples/compliance-repo/`.

## 2. What runs fill, and what people assess

`mappings/cmmc-2.0.yaml` gives 32 practices the checks of the Essential Eight requirements that the
[crosswalk](../mapping-files.md#crosswalks-to-another-framework) links to them. Every row is `covers: part`: the Essential Eight is
narrower than 800-171, and the collectors see only Microsoft 365, Intune devices, Sophos Central firewalls and the vulnerability
scanner, while a CMMC practice applies to every system that holds CUI. So when every check passes, the practice is **partially
compliant**, never compliant, and its observation says a person must assess the rest
([mapping files](../mapping-files.md)). The other 78 practices are listed under `manual_only` with the record each family keeps.

Before you rely on the mapping, read each row against your CMMC scope. Remove `covers: part` from a row only when its checks answer the
whole practice for every system in that scope.

## 3. Work out the SPRS score

```console
$ hound sprs score --audit EX-CMMC-2026 --config hound.yaml
SPRS score for EX-CMMC-2026 on 2026-12-02: 71 (from 110, NIST SP 800-171 DoD Assessment Methodology 1.2.1)
Deductions (12, 39 points):
  -5  3.1.12  AC.L2-3.1.12  non_compliant
  -5  3.5.3  IA.L2-3.5.3  partially_compliant  3 points instead of 5 if MFA is implemented for remote and privileged users, ...
  ...
```

The score starts at 110 and takes off each requirement's points (5, 3 or 1) unless its row is compliant or not applicable. The weights
are in `frameworks/sprs/dod-assessment-methodology-1.2.1.yaml`, copied from the methodology's Annex A with its source cited, not in code.

- A row that is partially compliant, not assessed or missing from the audit counts as not implemented, as the methodology has no
  partial credit except two cases. For those two, 3.5.3 (MFA) and 3.13.11 (FIPS-validated cryptography), hound takes the full 5 points
  off and names the 3-point case, because only a person can say which applies.
- Where the methodology says not to subtract points because a capability is not permitted (remote access, wireless, mobile devices),
  record the practice as not applicable in CISO Assistant, with the policy that keeps it disabled.
- Without a compliant system security plan (3.12.4), the score says an assessment could not be completed.

`--format json` gives the same for an agent. `--control AC-SPRS` uploads the score as evidence on the applied control you keep for it,
so its history stays in CISO Assistant. A person reviews each deduction and enters the score in SPRS; hound never does.

## 4. Affirm each year

CMMC asks the affirming official to affirm continuing compliance every year (32 CFR 170.22). The evidence plan reminds them: a manual
row with `every: 12 months` and `first_due` set a little before the anniversary of the last assessment, so the reminder lands on time
whenever the plan is applied (a past date rolls forward a year at a time).

```yaml
# scope/evidence-plan.yaml
  - ref_id: EV-CMMC-AFFIRMATION
    control: AC-SPRS
    method: manual
    owner: Affirming official
    every: 12 months
    first_due: "2026-12-15"
    runbook: manual-sources/cmmc-affirmation.md
```

Before they affirm, give them the pack. It needs the month's posture snapshot and the CMMC plan in `scope/ssp.yaml`
([system security plan](../govern/system-security-plan.md#nist-sp-800-171-and-cmmc-level-2)):

```sh
hound report snapshot --period 2026-11 --config hound.yaml
hound sprs affirmation-pack --programme cmmc-level-2 --period 2026-11 --config hound.yaml --out affirmation-2026.md
```

The pack lists the SPRS score, every open POA&M item with its owner and ETA (marking those past their date and those CMMC does not allow
on a POA&M), each collector's freshness, and the manual sources past their due date. It decides nothing. The official affirms in SPRS
themselves; the GRC team attaches the pack and the date to the yearly task at the weekly review. Keep the pack out of the repo.
