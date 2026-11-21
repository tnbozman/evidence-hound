using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;

namespace EvidenceHound.Transport.Kafka;

/// <summary>Writes audit updates as CISO Assistant dispatcher messages to Kafka. Not built yet.</summary>
public sealed class KafkaSink(KafkaSinkOptions options, IEvidenceFiles files) : IAuditUpdateSink, IDisposable
{
    public Task<PublishReport> SendAsync(IReadOnlyList<AuditUpdate> updates, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{options}{files}");

    public void Dispose()
    {
    }
}

/// <summary>Where the dispatcher's Kafka is. The dispatcher reads the <c>observation</c> topic.</summary>
public sealed record KafkaSinkOptions(string BootstrapServers, string Topic = "observation");
