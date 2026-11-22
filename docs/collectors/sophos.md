# Sophos Central collector

The built-in `sophos` collector reads the network devices a Sophos Central tenant manages through the Sophos Central APIs, read-only, and reports framework-neutral checks on them. Today that is the firewalls and their firmware, and the admins of Sophos Central itself, who can change every one of those devices. It saves each list it read as JSON evidence, with host names, addresses, serial numbers and people's names and email addresses hashed.

It does not read endpoint protection. Evidence about endpoints, such as antivirus scanning of Office macros (ISM-1672), comes from the endpoint's own tools or a manual source, not from Sophos Central.

It signs in with a Sophos Central API credential (OAuth client credentials). It never signs in as a person, and it never writes to the tenant.

## 1. Create the API credential

In [Sophos Central](https://central.sophos.com), as a Super Admin:

1. Go to **Global Settings > API Credentials Management** (in the *Administration* section) and choose **Add Credential**.
2. Name it, for example `Evidence Hound collector`, and give it a short description such as "Read-only compliance evidence".
3. Choose the role **Service Principal Read-Only**. It can read firewalls, admins and roles, and change nothing. The collector needs no more, so do not choose *Service Principal Management*, *Forensics* or *Super Admin*.
4. Choose **Add**. Sophos Central shows the **client ID** and the **client secret**. Copy the secret now: it is shown only once. If you lose it, choose **Renew** on the credential to get a new one.
5. Note the credential's expiry date, and put a reminder in CISO Assistant (a recurring task) to renew it before it expires.

Store the client secret only as a secret on the host that runs the collector, for example as a Docker secrets file:

```console
$ install -m 600 /dev/null /srv/compliance/secrets/sophos_client_secret
$ cat > /srv/compliance/secrets/sophos_client_secret     # paste the secret, then Ctrl-D
```

Never put the secret in the compliance repo, in chat or in a ticket. The client ID is not a password, but it identifies your credential: keep it in your private deployment config only.

### Which credential

| Credential made in | Reads | `tenant_id` |
| --- | --- | --- |
| A tenant's own Sophos Central (most organisations) | That tenant | Not needed. Set it only to make sure the credential is for the tenant you expect. |
| Sophos Central Partner | Any of the partner's tenants | Required: the ID of the tenant to read. |
| Sophos Central Enterprise Dashboard (organisation) | Any tenant in the organisation | Required: the ID of the tenant to read. |

After signing in, the collector asks `whoami` which kind of credential it has and where the tenant's data lives (its data region, for example `https://api-eu01.central.sophos.com`). It only ever sends the token to `id.sophos.com`, `api.central.sophos.com` and the `*.central.sophos.com` data region host. A data region on any other host is refused with `sophos.data_region_untrusted`.

## 2. Configure the collector

```yaml
collectors:
  - name: example-sophos
    type: sophos
    schedule: "0 4 * * *"                       # daily, 04:00 in scheduler.time_zone
    settings:
      client_id: 00000000-0000-4000-8000-0000000050d1
      supported_firewall_firmware: "21.5, 22.0"
      super_admin_max: "2"
    secrets:
      client_secret: file:/run/secrets/sophos_client_secret
```

The ID above is made up. Keep your real client ID in your private deployment config, never in this repo.

| Setting | Required | Meaning |
| --- | --- | --- |
| `client_id` | yes | Client ID of the API credential. |
| `supported_firewall_firmware` | yes | Comma-separated firewall firmware versions still in support, such as `21.5, 22.0`. A firewall passes when its version is one of them or within one: `21.5.1` is within `21.5`, and `21.0.1` is within `21` but not within `21.5`. Check the Sophos Firewall OS lifecycle when Sophos announces an end of support, and update the list. |
| `super_admin_max` | no | Most Sophos Central admins allowed to hold the Super Admin role. Default `2`: one for daily work is too few to recover from a lost account, and more than two is rarely needed. |
| `tenant_id` | no | ID of the tenant to read. Required for a partner or organisation credential (see above). |
| `include_identities` | no | `true` keeps host names, firewall names, serial numbers, IP addresses and admins' names and email addresses in evidence. Default `false`: they are hashed. |
| `id_url` | no | Sophos ID address that issues tokens. Default `https://id.sophos.com/`. Change it only for tests. |
| `api_url` | no | Sophos Central global API address. Default `https://api.central.sophos.com/`. Change it only for tests. |

| Secret | Required | Meaning |
| --- | --- | --- |
| `client_secret` | yes | Client secret of the API credential. |

## 3. Dry run

```console
$ hound config validate --check-secrets hound.yaml
valid
$ hound run --config hound.yaml --only example-sophos --dry-run
```

The dry run signs in, reads the firewalls, admins and roles, stores the evidence locally and plans the updates to CISO Assistant without sending them. A first run that fails with `sophos.auth_failed` means the client ID or secret is wrong, or the credential has expired. `sophos.forbidden` means the credential's role cannot read a list; the message names it.

## Checks

| Check | Pass | Otherwise |
| --- | --- | --- |
| `network.firewall.firmware_supported` | Every firewall managed in Sophos Central runs firmware in `supported_firewall_firmware`. | `fail`, listing unsupported versions (or `unknown` when a firewall reports none). No firewalls is `not_applicable`. |
| `network.management.super_admin_count` | At most `super_admin_max` Sophos Central admins hold the predefined Super Admin role. | `fail` with the count. `error` when Sophos Central lists no predefined role named Super Admin, so nothing could be counted. |

The checks see only firewalls managed or reporting in Sophos Central. A firewall outside Sophos Central is not seen at all, so pair these checks with an asset inventory.

### What the API does not show

- **Admins' MFA.** The Common API's admin list (`common/v1/admins`) has each admin's profile and role assignments, but nothing about multi-factor authentication. Sophos Central itself requires every admin to sign in with MFA and walks them through setting it up the first time ([Sophos Central help: Multi-factor authentication](https://doc.sophos.com/central/Customer/help/en-us/ManageYourProducts/GlobalSettings/MultiFactorAuthentication/)), so there is no setting to read. ISM-1173 for Sophos Central admins rests on that, not on a check: keep a copy of the help page, or a screenshot of the sign-in prompt with no names in it, as manual evidence in CISO Assistant.
- **What a custom role can do.** The check counts the predefined Super Admin role only. A custom role with Super Admin's permissions is not counted, so review custom roles when you review admins.

### Essential Eight

In [`mappings/essential-eight-2023.yaml`](../../mappings/essential-eight-2023.yaml):

| Requirement | Checks | Why |
| --- | --- | --- |
| ISM-1501 *Operating systems that are no longer supported by vendors are replaced* (Patch operating systems) | `network.firewall.firmware_supported`, with the Microsoft 365 collector's `endpoint.os.supported_versions` | A firewall's firmware is the operating system of an internet-facing network device. Every check must pass. |
| ISM-1883 *Privileged user accounts explicitly authorised to access online services are strictly limited to only what is required* (Restrict administrative privileges) | `network.management.super_admin_count`, with the Microsoft 365 collector's `identity.privileged.global_admin_count` and `identity.privileged.pim_eligible_not_permanent` | Sophos Central is an online service, and its Super Admins can change every network device it manages. Every check must pass. |

A requirement is `not_assessed` when any of its checks has no result. If you do not run the Microsoft 365 collector, remove its checks from ISM-1501 and ISM-1883 in your own copy of the mapping, and say in each comment what covers the rest.

## Evidence

| File | Holds |
| --- | --- |
| `firewalls.json` | Every firewall (`firewall/v1/firewalls`, all pages): ID, model, firmware version, management and reporting status. |
| `admins.json` | Every Sophos Central admin (`common/v1/admins`, all pages): ID, role assignments, when they were added and changed. |
| `roles.json` | Every role (`common/v1/roles`): ID, name, type (`predefined` or `custom`) and description. Role names identify no one, so they are never hashed (`"identities": "none"`). |

The file records the request, the number of pages read and whether identities are `hashed` or `included`. Unless `include_identities` is `true`, host names, firewall names, serial numbers, IP addresses, people's names and email addresses, and the tenant's name are replaced by `sha256:` and the first 16 hex characters of a SHA-256 hash of the lower-cased value. The same device gets the same hash in every run. Firewall locations are left out. IDs are kept, so a finding can be traced back in Sophos Central.

## Problems

| Code | Meaning |
| --- | --- |
| `sophos.setting_missing`, `sophos.setting_invalid` | A setting is missing or has a value the collector cannot use. |
| `sophos.secret_missing` | `client_secret` is not set. |
| `sophos.auth_failed` | Sophos ID refused the credential: wrong client ID or secret, or an expired credential. The message gives Sophos's error code. |
| `sophos.tenant_id_required` | The credential is a partner or organisation credential, and `tenant_id` is not set. |
| `sophos.tenant_mismatch` | The credential belongs to a different tenant from `tenant_id`. |
| `sophos.data_region_untrusted` | Sophos Central named a data region that is not a Sophos Central host. The token was not sent there. |
| `sophos.unauthorized` | Sophos Central refused the token. |
| `sophos.forbidden` | The credential's role cannot read a list. Use the Service Principal Read-Only role. |
| `sophos.request_failed`, `sophos.invalid_response` | Any other error from Sophos Central, or an answer that is not JSON. |
| `sophos.unreachable` | Sophos ID or Sophos Central could not be reached (network, DNS or proxy). |
| `sophos.too_many_pages` | A list had more than 1,000 pages. |
