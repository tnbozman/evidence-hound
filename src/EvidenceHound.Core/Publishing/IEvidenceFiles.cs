using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Publishing;

/// <summary>Reads the content of an evidence file a collector captured.</summary>
public interface IEvidenceFiles
{
    Task<Result<ReadOnlyMemory<byte>>> ReadAsync(Evidence evidence, CancellationToken cancellationToken);
}
