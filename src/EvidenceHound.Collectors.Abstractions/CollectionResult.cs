using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Packaging;
using EvidenceHound.Core.Planning;

namespace EvidenceHound.Collectors;

/// <summary>
/// What a collector found. Check results cite evidence by <see cref="CollectedEvidence.FileName"/>; the runner hashes the files and
/// turns those names into evidence IDs.
/// </summary>
public sealed record CollectionResult(IReadOnlyList<CheckResult> Checks, IReadOnlyList<CollectedEvidence> Files, IReadOnlyList<ControlStatus> ControlStatuses);

/// <summary>One evidence file, read as a stream: a plain file name, its media type and a way to read its content. The runner disposes each stream it opens.</summary>
public sealed record CollectedEvidence(string FileName, string MediaType, Func<Stream> Open)
{
    /// <summary>A file whose content is already in memory.</summary>
    public static CollectedEvidence FromBytes(string fileName, string mediaType, ReadOnlyMemory<byte> content) =>
        new(fileName, mediaType, () => new MemoryStream(content.ToArray(), writable: false));

    /// <summary>Opens the file once and reads all of it into memory.</summary>
    public async Task<CollectedFile> ReadAsync(CancellationToken cancellationToken)
    {
        await using var content = Open();
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return new CollectedFile(FileName, MediaType, buffer.ToArray());
    }
}
