using EvidenceHound.Core.Planning;

namespace EvidenceHound.Core.Publishing;

/// <summary>Sends planned audit updates to CISO Assistant in order. Not built yet.</summary>
public sealed class Publisher(ICisoAssistantClient client, IEvidenceFiles files)
{
    public static PublishReport DryRun(IReadOnlyList<AuditUpdate> updates) => throw new NotImplementedException();

    public Task<PublishReport> PublishAsync(IReadOnlyList<AuditUpdate> updates, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{client}{files}");
}
