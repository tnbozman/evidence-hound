---
name: keep-the-sprs-score
description: "Work out the NIST SP 800-171 DoD Assessment Methodology (SPRS) score from the CMMC Level 2 audit and walk the person through each deduction. Use when the person asks for their SPRS score, before they update SPRS, or when the CMMC Level 2 audit changes."
---
## Keep the SPRS score

The guide is [docs/adopt/cmmc-level-2.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/adopt/cmmc-level-2.md).

1. Work out the score: `hound sprs score --audit <cmmc audit> --config hound.yaml --format json`. It starts at 110 and takes off each
   requirement's points unless its row is compliant or not applicable; rows not assessed or missing count as not implemented.
2. If `ssp_missing` is true, say that first: without a system security plan an assessment cannot be completed. Point to the
   `build-the-ssp` skill.
3. Go through the deductions with the person, largest first. For each, say the practice, what CISO Assistant records and what would
   close it. A row from a run is at most partially compliant (`covers: part`): ask what the rest of the practice looks like before
   suggesting it is met. For 3.5.3 and 3.13.11, ask which case applies; only the person can take the 3-point case.
4. A capability that is not permitted (remote access, wireless, mobile devices) takes no points off when the practice is recorded as not
   applicable in CISO Assistant with the policy behind it. The person records it; never change a row yourself.
5. When the person agrees the score, give them the command to keep it as evidence:
   `hound sprs score --audit <cmmc audit> --config hound.yaml --control <their SPRS control>`. The person enters the score in SPRS;
   never do it for them, and never present the score as final before they have reviewed it.
