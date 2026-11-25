# Design: a generic REST collector

Status: Implemented (s09-09 to s09-14, Nov 22). The reference is [docs/collectors/rest.md](../collectors/rest.md) and the tutorial [Write a profile](../tutorials/write-a-profile.md). Changes made during the build are listed at the end, under "As built".

## The problem

Every collector today is hand-written C# for one API. For each system we write code that signs in, follows that vendor's paging, saves the lists as evidence, parses the JSON into typed records, and then evaluates checks over those records. Getting the evidence right means reading API docs, writing a parser, and only then writing the check that matters.

The shape is already good. The Sophos and Microsoft 365 collectors both read each list once per run, save it as evidence, and run pure check functions over the saved data (`SophosSources` and `SophosChecks`, `GraphSources` and `M365Checks`). Many checks already share one list. What is expensive is that **every part of that shape is code**, written again for each vendor:

| Part | Sophos today | Generic? |
| --- | --- | --- |
| Sign in | OAuth client credentials, then `whoami` for tenant and data region | Yes, with a "bootstrap" step |
| Paging | `page` and `pages.total` | Yes, one of a few styles |
| Evidence and privacy | Raw list, identities hashed by property name | Yes, from a list of property names |
| Parse to typed records | `SophosData.Parse` | Not needed if checks read JSON directly |
| Checks | Pure C# per check | Most are "for each item, test a field"; some are not |

So the question is not "should queries and checks be separate" (they already are), but **"should queries and checks be configuration instead of code"**.

## Goals

1. Add a system by writing a **profile** (YAML), not a C# project, for the common case.
2. Call each endpoint **once per run**, eagerly and with paging, and let **many checks share one response**. A 1 to 1 check is just a query with one check.
3. Each check **pulls fields out and tests them against the requirement**, giving pass, fail, or **unable to assess**.
4. A broken query, a missing field or a wrong path never shows as a control failure.
5. Keep checks framework-neutral. Profiles emit check IDs; the mapping files stay as they are.
6. Keep TDD: a profile is tested against recorded responses before it is trusted.

Not goals: a general workflow engine, writing to any API, or replacing the manual collector.

## Options

### A. Keep coded collectors, extract a shared HTTP kit

Move auth, paging, rate limiting, evidence saving and redaction into a reusable library. Each vendor is still a C# project, but much thinner.

- Gains: smallest change; full power of C# for every check; nothing new to learn.
- Costs: still a code change, review, release and new image for every system and every new check. The GRC team cannot read or adjust a check without reading C#. Does not address the person's main pain.

### B. Declarative REST collector with a code escape hatch (recommended)

One built-in collector type, `rest`, driven by a profile. The profile declares the connection, the queries and the checks. Checks use a small, fixed set of operators over JSON paths. Anything the operators cannot express is a **code check**: a named, pure C# function built into `hound`, with the same inputs as a declarative check.

- Gains: most systems need no code; checks are readable by the GRC team and auditors; one tested engine for auth, paging, retries and redaction; fewer API calls; one consistent snapshot; profiles are testable against saved responses.
- Costs: we design and maintain a small language; some logic still needs code; a wrong path has to be caught (see "Making failure visible").

### C. Declarative queries, checks in a general expression language

Like B, but each check is an expression in an existing language: JMESPath, JSONata, jq or CEL.

- Gains: more expressive without code (filters, projections, some date maths in JSONata and CEL); someone else maintains the language.
- Costs: expressions get dense and hard for non-developers to review; static validation of an expression is weaker; .NET support varies by language (JMESPath and JSONata have .NET ports; CEL and jq support in .NET would need checking before we rely on it); one more dependency in the core.

### Recommendation

**B**, with two guard rails: start with a deliberately small operator set, and keep the code-check hatch from day one. If, after converting Sophos and writing two or three new profiles, the operators keep falling short in the same way, add JMESPath as one more operator (`expression:`) rather than switching wholesale. That keeps the common case readable and leaves a path to C.

## How B works

### Three phases: collect, extract, assess

Evidence for one control often spans two or more API calls: an admin's role comes from one endpoint, their MFA state from another, and the firmware they run from a third. So a run is three phases, each finished before the next starts:

```text
 1. Collect                       2. Extract                          3. Assess
 every query, once                named evidence datasets             every check
 ------------------------         -----------------------------       ---------------------------
 token, whoami  (bootstrap)       admin_accounts =                    super_admin_count
 admins, roles, firewalls  --->     admins + roles (join on roleId)   ---> firmware_supported
 firmware_upgrades (per fw)       firewall_firmware =                 firmware_current
                                    firewalls + firmware_upgrades
 raw responses saved              datasets saved, with lineage        results cite datasets
```

1. **Collect.** Run every query the profile needs, including dependent ones, as a graph: a query that needs an ID from another (`whoami` before anything, one call per firewall after `firewalls`) waits for it; independent ones can run in parallel within the rate limit. Each response is saved as raw evidence. No check runs until collection ends, so every check sees the same snapshot.
2. **Extract.** Build named **evidence datasets** from one or more responses: pick the fields that matter, join responses on a key, filter to what is in scope. A dataset is the evidence a control is judged on, for example "admin accounts with their roles and MFA state". Each dataset is saved as its own evidence file, listing the raw files (and their hashes) it came from, so an auditor can trace a result back to the API responses.
3. **Assess.** Each check reads one dataset and tests its fields against the requirement. Checks never call an API or join anything themselves.

This splits the hard parts. Collecting is about APIs (auth, paging, retries). Extracting is about data (which fields, which joins). Assessing is about the control (what good looks like). Each can be tested on its own: extraction against recorded responses, checks against a small hand-written dataset.

**When one of several queries fails**, only what depends on it is affected. A failed query makes every dataset built from it, and every check reading those datasets, **unable to assess**, naming the failed query. Checks that do not depend on it still run and report normally. A dataset is never built from a partial set of its inputs.

**Evidence from two different systems** (for example, a user list from an HR system checked against identity provider accounts):

- Within one product family, one profile can declare more than one connection, and datasets can join across them.
- Across separate collector instances, the first answer is the mapping file: the requirement needs both systems' checks to pass. A true join across instances (one dataset built from two collectors' responses) is a later step; it needs the runner to share snapshots between collectors, and we should wait for a real case before building it.

### Profile and instance

- A **profile** holds what is true for every customer of a product: endpoints, paging, which properties identify people, the checks. No profile is built into `hound`. Every profile loads the same way, from a profiles folder the organisation keeps in its own config repo, so a new system does not need a new release. This repo keeps a public library of profiles to copy from (for example `profiles/sophos-central.yaml`). See "Where profiles live".
- A **collector instance** in the deployment config names the profile and gives the per-organisation parts: settings, secrets, thresholds.

```yaml
# deployment config (fictional)
collectors:
  - name: sophos-example
    type: rest
    settings:
      profile: sophos-central
      client_id: 00000000-0000-0000-0000-000000000000
      supported_firewall_firmware: 20.0, 21.0
      super_admin_max: 2
    secrets:
      client_secret: env:SOPHOS_CLIENT_SECRET
```

### Queries

```yaml
profile: sophos-central
version: 1

parameters:                       # validated before anything is called
  client_id: { type: string, required: true }
  supported_firewall_firmware: { type: list, required: true }
  super_admin_max: { type: int, default: 2 }
secrets:
  client_secret: { required: true }

connection:
  base_url: https://api.central.sophos.com/
  allowed_hosts: ["*.central.sophos.com", id.sophos.com]   # secrets and tokens go nowhere else
  auth:
    type: oauth2_client_credentials
    token_url: https://id.sophos.com/api/v2/oauth2/token
    client_id: ${client_id}
    client_secret: ${secrets.client_secret}
    scope: token
  rate_limit: { per_second: 5, retry_after: honour, max_retries: 3 }

bootstrap:                        # runs first; its values feed later requests
  whoami:
    get: whoami/v1
    vars:
      tenant_id: $.id
      data_region: $.apiHosts.dataRegion

queries:
  firewalls:
    get: ${data_region}/firewall/v1/firewalls
    headers: { X-Tenant-ID: "${tenant_id}" }
    paging: { style: page_number, page: page, size: pageSize=100, total_pages: $.pages.total }
    items: $.items
    identities: [hostname, name, serialNumber, ipv4Addresses, macAddresses]
  admins:
    get: ${data_region}/common/v1/admins
    headers: { X-Tenant-ID: "${tenant_id}" }
    paging: { style: page_number, page: page, size: pageSize=100, total_pages: $.pages.total }
    items: $.items
    identities: [firstName, lastName, email]
  roles:
    get: ${data_region}/common/v1/roles
    headers: { X-Tenant-ID: "${tenant_id}" }
    items: $.items
```

The engine supports, at first:

- **Auth:** OAuth 2.0 client credentials (secret or certificate), static API key (header or query), bearer token, basic, and mutual TLS. Tokens are refreshed on expiry.
- **Paging:** next link in the body (Graph `@odata.nextLink`), page number with total, cursor or continuation token, offset and limit, and the HTTP `Link` header. A page cap per query stops endless paging, as `SophosClient.MaxPages` does today.
- **Rate limits:** per-profile request rate, `Retry-After` on 429 and 503, bounded retries.
- **Bootstrap variables** for flows like Sophos `whoami` that tell you where to read from.
- **Fan-out** (`for_each: firewalls`) for APIs that need one call per item, capped, because it multiplies calls.
- **XML:** converted to the same JSON tree at the edge (attributes as `@name`, text as `#text`), so checks use one path language. A profile lists paths that must always be arrays, which avoids the usual "one element is an object, two are an array" trap.

Each query is read **once per run, before any check runs**, and saved as one evidence file. Checks only ever see the saved snapshot, so every result can be reproduced from the evidence.

A dependent query names what it needs, and the engine orders the calls:

```yaml
  firmware_upgrades:
    # Illustrative: s08-13 confirms the real endpoint and whether release dates are exposed.
    for_each: firewalls               # one call per firewall, after firewalls is read
    max_calls: 200
    post: ${data_region}/firewall/v1/firewalls/actions/firmware-upgrade-check
    body: { firewalls: ["${item.id}"] }
    items: $.firewalls
```

### Datasets

```yaml
datasets:
  admin_accounts:
    from: admins
    join:
      - with: roles
        on: { left: $.roleAssignments[*].roleId, right: $.id }
        as: roles                     # each admin gains a list of its roles
    fields:
      id: $.id
      role_names: $.roles[*].name
      # identities stay hashed, as in the raw evidence
  firewall_firmware:
    from: firewalls
    join:
      - with: firmware_upgrades
        on: { left: $.id, right: $.id }
        as: upgrades
        optional: true                # no row means no upgrade is available
    fields:
      id: $.id
      version: $.firmwareVersion
      upgrade_released: $.upgrades[0].releasedAt
```

The first extraction operations, kept small on purpose: `from` (one query), `join` (on a key, one-to-many allowed, `optional` for a left join), `where` (filter), `fields` (pick and rename), and `flatten` (one row per element of a nested list). A join whose key matches nothing on a required side is reported, not silently dropped: the dataset records how many rows did not match, and a check can treat unmatched rows as unable to assess.

A one-to-one check needs no dataset: a check can read a query directly, and the engine treats it as a dataset of that query's items.

### Checks

```yaml
checks:
  - id: network.firewall.firmware_supported
    over: firewall_firmware
    when_empty: not_applicable
    each:
      field: $.version
      version_in: ${supported_firewall_firmware}
    require: all
    describe: "{passed} of {total} firewalls run supported firmware. Supported: ${supported_firewall_firmware}."

  - id: network.management.super_admin_count
    over: admin_accounts
    where: { field: $.role_names, any_matches: "(?i)^super ?-?admin$" }
    require: { count_at_most: ${super_admin_max} }
    describe: "{matched} of {total} Sophos Central admins hold Super Admin; at most ${super_admin_max} allowed."

  - id: network.firewall.firmware_current
    over: firewall_firmware
    each:
      field: $.upgrade_released
      missing: pass                   # no upgrade available is up to date
      older_than: P14D
      negate: true
    require: all
    when_empty: not_applicable
    describe: "{failed} of {total} firewalls have had a firmware upgrade available for more than 14 days."
```

With the join done in extraction, "updated within 14 days of release" no longer needs code. Code checks stay for logic that is not "test a field of each row", such as conditional access policy coverage.

A declarative check has four parts:

1. **`over`**: which dataset (or query) it reads. Many checks can name the same dataset.
2. **`where`** (optional): which items are in scope, for example only enabled users, or only admins with a given role.
3. **`each`**: the test applied to a field of each item in scope.
4. **`require`**: how item results become the check result: `all`, `any`, `none`, `count_at_most`, `count_at_least`, or `ratio_at_least` with an optional partial band (for example pass at 95 %, partial from 80 %).

The first operator set, deliberately small: `equals`, `not_equals`, `in`, `matches` (regex), `exists`, `version_in`, `version_at_least`, `within` and `older_than` (a duration against the run's clock), `at_least` and `at_most` for numbers, and `any_matches` for lists. Each test can say what a missing field means (`missing:`) and can be negated. Joins are not a check operator: they belong to datasets.

`describe` is the observation written to CISO Assistant, with counts filled in. Offending items are listed in the observation (capped, as today) and in full in the evidence.

### Code checks

A code check is a pure function built into `hound` under a name (a small library in the repo, reviewed and tested like any other code, not loaded at runtime): it receives the snapshots it names, its parameters and the clock, and returns a `Finding`. That is exactly the signature `SophosChecks` already has, so today's checks can move over unchanged and be replaced by declarative ones one at a time. Microsoft 365 conditional access coverage is expected to stay as code.

### Where profiles live

| Kind | Where | How it gets to `hound` | Who changes it |
| --- | --- | --- | --- |
| Profile library | `profiles/` in this repo, each profile with fictional recordings and tests | Not loaded by `hound`. Copied into an organisation's profiles folder; the interactive tutorial copies the ones it uses | Pull request here; CI runs every library profile's tests |
| Organisation profile | A `profiles/` folder in the organisation's private config repo, next to its deployment config | Mounted read-only into the container (setting `profiles_path`, default `/config/profiles`); the only place `hound` reads profiles from | Pull request in that private repo, same `hound profile validate` and `hound profile test` |
| Code check | `src/` in this repo | Compiled into `hound` | Pull request here, test-first |

Rules:

- **One way in.** `hound` reads profiles only from the profiles folder, so there is nothing to override and the image stays the same size whatever profiles exist. A copied library profile keeps its `source` and `version` fields, so `hound profile diff <name>` can show how a local copy differs from the library version it came from.
- **Validated before use.** Every profile passes the same schema and reference checks before any request is made. A profile that fails validation stops only its own collector instances, as unable to assess, not the whole run.
- **No code in profiles.** A profile can only name built-in operators and built-in code checks. It cannot load a DLL, run a script or call a shell, so a profile cannot do more than make the read requests it declares.
- **Secrets stay on declared hosts.** A profile's `allowed_hosts` bounds where its tokens and secrets can go; redirects to other hosts are refused.
- **Compatible versions.** A profile declares the `hound` features it needs (`requires: { hound: ">=1.2" }`), so an older image refuses it clearly instead of half-running it.
- **Traceable.** Each run's manifest records every profile's name, version and SHA-256, so evidence shows which rules judged it. A changed profile is visible in the next run's manifest.
- **Tests encouraged, optionally required.** `hound` warns when a profile in the folder has no tests (library profiles bring theirs when copied); the deployment setting `require_profile_tests: true` makes that an error.

This replaces the earlier plan to load collector DLLs from a known folder at runtime. The folder still exists, but it holds YAML, not code. Runtime code plugins are not needed for release 1, and they are only worth revisiting if a system needs a protocol the REST engine cannot speak (see "Edge cases").

Alternatives considered:

- **Profiles built into the image:** works without copying anything, but every new system or tweak waits for a release, the image grows with every vendor, and built-in and local profiles need override rules. Rejected in favour of one load path.
- **Built-in library plus organisation folder (the earlier draft):** convenient for common vendors, at the cost of two load paths and a no-override rule. Replaced by the library-to-copy model.
- **Profiles fetched from a URL or registry:** convenient, but a supply chain risk for a tool that holds API credentials. Rejected.
- **Profiles stored in CISO Assistant:** keeps everything in the system of record, but CISO Assistant has no place for them and it would mix GRC records with collector code. Rejected.

## Edge cases

| Case | How it is handled |
| --- | --- |
| Evidence across several endpoints of one API | Datasets join the responses (see "Three phases"). |
| Evidence across two collectors | Mapping requires both checks; a cross-collector join waits for a real case. |
| A query needs IDs from another | Dependency graph and `for_each`, capped by `max_calls`. |
| Sign-in reveals where to read (Sophos `whoami`) | `bootstrap` variables. |
| Paging styles | Next link, page number and total, cursor, offset and limit, `Link` header; page cap. |
| Rate limits, 429, 503 | Per-profile rate, `Retry-After`, bounded retries; then unable to assess. |
| Reports that run as a job (start, poll, download) | `async` query: a start request, a status URL polled with a timeout, then the result download. |
| CSV or XML responses | Converted at the edge to the same tree as JSON; checks do not change. |
| POST queries (search APIs, GraphQL, hunting queries) | `post:` with a body; paging works the same. |
| Licence missing for one endpoint | `optional_on: [403, 404]` on the query; dependent checks become not applicable with the reason, as M365 does today. |
| Very large lists | Item and byte caps, `keep:` to hold only needed fields. |
| Wrong path or renamed field | Missing field is unable to assess; profile tests against recordings catch it before use. |
| Empty lists | `when_empty` is required on every check. |
| Logic beyond "test a field of each row" | Built-in code check, named in the profile. |
| Non-REST systems (SNMP, SSH, LDAP, file shares) | Out of scope. The manual collector covers them; a code collector only if one becomes important. |
| Push-only systems (webhooks, syslog) | Out of scope for a scheduled collector; use the system's export or the manual collector. |

## Making failure visible

The biggest risk of configuration is that a wrong path quietly returns nothing and a check passes over an empty list. The design closes that off in five ways.

**1. Three results, plus the two we already have.** The core already has `Error`, and the mapping engine already turns any `Error` into `not_assessed` for the requirement (`MappingEngine.Assess`). So "unable to assess" exists in the domain; this design makes it the default whenever the engine cannot be sure, and renames it in user-facing text from "error" to **unable to assess**. The full set a check can return:

| Result | When |
| --- | --- |
| pass | Items in scope meet the requirement. |
| fail | Items in scope were read and some do not meet it. |
| unable to assess | A query the check depends on (directly or through a dataset) failed, paging stopped early, a page cap was hit, a required field was missing on any item in scope, or a value had the wrong type. The observation says which. |
| not applicable | The list was read successfully and was empty, and the check says `when_empty: not_applicable`. |
| partial | Only for `ratio_at_least` with a partial band. |

**2. No silent empties.** Every check must say what an empty list means (`when_empty`: `not_applicable`, `fail` or `unable_to_assess`). There is no default, so "all of nothing passes" cannot happen by accident.

**3. Missing is not false.** If the field a check tests is absent on an item, the check is unable to assess, not failed. A profile can say `missing: fail` for a field whose absence genuinely means "not configured", but it has to say so.

**4. Validate before calling.** `hound` validates a profile and the instance before any request: parameters, secrets, path syntax, that every `over` names a query, that every code check is registered, and that every check ID is in the mapping (as for coded collectors today).

**5. Profile tests.** Each profile ships with recorded, fictional responses and the expected result per check, and `hound profile test <profile>` runs them offline. This is how strict TDD applies to profiles: write the recording and the expected outcome (red), then the check (green). CI runs every profile's tests.

## Evidence, privacy and secrets

- Each query is saved as one JSON file: the request (without headers), page count, item count, and the items.
- **Identities** listed in the profile are hashed, as `SophosEvidence` does today, unless the instance sets `include_identities`. Profiles default to hashing.
- **Secrets never reach evidence.** Request headers and the token exchange are never saved. A final scan rejects any saved file containing a value of a secret in use, and fails that query as unable to assess rather than saving it.
- **Secrets only go to `allowed_hosts`.** Profiles are configuration that can make requests with your credentials, so a profile cannot send a token or secret to a host it did not declare, and cross-host redirects are refused. This is what stops a bad or edited profile from leaking a credential.
- **Size budgets.** Each query has an item cap and a byte cap (defaults set by the engine, raised per query). Over the cap is unable to assess, with the cap named, rather than a run that eats the host's memory. A query can declare `keep:` to save and hold only the fields its checks use, for very large lists.

## Trade-offs in one place

| | We gain | It costs |
| --- | --- | --- |
| Profiles instead of code | New systems and checks without a release; readable by the GRC team | A small language to design, document and keep stable |
| One query, many checks | Fewer calls, fewer rate limit problems, one consistent snapshot | Memory for big lists; budgets and `keep:` needed |
| Collect, extract, assess as separate phases | Evidence across several APIs without code; each phase tested alone; auditors see the dataset a control was judged on | A third concept (datasets) to learn; one more evidence file per dataset |
| Fixed operators | Easy to read, easy to validate | Hard logic needs code checks |
| Code-check hatch | Nothing is out of reach | Two ways to write a check; a new code check needs a release |
| Unable to assess by default | Broken queries never show as control failures on the board report | More results show "not assessed" until profiles are right, which is the honest answer |
| Profiles loaded at runtime | Systems added without a new image | A profile is now security-relevant config; host allowlist and review needed |

## How existing collectors move over

- **Sophos is the proof.** Write `profiles/sophos-central.yaml` in the library and run it beside the coded collector on the same recordings. A parity test requires the same check IDs, outcomes and evidence content. When it passes, the coded collector is removed; existing `type: sophos` instances move to `type: rest` with `profile: sophos-central`, and `hound` explains the change when it meets the old type. The interactive tutorial copies the profile into the trial's profiles folder.
- **Microsoft 365** follows only if Sophos goes well. Most of its reads fit; several checks (conditional access coverage, PIM) are likely to stay as code checks inside the profile.
- **Manual** and **vuln-scan** are not REST and stay as they are.
- New systems after that (the roughly 13 SaaS systems) start as profiles. Where an API is weak, the manual collector stays the answer.

## Decisions (all agreed Nov 22)

1. **Option B with the code hatch**, adding JMESPath later only if the operators keep falling short.
2. **XML converted to the JSON tree** rather than a second path language (XPath).
3. **Evidence keeps the full response minus identities and secrets**, rather than only the fields checks use. Fuller evidence for auditors, at the cost of size; `keep:` is there for very large lists.
4. **Missing fields mean unable to assess** unless the profile says otherwise.
5. **No profiles built into `hound`.** Every profile loads from the organisation's mounted profiles folder. This repo keeps a tested public library to copy from, and the interactive tutorial uses it. No runtime code plugins.
6. **Collect, extract, assess as three phases**, with joins in named datasets rather than in checks, and joins across separate collector instances left until a real case needs one.

## s09 tasks

Added to s09 rather than a new sprint, ahead of s09-01 so new Microsoft 365 and Veeam work can be profile checks. Full acceptance criteria are in the sprint file:

- **s09-09 Profile model and validation.** Profile and instance schema, parameters, secrets, `allowed_hosts`, `hound profile validate`. Pure; no HTTP yet.
- **s09-10 Query engine.** Auth types, paging styles, rate limits and retries, a dependency graph for bootstrap variables and fan-out, XML conversion, budgets, snapshot evidence with identity hashing and the secret scan. Contract tests with WireMock.
- **s09-11 Datasets and declarative checks.** Extraction (`from`, `join`, `where`, `fields`, `flatten`) with lineage and unmatched-row counts; failure propagation from queries to datasets to checks; check operators, `where`, `require`, `when_empty`, missing-field rules, observations; "unable to assess" wording in user-facing text. Property tests over the evaluator.
- **s09-12 Code checks and profile tests.** Built-in code-check library with names profiles can use; profiles folder as the only load path, with validation, `requires`, `source` and manifest hashes; `hound profile test` and `hound profile diff`; the public `profiles/` library with an `example` profile; CI runs every library profile's tests.
- **s09-13 Sophos as a profile.** `profiles/sophos-central.yaml` in the library; parity test against the coded collector; then remove the coded collector, migrate `type: sophos` config, and have the interactive tutorial copy the profile.
- **s09-14 Documentation.** `docs/collectors/rest.md` (reference) and a tutorial: write a profile for a fictional API, test-first.

## As built

What changed from this design while it was built:

- **Retries.** `rate_limit` has `per_second` and `max_retries` only. `Retry-After` is always honoured, so there is no `retry_after` key.
- **Redirects** are followed only on the same host, at most 5 times, and refused otherwise (`rest.redirect_refused`).
- **Identities are hashed before extraction**, so datasets and checks see the hashed values, the same as the evidence.
- **Results.** A check gives pass, fail, partial (only for a ratio with `partial_at`), not applicable or unable to assess. `when_empty` has no default.
- **Sophos as a profile.** The `sophos-central` profile matched the coded collector on six recordings (same check IDs, outcomes and evidence items), so the coded collector was removed and `type: sophos` now fails validation, naming the replacement. The profile needed no datasets: its firmware check reads the `firewalls` query directly, and the Super Admin count is the `sophos.super_admin_count` code check over `admins` and `roles`. It supports tenant credentials only; partner and organisation credentials, which the coded collector also read, are not supported.
- **Profile tests** are YAML cases in `<profile>.tests/` with recordings beside them, run offline by `hound profile test`. A profile without tests runs with a warning, or is unable to assess when the instance sets `require_profile_tests`.
- **Adding a collector.** `hound add collector rest --set profile=<name>` asks for the profile's required parameters and secrets.
- **Manifest.** The run's `_manifest.json` lists each profile that ran, with its version, source and SHA-256.
