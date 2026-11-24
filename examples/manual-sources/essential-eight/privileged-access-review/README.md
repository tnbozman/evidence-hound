# Runbook: Privileged access review

Essential Eight: Restrict administrative privileges. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. Export the privileged access register: every privileged account, its owner, what it may administer, and when access was approved.
2. Export the access requests approved this quarter, and the review record showing accounts disabled after 12 months or 45 days unused.
3. Describe how administrators work: separate privileged environments, jump servers, and how break-glass, local administrator and service account credentials are kept unique and changed.

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every 3 months. The source is overdue 92 days after the last attested file.

## Who attests

The system owner, who approves privileged access. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source privileged-access-review <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `privileged.requests.validated` | ISM-1507 | Requests for privileged access are validated when first requested. |
| `privileged.access.revalidated_yearly` | ISM-1647 | Privileged access is disabled after 12 months unless revalidated. |
| `privileged.access.disabled_when_unused` | ISM-1648 | Privileged access is disabled after 45 days of inactivity. |
| `privileged.accounts.no_internet` | ISM-1175 | Privileged accounts cannot use the internet, email or web services. |
| `privileged.environments.separate` | ISM-1380, ISM-1687, ISM-1688, ISM-1689 | Privileged and unprivileged operating environments are kept apart. |
| `privileged.admin.jump_servers` | ISM-1387 | Administration is done through jump servers. |
| `privileged.credentials.unique` | ISM-1685 | Break-glass, local administrator and service account credentials are long, unique, unpredictable and managed. |
