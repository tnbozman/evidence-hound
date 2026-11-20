# Engineering guide

## Architecture: functional core, imperative shell

```
            +-------------------------- composition root (CLI / runner) --------------------------+
            |                                                                                     |
 Collector  |  collect (I/O)  ->  evaluate (pure)  ->  map to framework (pure)  ->  plan (pure)   |  publish (I/O)
 adapters --+--> raw facts         CheckResult[]       RequirementResult[]         AuditUpdate[]  +--> CISO Assistant API
            |                                                                                     |
            +-------------------------------------------------------------------------------------+
```

| Layer | Contents | Rules |
| --- | --- | --- |
| Domain (core) | `Evidence`, `CheckResult`, `CheckId`, `RequirementResult`, `ControlStatus`, `AuditUpdate`, `RunRecord`, `FrameworkMapping` | Immutable records. No I/O. No framework or vendor names. |
| Logic (core) | `evaluate`, `applyMapping`, `planUpdates`, `buildManifest`, `summariseRun` | Pure functions. Same input, same output. Property tests where useful. |
| Ports (core) | `ICisoAssistantClient`, `IEvidenceStore`, `ISecretProvider`, `IClock`, `ICollector` | Small interfaces, one purpose each. |
| Adapters | REST client, filesystem store, environment secrets, system clock, vendor collectors | All side effects live here. Thin; tested with contract tests against fakes and recorded responses. |
| Composition root | CLI and scheduled runner | Wires adapters to ports from configuration. The only place that knows concrete types. |

Dependencies point inwards. Collectors reference the core; the core references nothing outside the standard library and its small approved dependency list.

## Framework-neutral checks

Collectors emit stable check IDs such as `identity.mfa.admins` or `backup.restore_tested` with a result (`pass`, `fail`, `partial`, `not_applicable`, `error`), a short observation and evidence references. They never mention Essential Eight or NIST.

A mapping file per framework (`mappings/<framework>.yaml`) says which checks satisfy which requirement and how several checks roll into one result (for example: all must pass for `compliant`, any pass for `partial`). Adding a framework means adding a mapping file, not changing collectors.

## SOLID, applied

| Principle | In this codebase |
| --- | --- |
| Single responsibility | A collector collects. Evaluation, mapping, planning and publishing are separate functions. |
| Open/closed | New systems are new collector packages registered as plugins; new frameworks are new mapping files. The core does not change. |
| Liskov | Every `ICollector` passes the shared collector contract test suite. |
| Interface segregation | Ports are small: a collector never sees the CISO Assistant client; the publisher never sees vendor APIs. |
| Dependency inversion | Core defines ports; adapters implement them; the composition root wires them. |

## Functional style

- Records are immutable; "changes" return new values.
- Expected failures (API refused, bad config, missing evidence) are returned as `Result<T, Error>` values, not thrown. Exceptions are for bugs.
- No shared mutable state, no static singletons, no hidden clock or randomness: pass `IClock` and IDs in.
- Small total functions composed into pipelines. Avoid boolean flags that switch behaviour; use separate functions.
- Validate input at the edge (config, API responses) into domain types once; trust them inside.

## Testing levels

| Level | Scope | Runs | Tooling |
| --- | --- | --- | --- |
| Unit | Pure functions and domain types | Every commit, < 10 s | xUnit v3 + FsCheck (property tests) + Shouldly |
| Contract | Adapters against recorded vendor responses and a fake CISO Assistant | Every commit | WireMock.Net |
| Functional | CLI end to end against a real CISO Assistant container | Every PR in CI; on demand locally | Testcontainers |

Test names describe behaviour: `given_all_admin_accounts_have_mfa_check_passes`. Fixtures are synthetic and fictional. Coverage target: 90 % of core logic lines; adapters covered by contract tests.

## Commits and branches

- One branch per sprint: `sprint/sNN-<slug>`, opened as a draft PR by the Start sprint workflow.
- Commit prefixes follow Conventional Commits with the task ID: `test(s02-03): ... [red]`, `feat(s02-03): ... [green]`, `refactor(s02-03): ...`.
- The sprint PR is merged (squash or merge commit) when every task is ticked and checks are green.
