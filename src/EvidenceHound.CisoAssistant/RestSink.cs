using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;

namespace EvidenceHound.CisoAssistant;

/// <summary>The default transport: publishes straight to the CISO Assistant REST API.</summary>
public sealed class RestSink(ICisoAssistantClient client, IEvidenceFiles files) : IAuditUpdateSink
{
    public Task<PublishReport> SendAsync(IReadOnlyList<AuditUpdate> updates, CancellationToken cancellationToken) =>
        new Publisher(client, files).PublishAsync(updates, cancellationToken);
}
