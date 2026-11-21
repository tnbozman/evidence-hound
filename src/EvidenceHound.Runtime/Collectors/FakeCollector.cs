using EvidenceHound.Core.Collecting;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Runtime.Collectors;

/// <summary>
/// The built-in <c>fake</c> collector: fixed, fictional results for tests, demos and dry runs. It reports MFA and backup checks
/// that match <c>mappings/example.yaml</c>, one JSON evidence file per check, and two control statuses.
/// </summary>
public sealed class FakeCollector : ICollector
{
    public string Version => "1.0.0";

    public Task<Result<CollectorOutput>> CollectAsync(CollectorContext context, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
