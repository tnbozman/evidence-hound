using System.Security.Cryptography;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Publishing;

/// <summary>Reads an evidence file and checks it still matches the SHA-256 recorded when it was collected.</summary>
public static class EvidenceContent
{
    public static async Task<Result<ReadOnlyMemory<byte>>> ReadVerifiedAsync(IEvidenceFiles files, Evidence evidence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(evidence);
        return (await files.ReadAsync(evidence, cancellationToken)).Bind(content => Verified(evidence, content));
    }

    private static Result<ReadOnlyMemory<byte>> Verified(Evidence evidence, ReadOnlyMemory<byte> content) =>
        Convert.ToHexStringLower(SHA256.HashData(content.Span)) == evidence.Sha256
            ? Result.Ok(content)
            : Result.Fail<ReadOnlyMemory<byte>>(new Problem("evidence.hash_mismatch", $"Evidence {evidence.Id} ({evidence.FileName}) does not match its recorded SHA-256; it was changed after collection."));
}
