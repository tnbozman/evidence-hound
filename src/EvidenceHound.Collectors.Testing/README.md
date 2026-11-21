# EvidenceHound.Collectors.Testing

The contract tests every Evidence Hound collector must pass, and helpers for testing a collector against recorded responses from its target system.

## Apply the contract

Derive a public test class from `CollectorContractTests<TCollector>` in your collector's test project:

```csharp
public class WidgetCollectorContractTests : CollectorContractTests<WidgetCollector>, IDisposable
{
    private readonly RecordedApi _working = RecordedApi.Start().RespondWithFile("GET", "/api/widgets", "recorded/widgets.json");
    private readonly RecordedApi _refusing = RecordedApi.Start().Respond("GET", "/api/widgets", 401, "{\"detail\":\"Invalid token.\"}");

    protected override WidgetCollector CreateCollector() => new();

    protected override CollectorScenario Working => new() { HttpClients = _working.HttpClients };

    protected override CollectorScenario? RefusedCredentials => new() { HttpClients = _refusing.HttpClients };

    public void Dispose()
    {
        _working.Dispose();
        _refusing.Dispose();
    }
}
```

| Contract | The collector must |
| --- | --- |
| `DescribesAtLeastOneCheck` | List at least one check ID in `Describe()`. |
| `EmitsOnlyDeclaredCheckIds` | Report only check IDs that `Describe()` lists. |
| `EvidenceFilesCanBeHashed` | Emit evidence with plain, unique file names that reads the same each time, and cite only files it emitted. |
| `HonoursCancellation` | Throw `OperationCanceledException` when its token is already cancelled. |
| `ReturnsFailNotThrowsWhenCredentialsAreRefused` | Return `Result.Fail` with a problem, not throw, when the target system refuses its credentials. Collectors that declare no secrets pass this contract automatically. |
| `NeverLogsSecrets` | Never write a secret's value to a log message, a structured value, a scope or an exception. |

## Scenarios

A `CollectorScenario` is what the collector is given: settings, secret values and HTTP clients. `Working` defaults to no settings, no secrets and no network (`OfflineHttpClients`: every request fails). A collector that declares secrets must override `RefusedCredentials`. Declared secrets that a scenario leaves out get a distinctive made-up value, so `NeverLogsSecrets` always has something to look for.

## Recorded responses

`RecordedApi` is a local WireMock.Net server. `RespondWithFile` replays a recorded response file, `Respond` an inline body with any status, and requests with no recorded response get 404. `HttpClients` gives clients whose base address is the server, so the collector can use relative paths. Use `Server` for anything else WireMock.Net can do. Recorded responses must be synthetic: no real hosts, names or data.
