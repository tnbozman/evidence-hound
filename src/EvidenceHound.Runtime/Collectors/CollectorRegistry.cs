using EvidenceHound.Collectors;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Runtime.Collectors;

/// <summary>The collectors registered in DI, by type. Built once at startup; two collectors with the same type are a startup error.</summary>
public sealed class CollectorRegistry
{
    private CollectorRegistry(IReadOnlyDictionary<string, ICollector> byType) => ByType = byType;

    public IReadOnlyDictionary<string, ICollector> ByType { get; }

    /// <summary>What each type describes, for config validation.</summary>
    public IReadOnlyDictionary<string, CollectorDescription> Descriptions => throw new NotImplementedException();

    /// <summary>A registry of every <see cref="ICollector"/> registered in <paramref name="services"/>, or <c>collector.type_duplicate</c>.</summary>
    public static Result<CollectorRegistry> FromServices(IServiceProvider services) => throw new NotImplementedException();
}
