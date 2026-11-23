using EvidenceHound.Core;
using EvidenceHound.Core.Secrets;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Collectors;

/// <summary>
/// Everything a collector is given for one run: its instance name (the <c>Source</c> of its check results), its settings,
/// its resolved secrets, the clock, a logger and HTTP clients. Nothing else.
/// </summary>
public sealed record CollectorContext(
    string Name,
    CollectorSettings Settings,
    IReadOnlyDictionary<string, Secret> Secrets,
    IClock Clock,
    ILogger Logger,
    IHttpClientFactory HttpClients)
{
    /// <summary>What the capability map routes to other products; none without a map.</summary>
    public CollectorCapabilities Capabilities { get; init; } = CollectorCapabilities.All;

    /// <summary>The instance's earlier evidence files; none outside an evidence store.</summary>
    public IEvidenceHistory History { get; init; } = NoEvidenceHistory.Instance;
}

/// <summary>A collector instance's plain settings, by config key.</summary>
public sealed class CollectorSettings(IReadOnlyDictionary<string, string> values)
{
    /// <summary>The setting's value, or null when it is not set.</summary>
    public string? Find(string name) => values.GetValueOrDefault(name);

    public string GetOrDefault(string name, string fallback) => values.GetValueOrDefault(name, fallback);
}
