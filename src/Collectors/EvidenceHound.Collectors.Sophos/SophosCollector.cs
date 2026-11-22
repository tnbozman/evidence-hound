using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>The built-in <c>sophos</c> collector.</summary>
public sealed class SophosCollector : ICollector
{
    public const string TypeName = "sophos";

    public string Type => TypeName;

    public string Version => "1.0.0";

    public CollectorDescription Describe() => new([], [], []);

    public Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken) => throw new NotImplementedException();
}
