# Runbook: Multi-factor authentication configuration

Essential Eight: Multi-factor authentication. No collector reads this, so a person exports it, an agent may propose a result with
`hound manual propose`, and the owner attests it with `hound manual approve` at a terminal. The next `hound run` publishes the
result to every requirement below. CISO Assistant emails this runbook to the owner before each due date when an evidence plan
row points at it (docs/manual-evidence.md).

## What to export

1. Export the identity provider's MFA or conditional access policies (JSON or PDF).
2. List every online service and system people sign in to, with whether it uses the organisation's identity provider, its own MFA, or none.
3. For each service that holds sensitive data or serves customers, note the MFA method (phishing-resistant or not).

Do not send passwords, keys or personal details beyond names and roles. Export settings and records, not screenshots, where the
product allows it: an export can be read again next time and compared.

## How often

Every 6 months. The source is overdue 183 days after the last attested file.

## Who attests

The identity owner, who manages the identity provider. An agent may read the export and propose a result for each check; only the owner attests it.

## Send it to the GRC team

Email the exports to the GRC team, or upload them yourself with
`hound manual upload --config hound.yaml --source mfa-configuration <file>`. The GRC team checks the file is complete, records it against
the task in CISO Assistant, and reviews the result the next run publishes.

## Checks

Attest each check as pass, partial or fail, and say why in a sentence.

| Check | Requirements | Passes when |
| --- | --- | --- |
| `identity.mfa.third_party_services` | ISM-1679, ISM-1680, ISM-1893 | Users authenticate to third-party online services with MFA. |
| `identity.mfa.organisation_services` | ISM-1892 | Users authenticate to the organisation's online customer services with MFA. |
| `identity.mfa.customers` | ISM-1681 | Customers authenticate to online customer services with MFA. |
| `identity.mfa.systems` | ISM-0974 | Unprivileged users of systems authenticate with MFA. |
| `identity.mfa.two_factors` | ISM-1401 | MFA uses something users have and something they know or are. |
| `identity.mfa.phishing_resistant` | ISM-1872, ISM-1873, ISM-1682 | MFA for online services and systems is phishing-resistant. |
