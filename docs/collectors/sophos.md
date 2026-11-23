# Sophos Central

Sophos Central is read by the `rest` collector with the `sophos-central` library profile ([`profiles/sophos-central.yaml`](../../profiles/sophos-central.yaml)). It reads the network devices a Sophos Central tenant manages through the Sophos Central APIs, read-only, and reports framework-neutral checks on them. Today that is the firewalls and their firmware, and the admins of Sophos Central itself, who can change every one of those devices. It saves each list it read as JSON evidence, with host names, addresses, serial numbers and people's names and email addresses hashed.

It does not read endpoint protection. Evidence about endpoints, such as antivirus scanning of Office macros (ISM-1672), comes from the endpoint's own tools or a manual source, not from Sophos Central.

It signs in with a Sophos Central API credential (OAuth client credentials). It never signs in as a person, and it never writes to the tenant.

Until s09 this was a built-in `sophos` collector. The profile gives the same checks and outcomes on the same recordings, so the coded collector was removed. A config that still says `type: sophos` fails validation with `config.collector_type_retired` and the replacement below: `client_id`, `supported_firewall_firmware`, `super_admin_max` and the `client_secret` secret keep their names, and `tenant_id` is no longer used.

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

Create the credential in the tenant's own Sophos Central (most organisations do). A Sophos Central Partner or Enterprise Dashboard (organisation) credential is not supported: Sophos Central gives it no data region, so both checks are `unable to assess` with `rest.value_missing` for `data_region`. Create a tenant credential instead.

After signing in, the profile asks `whoami` for the tenant's ID and where its data lives (its data region, for example `https://api-eu01.central.sophos.com`). The profile's `allowed_hosts` are Sophos ID, the global API and `*.central.sophos.com`, so the token is only ever sent there. A data region on any other host is refused with `rest.host_not_allowed`, and nothing is sent to it.

## 2. Configure the collector

Copy the profile and its tests from this repository's `profiles/` folder into the profiles folder your deployment mounts (default `/etc/evidence-hound/profiles`): `sophos-central.yaml` and `sophos-central.tests/`. Then add the collector, which asks for the profile's settings and secret:

```console
$ hound add collector rest --name example-sophos --config hound.yaml --secrets-dir /etc/evidence-hound/secrets \
    --set profile=sophos-central
```

That writes:

```yaml
collectors:
  - name: example-sophos
    type: rest
    schedule: "0 4 * * *"                       # daily, 04:00 in scheduler.time_zone
    settings:
      profile: sophos-central
      client_id: 00000000-0000-4000-8000-0000000050d1
      supported_firewall_firmware: "21.5, 22.0"
      super_admin_max: "2"
    secrets:
      client_secret: file:/run/secrets/sophos_client_secret
```

The ID above is made up. Keep your real client ID in your private deployment config, never in this repo.

| Setting | Required | Meaning |
| --- | --- | --- |
| `profile` | yes | `sophos-central`. |
| `profiles_path` | no | The profiles folder, when it is not the default. |
| `client_id` | yes | Client ID of the API credential. |
| `supported_firewall_firmware` | yes | Comma-separated firewall firmware versions still in support, such as `21.5, 22.0`. A firewall passes when its version is one of them or within one: `21.5.1` is within `21.5`, and `21.0.1` is within `21` but not within `21.5`. Check the Sophos Firewall OS lifecycle when Sophos announces an end of support, and update the list. |
| `firmware_update_days` | no | Most days a firewall may have a firmware upgrade offered before `network.firewall.firmware_current` fails. Default `14`, the two weeks ISM-1694 allows. |
| `super_admin_max` | no | Most Sophos Central admins allowed to hold the Super Admin role. Default `2`: one for daily work is too few to recover from a lost account, and more than two is rarely needed. |
| `include_identities` | no | `true` keeps host names, firewall names, serial numbers, IP addresses and admins' names and email addresses in evidence. Default `false`: they are hashed. |
| `id_url` | no | Sophos ID address that issues tokens. Default `https://id.sophos.com/`. Change it only for tests. |
| `api_url` | no | Sophos Central global API address. Default `https://api.central.sophos.com/`. Change it only for tests. |

| Secret | Required | Meaning |
| --- | --- | --- |
| `client_secret` | yes | Client secret of the API credential. |

To check your copy of the profile offline against its recordings, run `hound profile test sophos-central`. After an update to this repository, `hound profile diff sophos-central --library <checkout>` shows how your copy differs from the library version.

## 3. Dry run

```console
$ hound config validate --check-secrets hound.yaml
valid
$ hound run --config hound.yaml --only example-sophos --dry-run
```

The dry run signs in, reads the firewalls, admins and roles, stores the evidence locally and plans the updates to CISO Assistant without sending them. A first run that fails with `rest.auth_failed` means the client ID or secret is wrong, or the credential has expired. A list the credential's role cannot read makes the checks over it `unable to assess`, and the observation gives Sophos Central's answer.

## Checks

| Check | Pass | Otherwise |
| --- | --- | --- |
| `network.firewall.firmware_supported` | Every firewall managed in Sophos Central runs firmware in `supported_firewall_firmware`. | `fail`, listing the firewalls (by ID) whose version is unsupported or missing. No firewalls is `not applicable`. |
| `network.firewall.firmware_current` | No firewall has had a firmware upgrade offered for more than `firmware_update_days` days (the `sophos.firmware_upgrade_age` code check over the firmware upgrade check, `firewall/v1/firewalls/actions/firmware-upgrade-check`, one call per firewall). | `fail` with the counts and the longest age. `unable to assess` when the upgrade check is refused or does not answer for a firewall. No firewalls is `not applicable`. |
| `network.management.super_admin_count` | At most `super_admin_max` Sophos Central admins hold the predefined Super Admin role (the `sophos.super_admin_count` code check). | `fail` with the count. `unable to assess` when Sophos Central lists no predefined role named Super Admin, so nothing could be counted. |

The checks see only firewalls managed or reporting in Sophos Central. A firewall outside Sophos Central is not seen at all, so pair these checks with an asset inventory.

### What the API does not show

- **Admins' MFA.** The Common API's admin list (`common/v1/admins`) has each admin's profile and role assignments, but nothing about multi-factor authentication. Sophos Central itself requires every admin to sign in with MFA and walks them through setting it up the first time ([Sophos Central help: Multi-factor authentication](https://doc.sophos.com/central/Customer/help/en-us/ManageYourProducts/GlobalSettings/MultiFactorAuthentication/)), so there is no setting to read. ISM-1173 for Sophos Central admins rests on that, not on a check: keep a copy of the help page, or a screenshot of the sign-in prompt with no names in it, as manual evidence in CISO Assistant.
- **When firmware was released.** The firmware upgrade check lists the upgrades offered to each firewall with their size, fixed bugs and news, but no release date. hound counts an offer's age from its own earlier runs: the check is given the upgrade check's answers from every earlier run the evidence store still keeps (`history: firmware_upgrades` in the profile) and counts from the first run of the unbroken series that saw an offer. Counting starts with the first run, so for the first `firmware_update_days` days after you start collecting the check cannot fail, and retention (`evidence.retention_days`) must be longer than `firmware_update_days`. The 48-hour window for exploited vulnerabilities (ISM-1877) is not judged: Sophos does not say which upgrades fix an exploited vulnerability.
- **Whether the read-only role may call the upgrade check.** It is a POST that changes nothing. If your credential's role is refused, the check is unable to assess; the other checks are not affected.
- **What a custom role can do.** The check counts the predefined Super Admin role only. A custom role with Super Admin's permissions is not counted, so review custom roles when you review admins.

### Essential Eight

In [`mappings/essential-eight-2023.yaml`](../../mappings/essential-eight-2023.yaml):

| Requirement | Checks | Why |
| --- | --- | --- |
| ISM-1501 *Operating systems that are no longer supported by vendors are replaced* (Patch operating systems) | `network.firewall.firmware_supported`, with the Microsoft 365 collector's `endpoint.os.supported_versions` | A firewall's firmware is the operating system of an internet-facing network device. Every check must pass. |
| ISM-1694 *Patches, updates or other vendor mitigations for vulnerabilities in operating systems of internet-facing servers and internet-facing network devices are applied within two weeks of release* (Patch operating systems) | `network.firewall.firmware_current` | For the firewalls Sophos Central manages. Internet-facing servers, and fixes for exploited vulnerabilities within 48 hours, need the scanner and patch records as well. |
| ISM-1883 *Privileged user accounts explicitly authorised to access online services are strictly limited to only what is required* (Restrict administrative privileges) | `network.management.super_admin_count`, with the Microsoft 365 collector's `identity.privileged.global_admin_count` and `identity.privileged.pim_eligible_not_permanent` | Sophos Central is an online service, and its Super Admins can change every network device it manages. Every check must pass. |

A requirement is `not_assessed` when any of its checks has no result. If you do not run the Microsoft 365 collector, remove its checks from ISM-1501 and ISM-1883 in your own copy of the mapping, and say in each comment what covers the rest.

## Evidence

| File | Holds |
| --- | --- |
| `firewalls.json` | Every firewall (`firewall/v1/firewalls`, all pages): ID, model, firmware version, management and reporting status. |
| `firmware_upgrades.json` | The firmware upgrade check for each firewall (`firewall/v1/firewalls/actions/firmware-upgrade-check`): its firmware and the upgrades offered. |
| `firmware_upgrades.history.json` | The same answers from every earlier run the evidence store keeps, with when each was collected: what the firmware age was counted from. |
| `admins.json` | Every Sophos Central admin (`common/v1/admins`, all pages): ID, role assignments, when they were added and changed. |
| `roles.json` | Every role (`common/v1/roles`, all pages): ID, name, type (`predefined` or `custom`) and description. Role names identify no one, so they are never hashed. |

Each file records the request, the pages read and the SHA-256 of what was read; the run's `_manifest.json` names the profile and its version. Unless `include_identities` is `true`, host names, firewall names, serial numbers, IP addresses, firewall locations and people's names and email addresses are replaced by `sha256:` and the first 16 hex characters of a SHA-256 hash. The same device gets the same hash in every run. IDs are kept, so a finding can be traced back in Sophos Central.

## Problems

| Code | Meaning |
| --- | --- |
| `config.collector_type_retired` | The config still says `type: sophos`. Use `type: rest` with `profile: sophos-central`, as above. |
| `profile.parameter_missing`, `profile.parameter_invalid` | A setting the profile needs is missing or has a value it cannot use. |
| `profile.secret_missing` | `client_secret` is not set. |
| `profile.not_found` | `sophos-central.yaml` is not in the profiles folder. |
| `rest.auth_failed` | Sophos ID refused the credential: wrong client ID or secret, or an expired credential. |
| `rest.value_missing` | `whoami` gave no tenant ID or data region: the credential is a partner or organisation credential. |
| `rest.host_not_allowed` | Sophos Central named a data region that is not a Sophos Central host. Nothing was sent there. |
| `rest.unreachable` | Sophos ID or Sophos Central could not be reached (network, DNS or proxy). |
