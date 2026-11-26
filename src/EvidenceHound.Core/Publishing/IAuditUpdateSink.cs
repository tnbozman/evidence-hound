using EvidenceHound.Core.Planning;

namespace EvidenceHound.Core.Publishing;

/// <summary>Where planned audit updates go: the CISO Assistant API, or a test double.</summary>
public interface IAuditUpdateSink
{
    Task<PublishReport> SendAsync(IReadOnlyList<AuditUpdate> updates, CancellationToken cancellationToken);
}
