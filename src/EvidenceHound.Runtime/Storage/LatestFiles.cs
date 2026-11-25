using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Runtime.Storage;

/// <summary>
/// s14-05: evidence content from this run, or else from the stored run of the collector whose latest results cite it, for evidence
/// CISO Assistant does not hold yet (say, its own run could not publish).
/// </summary>
public sealed class LatestFiles(IEvidenceFiles run, IReadOnlyDictionary<string, string> locations) : IEvidenceFiles
{
    /// <summary>This run's files, falling back to the stored runs of <paramref name="others"/>.</summary>
    public static IEvidenceFiles Over(IEvidenceFiles run, IReadOnlyList<LatestResults> others)
    {
        ArgumentNullException.ThrowIfNull(others);
        return others.Count == 0 ? run : new LatestFiles(run, others
            .SelectMany(o => o.Results.Evidence.Select(e => (e.Id, o.Location)))
            .DistinctBy(e => e.Id)
            .ToDictionary(e => e.Id, e => e.Location, StringComparer.Ordinal));
    }

    public async Task<Result<ReadOnlyMemory<byte>>> ReadAsync(Evidence evidence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var own = await run.ReadAsync(evidence, cancellationToken);
        if (own is Result<ReadOnlyMemory<byte>>.Ok || !locations.TryGetValue(evidence.Id, out var location))
        {
            return own;
        }

        var path = Path.Combine(location, evidence.FileName);
        return Path.GetFileName(evidence.FileName) == evidence.FileName && File.Exists(path)
            ? Result.Ok<ReadOnlyMemory<byte>>(await File.ReadAllBytesAsync(path, cancellationToken))
            : Result.Fail<ReadOnlyMemory<byte>>(new Problem("evidence.file_not_found", $"Evidence {evidence.Id} is no longer stored in {location}."));
    }
}
