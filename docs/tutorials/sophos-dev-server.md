# The Sophos Central dev server

A fictional Example Pty Ltd Sophos Central tenant that answers the `sophos-central` profile on your machine. `make trial` uses it so the trial needs no Sophos account and no API key; you can also use it to work on the profile or its checks. Nothing leaves the machine.

```bash
make sophos-dev                                   # every control in place, on port 5370
make sophos-dev ARGS="--variant gaps"             # every deviation below
make sophos-dev ARGS="--deviation third-super-admin --port 5371"
```

Run hound where the server is on `localhost` (in a container, with `--network host`): hound sends credentials over plain http only to the machine it runs on, so an address such as `http://host.docker.internal:5370/` fails with `rest.host_not_allowed`.

The server prints the settings for a `rest` collector with the `sophos-central` profile: `client_id`, `id_url` and `api_url` (both the server's address) and `supported_firewall_firmware`. The secret `client_secret` can be any text.

## What the tenant holds

| Part | In the compliant tenant |
| --- | --- |
| Firewalls | Two XGS firewalls, `fw-office.example.com` on SFOS 21.0.1 and `fw-branch.example.com` on SFOS 21.5.0. |
| Admins | Five admins at `example.com`: two Super Admins, one Admin and two Read-only. |
| Roles | The four predefined roles: Super Admin, Admin, Read-only and Service Principal Read-Only. |

It answers every address the profile reads, with the same IDs in every answer:

| Address | Answer |
| --- | --- |
| `POST /api/v2/oauth2/token` | Always the same token. The server checks no credential and has no sign-in logic; this is the same fixed answer the profile's recorded tests use. |
| `GET /whoami/v1` | The tenant, with the server's own address as its global and data region host, so every later request comes back to it. |
| `GET /firewall/v1/firewalls`, `/common/v1/admins`, `/common/v1/roles` | The lists, in pages of two by `page` number with a page total, as Sophos Central pages them. |
| `POST /firewall/v1/firewalls/actions/firmware-upgrade-check` | The upgrades offered to the firewall the body names, as Sophos Central answers: versions and release notes, no date. |

Any other address gets 404.

## The two variants

The compliant tenant passes the three checks of the `sophos-central` profile. The `gaps` tenant applies every deviation below.

| Deviation | What changes | Check and expected outcome |
| --- | --- | --- |
| `unsupported-firmware` | The branch firewall runs SFOS 20.0.3, out of support. | `network.firewall.firmware_supported`: fail |
| `third-super-admin` | A Read-only admin is made Super Admin: three, where at most two are allowed. | `network.management.super_admin_count`: fail |
| `firmware-upgrade-overdue` | SFOS 21.5 has been offered to the office firewall for 20 days, more than the 14 allowed. | `network.firewall.firmware_current`: fail, once hound has 20 days of earlier runs |

`SophosDevTenantTests` runs the rest collector with the library profile against each variant and each deviation, so this table and the tenant cannot drift apart.

Sophos Central gives no firmware release dates, so `network.firewall.firmware_current` counts an offer's age from hound's own earlier runs. The server cannot make that history: on a first run against `firmware-upgrade-overdue` the offer is 0 days old and the check passes, saying counting starts today. The tests give the collector the evidence twenty daily runs would have kept (`SophosAnswers.EarlierUpgradeChecks`), and so does a real deployment after twenty days of collecting.

## In the trial

`make trial`, step 13, starts the server, adds the `sophos` collector pointed at it with a placeholder secret, runs it dry, publishes, and then offers to restart the server with `unsupported-firmware` and collect again, so you can watch the result change in CISO Assistant. See [the trial guide](local-trial.md#13-office-firewalls-evidence-sophos-central-a-fictional-tenant-on-this-machine-or-your-own).

## Adding a firewall, an admin or a deviation

- The tenant is described once, in `src/EvidenceHound.SophosDevServer/SophosDevTenant.cs`; `SophosAnswers.cs` turns it into Sophos Central JSON.
- A deviation is a name, a sentence, the checks it fails with their expected outcome, and a change to the tenant. Add it to `SophosDevTenant.Deviations` and a row to the table above.
- A new query in the profile adds its path to `SophosAnswers.Paths` and its items to `SophosAnswers.Items`.

## What it cannot show

- The firewall itself: the XGS firewall's own API waits on its specification (s09-15 and s09-16), so the dev server stands in for Sophos Central only.
- Sophos Central's real behaviour: rate limits, expired tokens, partner credentials and data region moves. The profile's own recorded tests and a run against a real tenant cover those.
