using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Manual;

/// <summary>
/// The built-in <c>manual</c> collector, for systems without an API. Each run reads the drop folder, emits the files within the
/// due interval as evidence, and reports each configured check from the newest file's attestation.
/// </summary>
public sealed class ManualCollector : ICollector
{
    public const string TypeName = "manual";

    public string Type => TypeName;

    public string Version => "1.0.0";

    public CollectorDescription Describe() => throw new NotImplementedException();

    public Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken) => throw new NotImplementedException();
}
