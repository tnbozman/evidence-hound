using EvidenceHound.Collectors.Testing;

namespace Hound.Template.Collector.Tests;

/// <summary>The shared contract every collector must pass, run against recorded TemplateStem responses.</summary>
public sealed class TemplateStemCollectorContractTests : CollectorContractTests<TemplateStemCollector>, IDisposable
{
    private readonly RecordedApi _working = RecordedApi.Start().RespondWithFile("GET", "/api/status", Recorded.Status);

    private readonly RecordedApi _refusing = RecordedApi.Start().Respond("GET", "/api/status", 401, "{\"detail\":\"Invalid token.\"}");

    protected override CollectorScenario Working => Recorded.Scenario(_working);

    protected override CollectorScenario? RefusedCredentials => Recorded.Scenario(_refusing);

    public void Dispose()
    {
        _working.Dispose();
        _refusing.Dispose();
    }

    protected override TemplateStemCollector CreateCollector() => new();
}
