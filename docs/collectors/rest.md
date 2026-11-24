# REST collector and profiles

The built-in `rest` collector reads any HTTP API described by a **profile**: a YAML file that says how to sign in, which lists to read, and which checks to run over them. A new system, or a new check on a system, is a profile change, not a code change or a release. Sophos Central is read this way ([`profiles/sophos-central.yaml`](../../profiles/sophos-central.yaml), [guide](sophos.md)), infrastructure drift from an IaC pipeline's plan artefact ([`profiles/terraform-drift.yaml`](../../profiles/terraform-drift.yaml), [guide](terraform-drift.md)), hosting controls from the pipeline's infrastructure tests ([`profiles/terraform-tests.yaml`](../../profiles/terraform-tests.yaml), [guide](terraform-tests.md)), and CISO Assistant's own AI risks and supplier records ([`profiles/ciso-assistant.yaml`](../../profiles/ciso-assistant.yaml), [guide](ciso-assistant.md)).

Each run has three phases, and each is kept apart from the others:

1. **Collect.** Sign in, run the `bootstrap` queries (such as `whoami`), then read each query once, following its pages. Each query is saved as one evidence file, `<query>.json`, with identities hashed.
2. **Extract.** Build the `datasets`: join, flatten, filter and pick fields from the queries' items. Each dataset is saved as `dataset.<name>.json`, naming the query files it was built from and their SHA-256.
3. **Assess.** Run each check over a query or a dataset. A check's result names the evidence it read.

Nothing in a run calls a language model. The profile is the whole definition; the engine only follows it.

To write your first profile, follow the tutorial [Write a profile](../tutorials/write-a-profile.md). The design and the decisions behind it are in [the design note](../design/rest-collector.md).

## The profiles folder and the library

`hound` has no profiles built in. Every profile loads from the organisation's **profiles folder**, by default `/config/profiles`, mounted read-only into the runner. Keep the folder in your private compliance repo, next to `hound.yaml`. Each profile is `<name>.yaml`, and its tests are in `<name>.tests/` beside it.

This repository keeps a public **library** of tested profiles in [`profiles/`](../../profiles): `example` (the fictional Example Asset API) and `sophos-central`. To use one, copy the profile and its tests into your profiles folder:

```console
$ cp -R evidence-hound/profiles/sophos-central.yaml evidence-hound/profiles/sophos-central.tests /srv/compliance/profiles/
$ hound profile validate sophos-central --profiles /srv/compliance/profiles
$ hound profile test sophos-central --profiles /srv/compliance/profiles
```

A copied profile keeps its `source` and `version`. After you update your checkout of this repository, `hound profile diff sophos-central --library <checkout>` shows how your copy differs from the library version, so you can take the library's fixes without losing your own changes. Set `HOUND_PROFILES_PATH` and `HOUND_LIBRARY_PATH` to skip the options.

| Command | Does |
| --- | --- |
| `hound profile validate <name>` | Parses and checks the profile, listing every problem with its code. |
| `hound profile test <name>` | Runs each test case in `<name>.tests/` offline, against its recorded answers, and compares each check's result with the expected one. Exits 1 if any case fails. |
| `hound profile diff <name> --library <checkout>` | Shows the lines that differ from the library version named in `source`. |

`make test-profiles` runs `hound profile test` for every library profile; CI runs it on every push.

## Using a profile

```yaml
collectors:
  - name: example-assets
    type: rest
    schedule: "0 5 * * *"
    settings:
      profile: example
      client_id: 00000000-0000-4000-8000-0000000000c1
      supported_os: "14, 15"
    secrets:
      client_secret: file:/run/secrets/example-assets_client_secret
```

| Setting | Required | Meaning |
| --- | --- | --- |
| `profile` | yes | The profile's name, its file name without `.yaml`. |
| `profiles_path` | no | The profiles folder. Default `/config/profiles`. |
| `include_identities` | no | `true` keeps the identities the profile lists in evidence. Default `false`: they are hashed. |
| `allow_writes` | no | `true` lets the profile's `write: true` queries run, such as the opt-in pipeline trigger of `terraform-drift-trigger`. Default `false`: they are refused and nothing is sent. Give the instance a token that can do only that one write. |
| `require_profile_tests` | no | `true` makes a profile without tests unable to assess. Default `false`: a warning in the log. |

Every other setting is one of the profile's `parameters`, and every secret one of its `secrets`. `hound add collector rest --set profile=<name>` asks for the ones the profile requires. `hound config validate` checks each instance against its profile: unknown settings, missing parameters, values of the wrong type and missing secrets are all reported before anything is called.

## Profile reference

A profile has these top-level keys. Anything else is refused (`profile.key_unknown`).

```yaml
profile: example                 # the name; must match the file name
version: "1.0.0"                 # your version of this profile
source: evidence-hound/profiles/example.yaml   # where it was copied from, for hound profile diff
description: One line on what it reads.
requires:
  hound: ">=0.1"                 # the oldest hound that can run it
parameters: { ... }
secrets: { ... }
connection: { ... }
bootstrap: { ... }               # optional
queries: { ... }
datasets: { ... }                # optional
checks: [ ... ]
```

### Parameters and secrets

```yaml
parameters:
  client_id:
    type: string
    required: true
    description: Client ID of the read-only API client.
  stale_after:
    type: duration
    default: P30D
    description: A device not seen for longer than this is stale.
secrets:
  client_secret:
    required: true
    description: Client secret of the read-only API client.
```

| Type | Holds |
| --- | --- |
| `string` | Any text. |
| `int` | A whole number. |
| `number` | A number, such as `0.95`. |
| `bool` | `true` or `false`. |
| `list` | Comma-separated values, such as `14, 15`. |
| `duration` | An ISO 8601 duration, such as `P30D` or `PT12H`. |
| `url` | An absolute `http` or `https` address. |

A parameter is required when it says so and has no `default`. Text anywhere in the profile can use placeholders:

| Placeholder | Is |
| --- | --- |
| `${name}` | A parameter, or a variable a query set with `vars`. |
| `${secrets.name}` | A secret the profile declares. Evidence records the request with the placeholder, never the value. |
| `${item.path}` | A field of the item a `for_each` query is called for, such as `${item.id}`. |

A placeholder with no value is a problem (`rest.value_missing`), never an empty string.

### Connection

```yaml
connection:
  base_url: ${base_url}
  allowed_hosts: [api.example.com, login.example.com, "${base_url}", "${token_url}"]
  auth:
    type: oauth2_client_credentials
    token_url: ${token_url}
    client_id: ${client_id}
    client_secret: ${secrets.client_secret}
    scope: read
  rate_limit:
    per_second: 5
    max_retries: 3
  tls:
    pinned_thumbprint: ""        # optional: accept a self-signed certificate only with this SHA-256 thumbprint
```

Relative query paths are resolved against `base_url`.

| Auth `type` | Keys | Sends |
| --- | --- | --- |
| `none` | | Nothing. |
| `oauth2_client_credentials` | `token_url`, `client_id`, `client_secret` or `client_certificate`, `scope` | A bearer token from the token endpoint, renewed when it expires. With `client_certificate` (PEM with its key), a signed client assertion instead of a secret. |
| `api_key` | `in` (`header` or `query`), `name`, `value` | A static key. |
| `bearer` | `token` | A static bearer token. |
| `basic` | `username`, `password` | HTTP basic authentication. |
| `mutual_tls` | `certificate` | A client certificate (PEM with its key) as the only credential. |

`rate_limit` defaults to 5 requests a second and 3 retries. A `429` or `503` answer is retried after its `Retry-After`. Redirects are followed at most 5 times, and only on the same host; any other redirect is refused (`rest.redirect_refused`) and nothing is sent.

### Queries

```yaml
bootstrap:
  whoami:
    get: v1/whoami
    vars:
      org_id: $.organisation.id

queries:
  devices:
    get: v1/orgs/${org_id}/devices
    paging: { style: next_link, next: $.next }
    items: $.items
    identities: [hostname, serial, owner]
```

A **bootstrap** query runs before the others, is not saved as evidence and cannot be checked; it sets variables with `vars` (name to path). A query may also set `vars` for queries after it; `hound` orders queries by what they use and refuses a cycle (`profile.dependency_cycle`).

| Key | Meaning |
| --- | --- |
| `get` or `post` | The path or address. |
| `headers`, `query`, `body` | Request headers, query parameters and (for `post`) a JSON body. |
| `items` | Where the list is in each answer. Default `$`, the answer itself. |
| `paging` | How to follow pages (below). |
| `identities` | Property names, at any depth, that identify a person, device or network. Hashed in evidence and in datasets unless `include_identities` is `true`. |
| `vars` | Values later queries can use as `${name}`. |
| `for_each` | Another query: this one is called once per item of it, with `${item.path}`. At most `max_calls` calls (default 100). |
| `optional_on` | Status codes that mean "not available here", such as a missing licence: `[403, 404]`. Checks over it become not applicable, with the reason. |
| `async` | A report run as a job: `status_url` (path in the first answer), `done` (a test on the status answer), `result_url`, `poll_seconds` and `timeout_seconds`. |
| `format` | `auto` (from the content type), `json`, `xml`, `csv` or `zip`. `arrays` names XML paths that are always lists. |
| `write` | `true` marks a query that changes something, such as starting a pipeline run (`terraform-drift-trigger`). It runs only when the collector instance also sets `allow_writes: true`; otherwise nothing is sent and checks over it are unable to assess. Every other query only reads. |
| `redirect_hosts` | Hosts other than the API's that a redirect from this query's GET may lead to, such as the signed storage a pipeline artefact downloads from. They get a plain GET with no sign-in and no headers, so they never receive credentials. Any other redirect off the API's host is refused (`rest.redirect_refused`). |
| `member` | With `format: zip` (a pipeline artefact, for example), the file in the archive to read, as JSON, XML or CSV by its extension. Required with `zip`, refused without it. The archive and the unzipped member both count towards `max_bytes`, so a member that unzips past the cap is refused. |
| `keep` | Keep only these properties of each item, in memory and in evidence, for very large lists. |
| `max_items`, `max_bytes`, `max_pages` | Caps. Defaults 50,000 items, 50 MB and 1,000 pages. Over a cap, checks over the query are unable to assess, naming the cap. |

| Paging `style` | Keys | Follows |
| --- | --- | --- |
| `next_link` | `next` | The next page's address at a path in the answer, until it is absent. |
| `page_number` | `page`, `size_param`, `size`, `total_pages` | Page numbers from 1 until `total_pages` (a path in the answer). |
| `cursor` | `cursor`, `param` | A cursor at a path, sent back as query parameter `param`, until there is none. |
| `offset` | `offset_param`, `limit_param`, `limit`, `total` | Offset and limit, until `total` items, or a short page when there is no total. |
| `link_header` | | The `Link` header's `rel="next"`. |

Paths are a small, fixed language so every path can be checked before a run: `$`, then `.name`, `['any name']`, `[2]` or `[*]` (every element of a list).

### Datasets

A dataset shapes one query's items into the rows a check needs.

```yaml
datasets:
  user_roles:
    from: users
    join:
      - with: roles
        on: { left: "$.roleIds[*]", right: $.id }
        as: roles
        optional: true
    where: { field: $.status, equals: active }
    fields:
      id: $.id
      mfa: $.mfa.enabled
      role_names: $.roles[*].name
```

| Key | Meaning |
| --- | --- |
| `from` | A query or another dataset. |
| `join` | Attach the rows of `with` whose `right` value equals a `left` value of the row, as a list under `as`. A join that is not `optional` counts rows that match nothing, and checks over the dataset report them. |
| `flatten` | A path to a list in each row: one row per element, with the original row under `parent` (a plain value goes under `value`). |
| `where` | Tests (as in checks) a row must pass to stay. |
| `fields` | Output name to path. Without it, whole rows are kept. A path that finds nothing leaves the field out. |

A dataset built from a query that failed, or is not available, fails or is not available with it, and says which.

### Checks

```yaml
checks:
  - id: example.device.os_supported
    title: Devices run a supported operating system
    over: devices
    each: { field: $.os.version, version_in: "${supported_os}" }
    require: all
    when_empty: not_applicable
    describe: "{passed} of {total} devices run a supported operating system. Supported: ${supported_os}."
```

| Key | Meaning |
| --- | --- |
| `id` | The framework-neutral check ID that mappings use, such as `network.firewall.firmware_supported`. Unique in the profile. |
| `over` | The query or dataset it reads. A code check may read several: `[admins, roles]`. |
| `where` | Tests that put a row in scope. Rows out of scope are not tested. |
| `each` | The test every row in scope is put to. |
| `require` | How row results become the check's result (below). |
| `when_empty` | What no rows in scope means: `not_applicable`, `fail` or `unable_to_assess`. Required: there is no default, because an empty list means different things for different checks. |
| `describe` | The observation. `{passed}`, `{failed}`, `{total}` (rows read) and `{matched}` (rows in scope) are filled in, as are `${...}` placeholders. Failing rows are listed after it, by `id` or `name`, up to 20. |
| `code`, `with` | A built-in code check instead of `each` and `require`, with its own settings. |
| `history` | For a code check only: a query it is over whose items from the instance's earlier runs it is also given, oldest first, as the evidence store still keeps them. The run saves them as `<query>.history.json`, which the result cites, so it can be reproduced from this run's evidence. For things an API gives no date for, such as how long a firmware upgrade has been offered. |

A **test** is a map with `field`, one operator, and optionally `missing` and `negate`:

| Operator | Passes when the field |
| --- | --- |
| `equals` | Equals the value (text, number or `true`/`false`). |
| `not_equals` | Does not equal the value. |
| `in` | Equals one of a list, such as `"enabled, enforced"` or a `list` parameter. |
| `matches` | Matches a regular expression. |
| `any_matches` | Is a list with at least one element matching a regular expression. |
| `exists` | Is present and not null (`exists: true`). For "is absent", add `negate: true`. |
| `version_in` | Is a version within one of a list: `21.0.1` is within `21.0` and `21`, not within `21.5`. |
| `version_at_least` | Is a version at least this one. |
| `within` | Is a date no older than a duration, such as `P30D`. |
| `older_than` | Is a date older than a duration. |
| `at_least` | Is a number at least this one. |
| `at_most` | Is a number at most this one. |

`missing` says what a missing field means: `unable_to_assess` (the default), `pass` or `fail`. `negate: true` turns a pass into a fail and a fail into a pass; it does not change `missing`. A test that cannot be decided (a field that is not a date for `within`, say) makes the check unable to assess, naming the row.

| `require` | Pass when |
| --- | --- |
| `all` | Every row in scope passes `each`. |
| `any` | At least one does. |
| `none` | None does. |
| `count_at_most`, as `{ count_at_most: N }` | At most N rows are in scope (N may be a placeholder). |
| `count_at_least`, as `{ count_at_least: N }` | At least N rows are in scope. |
| `{ ratio_at_least: 0.95, partial_at: 0.8 }` | At least 95% pass. From `partial_at` up to it, the result is `partial`; below it, `fail`. |

Code checks are for what the operators cannot say. They are built into `hound`, named and tested; a profile names one with `code`. Today they are `sophos.super_admin_count` (Sophos Central admins and roles, `with: { max: ... }`), `sophos.firmware_upgrade_age` (firewalls and the firmware upgrade check, `history` of the upgrade check, `with: { days: ... }`), `terraform.no_drift` and `terraform.tests_pass`. There are no runtime code plugins.

## Results

Every check gives one of these, which the mapping turns into CISO Assistant results:

| Result | Means |
| --- | --- |
| `pass` | The requirement holds on the evidence read. |
| `fail` | It does not. The observation names the rows that fail. |
| `partial` | Only for `ratio_at_least` with `partial_at`: between the two. |
| `not applicable` | Nothing to assess: no rows (with `when_empty: not_applicable`), or the API said the list is not available here (`optional_on`). |
| `unable to assess` | The evidence could not be read or decided: a failed query, a cap reached, a missing field, a join with unmatched rows. It never counts as a pass or a fail. |

A broken query only affects the checks that read it. The run's `_manifest.json` names the profile, its version, its source and the SHA-256 of the file that ran.

## Limits

| Limit | Default | Change it with |
| --- | --- | --- |
| Items per query | 50,000 | `max_items` |
| Bytes per query | 50 MB | `max_bytes` |
| Pages per query | 1,000 | `max_pages` |
| Calls per `for_each` query | 100 | `max_calls` |
| Requests per second | 5 | `rate_limit.per_second` |
| Retries after 429 or 503 | 3 | `rate_limit.max_retries` |
| Redirects | 5 | Fixed |

## Security

- **Allowed hosts.** Credentials, tokens and requests only go to hosts in `connection.allowed_hosts`: a host name, a wildcard for its sub-domains (`*.central.sophos.com`), or an address (an entry with a port matches only that port). Anything else, including a data region named by the API, is refused with `rest.host_not_allowed` and nothing is sent. Credentials go only over `https`, except to this machine for recorded tests (`profile.host_insecure`).
- **Secrets.** Secrets come only from the instance's `secrets`, which reference files or environment variables, and a profile can only use the secrets it declares. Before evidence is written, it is searched for every secret value; a file holding one is refused (`rest.secret_in_evidence`) and its checks are unable to assess.
- **Identities.** The properties a query lists in `identities` are replaced by `sha256:` and the first 16 hex characters of a SHA-256 hash, before datasets and checks see them, unless the instance sets `include_identities: true`. The same value gets the same hash in every run, so findings can be followed over time.
- **Read only.** A profile can `post` (some APIs search with a POST), but it is meant for reading. Give the API credential the least role that can read the lists.
- **No language model.** Collection and assessment are the profile and the engine only.

## Problems

Profile problems start with `profile.` and are listed by `hound profile validate`, with the line they are on. Run problems start with `rest.`:

| Code | Meaning |
| --- | --- |
| `rest.auth_failed` | The token endpoint or API refused the credential. |
| `rest.host_not_allowed` | A request or token would have gone to a host not in `allowed_hosts`. Nothing was sent. |
| `rest.redirect_refused` | The API redirected to another host. Nothing was sent there. |
| `rest.value_missing` | A placeholder has no value: a missing parameter, or a variable a query did not set. |
| `rest.rate_limited` | Still `429` or `503` after every retry. |
| `rest.response_invalid` | An answer that is not the format expected, or has no items where `items` says. |
| `rest.zip_member_missing` | A zip answer without the `member` the query names; the message lists what the archive holds. |
| `rest.response_too_large` | A zip `member` that unzips past `max_bytes`. |
| `rest.job_failed`, `rest.job_timeout` | An `async` report failed or did not finish in time. |
| `rest.secret_in_evidence` | Evidence held a secret value; it was not written. |
| `rest.unreachable` | The API could not be reached (network, DNS or proxy). |
| `profile.tests_missing` | `require_profile_tests` is `true` and the profile has no tests. |
