using EvidenceHound.Collectors;
using EvidenceHound.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace EvidenceHound.Runtime.Collectors;

/// <summary>The collectors registered in DI, by type. Built once at startup; two collectors with the same type are a startup error.</summary>
public sealed class CollectorRegistry
{
    private CollectorRegistry(IReadOnlyDictionary<string, ICollector> byType)
    {
        ByType = byType;
        Descriptions = byType.ToDictionary(pair => pair.Key, pair => pair.Value.Describe(), StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, ICollector> ByType { get; }

    /// <summary>What each type describes, for config validation.</summary>
    public IReadOnlyDictionary<string, CollectorDescription> Descriptions { get; }

    /// <summary>A registry of every <see cref="ICollector"/> registered in <paramref name="services"/>, or <c>collector.type_duplicate</c>.</summary>
    public static Result<CollectorRegistry> FromServices(IServiceProvider services) => Create([.. services.GetServices<ICollector>()]);

    private static Result<CollectorRegistry> Create(IReadOnlyList<ICollector> collectors)
    {
        var duplicates = collectors
            .GroupBy(c => c.Type, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => new Problem(
                "collector.type_duplicate",
                $"Collector type '{g.Key}' is registered more than once: {string.Join(", ", g.Select(c => c.GetType().Name))}."))
            .ToArray();
        return duplicates.Length > 0
            ? Result.Fail<CollectorRegistry>(duplicates)
            : Result.Ok(new CollectorRegistry(collectors.ToDictionary(c => c.Type, StringComparer.Ordinal)));
    }
}
