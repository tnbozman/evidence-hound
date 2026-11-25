---
name: keep-exceptions-and-acceptances
description: "Request, renew or close security exceptions and risk acceptances in the compliance repo, and tell the person which ones expire soon. Use when a control cannot be met for a while, when hound next or hound status lists an exception expiring or expired, or when the person asks what exceptions are open."
---
## Keep exceptions and acceptances

The guide is [docs/risk.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/risk.md#3-request-a-security-exception); the file
reference is [docs/scope.md](https://github.com/tnbozman/evidence-hound/blob/main/docs/scope.md#exceptionsyaml).

1. See what is near or past its expiry: `hound status --config hound.yaml --scope scope --json` lists them offline, and
   `hound next --repo . --config hound.yaml --json` lists each as an `exception` item. With CISO Assistant,
   `hound scope plan scope/ --config hound.yaml --json` adds what the approver decided (draft, in review, approved, rejected, expired).
2. A security exception (a known gap, such as an application that cannot use single sign-on yet) goes in `scope/exceptions.yaml`: its
   domain, severity, owners, `expiration_date`, the compensating `observation`, and the systems, controls, risk scenarios and audit
   rows (`requirements`) it concerns. Keep the expiry short enough that someone looks again: months, not years.
3. A risk the organisation accepts is a scenario in `scope/risks/` with `treatment: accept` and a justification; CISO Assistant makes
   the acceptance `ACC-<study>-<scenario>`. Rate the scenario first with the skill "keep-the-risk-register".
4. To renew one, move its `expiration_date` and say in the observation what changed since it was approved; it goes back to the
   approver. To close one whose gap is fixed, remove it from the file and say which run or document shows the fix.
5. Check and show the change: `hound scope validate scope/ --config hound.yaml --json`, then
   `hound scope plan scope/ --config hound.yaml --json`. Applying is the person's step.

## Your part and the person's

- **Never write a `status`.** Only the approver decides, in CISO Assistant (`scope.approval_not_configurable` if a status is written),
  so the person who asks is never the one who approves.
- **An exception explains a gap; it does not close it.** The row stays non-compliant and each run names the exception in its
  observation. Never tell the person a covered row is met.
- **Ask the person** who owns the gap, how long they need, and what limits the risk meanwhile. Ask once, in one short list.
