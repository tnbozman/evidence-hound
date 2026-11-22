# Sophos Central collector

The built-in `sophos` collector reads a Sophos Central tenant through the Sophos Central APIs, read-only, and reports framework-neutral checks for endpoint protection and firewall firmware. It saves the endpoint and firewall lists it read as JSON evidence, with host names, people, addresses and serial numbers hashed.

It signs in with a Sophos Central API credential (OAuth client credentials). It never signs in as a person, and it never writes to the tenant.

## 1. Create the API credential

In [Sophos Central](https://central.sophos.com), as a Super Admin:

1. Go to **Global Settings > API Credentials Management** (in the *Administration* section) and choose **Add Credential**.
2. Name it, for example `Evidence Hound collector`, and give it a short description such as "Read-only compliance evidence".
3. Choose the role **Service Principal Read-Only**. It can read endpoints and firewalls and change nothing. The collector needs no more, so do not choose *Service Principal Management*, *Forensics* or *Super Admin*.
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
      stale_agent_days: "14"
    secrets:
      client_secret: file:/run/secrets/sophos_client_secret
```

The ID above is made up. Keep your real client ID in your private deployment config, never in this repo.

| Setting | Required | Meaning |
| --- | --- | --- |
| `client_id` | yes | Client ID of the API credential. |
| `supported_firewall_firmware` | yes | Comma-separated firewall firmware versions still in support, such as `21.5, 22.0`. A firewall passes when its version is one of them or within one: `21.5.1` is within `21.5`, and `21.0.1` is within `21` but not within `21.5`. Check the Sophos Firewall OS lifecycle when Sophos announces an end of support, and update the list. |
| `tenant_id` | no | ID of the tenant to read. Required for a partner or organisation credential (see above). |
| `stale_agent_days` | no | Days an endpoint may go unseen by Sophos Central before it counts as stale. Default 14. |
| `healthy_pass_percent` | no | Share of endpoints in good health needed to pass. Default 95. |
| `healthy_partial_percent` | no | Share of endpoints in good health needed for `partial`. Default 80. |
| `include_identities` | no | `true` keeps host names, firewall names, serial numbers, IP and MAC addresses and the endpoint's associated person in evidence. Default `false`: they are hashed. |
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

The dry run signs in, reads the endpoints and firewalls, stores the evidence locally and plans the updates to CISO Assistant without sending them. A first run that fails with `sophos.auth_failed` means the client ID or secret is wrong, or the credential has expired. `sophos.forbidden` means the credential's role cannot read endpoints or firewalls.

## Checks

| Check | Pass | Otherwise |
| --- | --- | --- |
| `endpoint.protection.healthy_ratio` | At least `healthy_pass_percent` of endpoints have overall health *good* (Sophos's own health: threats, services and policy). | `partial` at `healthy_partial_percent` or more, else `fail`. The observation counts the other health states. No endpoints is `fail`. |
| `endpoint.protection.tamper_protection_enabled` | Every endpoint reports tamper protection on. | `fail`, counting endpoints with it off or not reported. No endpoints is `fail`. |
| `endpoint.protection.stale_agents` | Every endpoint was seen by Sophos Central in the last `stale_agent_days` days. | `fail`, counting stale endpoints and endpoints never seen. A stale endpoint is unprotected or gone, and either needs action. No endpoints is `fail`. |
| `network.firewall.firmware_supported` | Every firewall managed in Sophos Central runs firmware in `supported_firewall_firmware`. | `fail`, listing unsupported versions (or `unknown` when a firewall reports none). No firewalls is `not_applicable`. |

The checks see only what Sophos Central manages: endpoints with a Sophos agent, and firewalls managed or reporting in Sophos Central. A device without the agent is not seen at all, so pair these checks with an asset inventory.

### Essential Eight

In [`mappings/essential-eight-2023.yaml`](../../mappings/essential-eight-2023.yaml):

| Requirement | Checks | Why |
| --- | --- | --- |
| ISM-1501 *Operating systems that are no longer supported by vendors are replaced* (Patch operating systems) | `network.firewall.firmware_supported`, with the Microsoft 365 collector's `endpoint.os.supported_versions` | A firewall's firmware is the operating system of an internet-facing network device. Every check must pass. |
| ISM-1672 *Microsoft Office macro antivirus scanning is enabled* (Restrict Microsoft Office macros) | `endpoint.protection.healthy_ratio`, `endpoint.protection.tamper_protection_enabled`, `endpoint.protection.stale_agents` | Office hands macros to the installed antivirus through AMSI. These checks show that the antivirus is installed, healthy, cannot be turned off and still reports. They do not read Office's own AMSI setting, which is on by default. |

A requirement is `not_assessed` when any of its checks has no result. If you do not run the Microsoft 365 collector, remove `endpoint.os.supported_versions` from ISM-1501 in your own copy of the mapping, and say in its comment what covers your other devices' operating systems.

## Evidence

| File | Holds |
| --- | --- |
| `endpoints.json` | Every endpoint (`endpoint/v1/endpoints`, full view, all pages): ID, type, health, operating system, tamper protection, assigned products and versions, last seen. |
| `firewalls.json` | Every firewall (`firewall/v1/firewalls`, all pages): ID, model, firmware version, management and reporting status. |

Each file records the request, the number of pages read and whether identities are `hashed` or `included`. Unless `include_identities` is `true`, host names, firewall names, serial numbers, IP and MAC addresses and the associated person's name and login are replaced by `sha256:` and the first 16 hex characters of a SHA-256 hash of the lower-cased value. The same device gets the same hash in every run. Firewall locations are left out. IDs are kept, so a finding can be traced back in Sophos Central.

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
