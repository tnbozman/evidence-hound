# Write a profile

This tutorial writes a REST profile test-first, from recorded API answers to a green `hound profile test`. It uses the fictional Example Asset API of Example Pty Ltd (`api.example.com`), whose recorded answers are in this repository, so you need no account and no network. Allow 20 minutes.

You will write `example-devices`, a profile with one check: every device runs a supported operating system. The library's [`example`](../../profiles/example.yaml) profile is the finished, larger version; the [REST collector reference](../collectors/rest.md) explains every key used here.

Run the commands from a checkout of this repository, in the dev container, after `make build`. `hound` below stands for `dotnet run --project src/EvidenceHound.Cli --`, or the `hound` binary if you have one.

## 1. Look at what the API answers

A profile starts from real answers, not from the API's documentation alone. For a real system you record them yourself, with identities removed (see the Sophos and Microsoft 365 recordings in this repository for the shape). For this tutorial they are already recorded in `profiles/example.tests/recordings`:

| File | Is the answer to |
| --- | --- |
| `token.json` | `POST https://login.example.com/oauth2/token`: an access token. |
| `whoami.json` | `GET /v1/whoami`: the organisation the API client belongs to, `org-0001`. |
| `devices.json` | `GET /v1/orgs/org-0001/devices`: the first page of devices, with a `next` link to page 2. |
| `devices.2.json` | `GET /v1/orgs/org-0001/devices?cursor=page-2`: the last page. |

Open `devices.json`. Each device has an `id`, a `hostname`, a `serial`, an `owner` and an `os.version`, and the list is under `items`. The `next` link uses `{{api}}`, which a profile test replaces with the address of the request, so the recording works wherever it is replayed.

Make a profiles folder for your work and copy the recordings into your profile's test folder:

```console
$ mkdir -p my-profiles/example-devices.tests
$ cp -R profiles/example.tests/recordings my-profiles/example-devices.tests/
```

## 2. Write the first test

A test case gives the instance's settings and secrets, the recorded answer to each request, and the result each check must give. Write the case for a tenant where every device is supported:

```yaml
# file: my-profiles/example-devices.tests/all-supported.yaml
description: Every device runs version 14 or 15, which are both supported.
now: 2026-11-22T02:00:00Z
settings:
  client_id: 00000000-0000-4000-8000-0000000000c1
  supported_os: "14, 15"
secrets:
  client_secret: example-test-client-secret
responses:
  - { post: /oauth2/token, file: recordings/token.json }
  - { get: /v1/whoami, file: recordings/whoami.json }
  - { get: /v1/orgs/org-0001/devices, file: recordings/devices.json }
  - { get: /v1/orgs/org-0001/devices, query: { cursor: page-2 }, file: recordings/devices.2.json }
expect:
  example.device.os_supported: pass
```

The secret is a made-up test value: a profile test never calls the real API.

Run it:

```console
$ hound profile test example-devices --profiles my-profiles
```

It fails with `profile.not_found`: there is no profile yet. That is the red step.

## 3. Write the profile

Write the profile, part by part:

- **Parameters and secrets** are what each organisation sets: its client ID, the versions it supports, and its client secret.
- **Connection** says where the API is, which hosts may receive the credential, and how to sign in.
- **Bootstrap** asks `whoami` for the organisation ID, which the devices address needs. It is not evidence.
- **Queries** read the devices, following the `next` link, and hash the host name, serial and owner, which identify a device or a person.
- **Checks** test every device's `os.version` against the supported list.

```yaml
# file: my-profiles/example-devices.yaml
profile: example-devices
version: "0.1.0"
description: Devices of the fictional Example Asset API, and whether they run a supported operating system.
requires:
  hound: ">=0.1"

parameters:
  client_id:
    type: string
    required: true
    description: Client ID of the read-only API client.
  base_url:
    type: url
    default: https://api.example.com/
    description: Where the API is.
  token_url:
    type: url
    default: https://login.example.com/oauth2/token
    description: Where tokens are issued.
  supported_os:
    type: list
    required: true
    description: Operating system versions still in support, for example 14, 15.

secrets:
  client_secret:
    required: true
    description: Client secret of the read-only API client.

connection:
  base_url: ${base_url}
  allowed_hosts: [api.example.com, login.example.com, "${base_url}", "${token_url}"]
  auth:
    type: oauth2_client_credentials
    token_url: ${token_url}
    client_id: ${client_id}
    client_secret: ${secrets.client_secret}
    scope: read

bootstrap:
  whoami:
    get: v1/whoami
    vars:
      org_id: $.organisation.id

queries:
  devices:
    get: v1/orgs/${org_id}/devices
    paging:
      style: next_link
      next: $.next
    items: $.items
    identities: [hostname, serial, owner]

checks:
  - id: example.device.os_supported
    title: Devices run a supported operating system
    over: devices
    each: { field: $.os.version, version_in: "${supported_os}" }
    require: all
    when_empty: not_applicable
    describe: "{passed} of {total} devices run a supported operating system. Supported: ${supported_os}."
```

Check it, then run the test again:

```console
$ hound profile validate example-devices --profiles my-profiles
$ hound profile test example-devices --profiles my-profiles
```

`validate` prints `example-devices 0.1.0: valid (1 query, 0 datasets, 1 check)`, and the test prints `example-devices/all-supported: passed (1 check)`. That is green.

## 4. Test the failure too

A check that has only been seen to pass has not been tested. Add a case where version 14 is no longer supported, so the device on 14.6 fails:

```yaml
# file: my-profiles/example-devices.tests/old-version.yaml
description: Only version 15 is supported, so the device on 14.6 fails.
now: 2026-11-22T02:00:00Z
settings:
  client_id: 00000000-0000-4000-8000-0000000000c1
  supported_os: "15"
secrets:
  client_secret: example-test-client-secret
responses:
  - { post: /oauth2/token, file: recordings/token.json }
  - { get: /v1/whoami, file: recordings/whoami.json }
  - { get: /v1/orgs/org-0001/devices, file: recordings/devices.json }
  - { get: /v1/orgs/org-0001/devices, query: { cursor: page-2 }, file: recordings/devices.2.json }
expect:
  example.device.os_supported: fail
```

```console
$ hound profile test example-devices --profiles my-profiles
```

Both cases pass. When a case fails, `hound profile test` prints the check, the result it expected, the result it got, and the observation, which names the devices that failed; a request with no recording is listed as `no recording for ...`.

A case for a check with `history` (a code check given a query's earlier evidence) lists the earlier runs it stands in for under `history`, each the evidence file an earlier run saved: `- { at: 2026-11-12T02:00:00Z, evidence: firmware_upgrades.json, file: recordings/history/firmware_upgrades-2026-11-12.json }`. The `sophos-central` tests use it for firmware age.

## 5. Use it

Copy `example-devices.yaml` and `example-devices.tests/` into your organisation's profiles folder, then add a collector for it, which asks for the client ID, the supported versions and the client secret:

```console
$ hound add collector rest --name example-devices --set profile=example-devices --config hound.yaml
```

Before relying on a new profile, run it once with `--dry-run` against the real API and read the evidence it saved: every identity should be a `sha256:` hash, and the observation should say what you expect.

From here, the [reference](../collectors/rest.md) covers more paging styles, datasets that join lists, the other operators and `require` modes, and code checks. To share a profile with others, add it to this repository's `profiles/` folder with its tests and recordings, scrubbed of anything that identifies your organisation.
