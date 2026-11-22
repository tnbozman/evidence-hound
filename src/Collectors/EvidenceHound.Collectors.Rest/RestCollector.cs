using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest;

/// <summary>The built-in <c>rest</c> collector: reads any REST API described by a profile in the organisation's profiles folder.</summary>
public sealed class RestCollector : ICollector, IInstanceValidator
{
    public const string TypeName = "rest";

    public string Type => TypeName;

    public string Version => "1.0.0";

    public CollectorDescription Describe() => throw new NotImplementedException();

    public Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken) => throw new NotImplementedException();

    public IReadOnlyList<Problem> ValidateInstance(CollectorSettings settings, IReadOnlyCollection<string> secrets) => throw new NotImplementedException();
}
