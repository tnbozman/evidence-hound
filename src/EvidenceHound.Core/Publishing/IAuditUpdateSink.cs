using EvidenceHound.Core.Planning;

namespace EvidenceHound.Core.Publishing;

/// <summary>Where planned audit updates go: straight to the CISO Assistant API, or through the dispatcher's Kafka topic.</summary>
public interface IAuditUpdateSink
{
    Task<PublishReport> SendAsync(IReadOnlyList<AuditUpdate> updates, CancellationToken cancellationToken);
}
