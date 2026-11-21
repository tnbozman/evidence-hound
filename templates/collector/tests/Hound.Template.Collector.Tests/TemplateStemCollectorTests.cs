using EvidenceHound.Collectors;
using EvidenceHound.Collectors.Testing;
using EvidenceHound.Core;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hound.Template.Collector.Tests;

public sealed class TemplateStemCollectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Healthy_status_passes_and_cites_the_response()
    {
        using var api = RecordedApi.Start().RespondWithFile("GET", "/api/status", Recorded.Status);

        var result = Collected(await new TemplateStemCollector().CollectAsync(Context(api), TestContext.Current.CancellationToken));

        var check = Assert.Single(result.Checks);
        Assert.Equal(TemplateStemCollector.HealthyCheck, check.CheckId.Value);
        Assert.Equal(CheckOutcome.Pass, check.Outcome);
        Assert.Equal(["status.json"], check.EvidenceRefs);
        Assert.Equal(Now, check.CollectedAt);
    }

    [Fact]
    public async Task Unhealthy_status_fails()
    {
        using var api = RecordedApi.Start().Respond("GET", "/api/status", 200, "{\"healthy\":false}");

        var result = Collected(await new TemplateStemCollector().CollectAsync(Context(api), TestContext.Current.CancellationToken));

        Assert.Equal(CheckOutcome.Fail, Assert.Single(result.Checks).Outcome);
    }

    private static CollectionResult Collected(Result<CollectionResult> result) =>
        result.Match(value => value, problems => throw new InvalidOperationException(string.Join("; ", problems.Select(p => p.Message))));

    private static CollectorContext Context(RecordedApi api)
    {
        var scenario = Recorded.Scenario(api);
        return new(
            "example-template_type",
            new CollectorSettings(scenario.Settings),
            scenario.Secrets.ToDictionary(pair => pair.Key, pair => new Secret(pair.Value)),
            new FixedClock(Now),
            NullLogger.Instance,
            scenario.HttpClients);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
