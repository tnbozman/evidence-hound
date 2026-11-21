namespace EvidenceHound.Collectors.Testing;

/// <summary>
/// What a contract test hands a collector: its settings, its secret values and its HTTP clients. A declared secret missing from
/// <see cref="Secrets"/> gets a distinctive made-up value, so the secret-logging contract can look for it.
/// </summary>
public sealed record CollectorScenario
{
    /// <summary>Plain settings by config key.</summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();

    /// <summary>Secret values by config key. Use long, distinctive values: the kit fails a test if one appears in a log.</summary>
    public IReadOnlyDictionary<string, string> Secrets { get; init; } = new Dictionary<string, string>();

    /// <summary>HTTP clients, usually <see cref="RecordedApi.HttpClients"/>. The default makes no requests: each one fails.</summary>
    public IHttpClientFactory HttpClients { get; init; } = OfflineHttpClients.Instance;
}
