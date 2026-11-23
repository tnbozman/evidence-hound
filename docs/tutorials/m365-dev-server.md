# The Microsoft 365 dev server

A fictional Example Pty Ltd tenant that answers Microsoft Graph on your machine, for building and testing Microsoft 365 checks without a test tenant. Nothing leaves the machine and nothing signs in.

```bash
make m365-dev                              # every control in place, on http://localhost:5365/
make m365-dev ARGS="--variant gaps"        # every deviation below
make m365-dev ARGS="--deviation synced-admin --port 5366"
```

The server prints the collector settings for the tenant (`tenant_id`, `client_id`, `break_glass_accounts`, `supported_windows_builds` and `graph_url`). Try `curl http://localhost:5365/v1.0/users` to see the first page.

## What the tenant holds

| Part | In the compliant tenant |
| --- | --- |
| People | 12 enabled members at `example.com`: two admins, two break-glass accounts and eight staff, all MFA-capable; one disabled leaver and one guest. All cloud-only. |
| Roles | The two break-glass accounts hold Global Administrator permanently; the two admins are eligible for Global Administrator through PIM, and for Exchange and Security Administrator. |
| Devices | Eight Windows laptops on 10.0.26100 or 10.0.22631, all compliant, and one iPhone. |
| Conditional Access | "USR - G - Require strong auth" (phishing-resistant MFA for all users and apps) and "USR - B - Block access via legacy auth", each excluding only the break-glass accounts. |
| Intune | "ASD Office Hardening Guidelines" (macros from the internet blocked, among 82 settings) and "ASD Windows Hardening Guidelines - Attack Surface Reduction" (19 rules in block mode), both assigned to all users; one Windows device restriction profile. |

The same IDs appear in every collection, so a role holder is a user, a device's owner is a user, and a policy's exclusions are users. Every collection the m365 collector reads is served, in pages of five with `@odata.nextLink`, as Graph pages them. A path the server does not know gets 404.

## The two variants

The compliant tenant passes all eleven m365 checks. The `gaps` tenant applies every deviation below, each expected to fail the checks it names and no other.

| Deviation | What changes | Check and expected outcome |
| --- | --- | --- |
| `staff-member-without-mfa` | One staff member has no MFA method (11 of 12). | `identity.mfa.registered_all_users`: partial |
| `mfa-policy-report-only` | "Require strong auth" left in report-only mode, as the Blueprint ships it. | `identity.mfa.enforced_conditional_access` and `identity.mfa.phishing_resistant_admins`: fail |
| `legacy-auth-excludes-a-group` | The legacy authentication block also excludes a group. | `identity.legacy_auth_blocked`: fail |
| `permanent-global-admin` | A staff member holds Global Administrator permanently (five in all). | `identity.privileged.global_admin_count` and `identity.privileged.pim_eligible_not_permanent`: fail |
| `synced-admin` | An admin account is synchronised from on-premises. | `identity.privileged.admins_cloud_only`: fail |
| `unsupported-windows-build` | One laptop runs Windows 10 22H2 (10.0.19045). | `endpoint.os.supported_versions`: fail |
| `noncompliant-laptop` | One of eight laptops is not compliant (87.5 %). | `endpoint.managed.compliant_ratio`: partial |
| `office-hardening-unassigned` | The Office hardening policy is assigned to nobody. | `apps.office.macros_blocked_from_internet`: fail |
| `asr-office-rule-in-audit` | "Block Win32 API calls from Office macros" in audit mode. | `endpoint.asr.office_rules_enabled`: partial |

`M365DevTenantTests` runs the m365 collector against each variant and each deviation, so the table and the tenant cannot drift apart.

## Adding a setting or a deviation

- The tenant is described once, in `src/EvidenceHound.M365DevServer/DevTenant.cs`; `GraphCollections.cs` turns it into Graph JSON. Add people, devices, roles or policies there.
- A deviation is a name, a sentence, the checks it fails with their expected outcome, and a change to the tenant. Add it to `DevTenant.Deviations` and a row to the table above; the tests then hold it to both.
- A new check that reads a new collection adds the collection to `GraphCollections.Paths` and `Render`, and its settings to the compliant tenant.

## Where the settings come from

The Intune and Conditional Access settings follow the [ASD Blueprint for Secure Cloud](https://github.com/ASD-Blueprint/ASD-Blueprint-for-Secure-Cloud/tree/a6e1e5f95e6051f23d2e7d1148c8fd57a6614000/static/content/files), pinned to commit `a6e1e5f`. © Commonwealth of Australia 2023, used under the [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/) licence.

- The Office hardening policy is the Blueprint's Graph export, embedded unchanged. The server adds the assignment and a description, which an export does not carry.
- The attack surface reduction rules and the two Conditional Access policies are translated by hand from the Blueprint's Microsoft365DSC configurations. The Conditional Access policies are switched on (the Blueprint ships them in report-only mode), and the Blueprint's excluded group is replaced by the two break-glass accounts, because the m365 collector only accepts excluded accounts listed in `break_glass_accounts`.

`src/EvidenceHound.M365DevServer/Blueprint/NOTICE.md` lists each file, its source and every change. The people, devices and roles are invented.

## What it cannot show

A recording of a real tenant (s09-27) and a trial against one (s09-18) still matter for:

- throttling: the server never answers 429, and real Graph paces requests differently from one tenant to the next;
- licences: what Graph answers without Entra ID P2 or Intune, beyond the cases the unit tests already stand in;
- real data: odd values, very large collections and settings nobody wrote down.

The server answers Graph only. `hound collect` against it still needs a sign-in, which the server does not answer yet, so the tests run the collector with a stand-in credential.
