using EvidenceHound.Collectors.Testing;

namespace Hound.Template.Collector.Tests;

/// <summary>Recorded TemplateStem responses (synthetic, never real data) and the scenario that points the collector at them.</summary>
internal static class Recorded
{
    public static string Status { get; } = Path.Combine(AppContext.BaseDirectory, "recorded", "status.json");

    public static CollectorScenario Scenario(RecordedApi api) => new()
    {
        Settings = new Dictionary<string, string> { ["base_url"] = api.Url.ToString() },
        Secrets = new Dictionary<string, string> { ["api_token"] = "example-template_type-token-3b9d6e21f0a4" },
        HttpClients = api.HttpClients,
    };
}
