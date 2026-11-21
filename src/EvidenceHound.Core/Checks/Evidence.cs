using System.Text.RegularExpressions;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Checks;

/// <summary>A file a collector captured as proof, identified by its SHA-256 hash.</summary>
public sealed partial record Evidence
{
    private Evidence(string id, string fileName, string mediaType, string sha256, long sizeBytes, DateTimeOffset collectedAt)
    {
        Id = id;
        FileName = fileName;
        MediaType = mediaType;
        Sha256 = sha256;
        SizeBytes = sizeBytes;
        CollectedAt = collectedAt;
    }

    public string Id { get; }

    public string FileName { get; }

    public string MediaType { get; }

    /// <summary>Lowercase hex SHA-256 of the file content.</summary>
    public string Sha256 { get; }

    public long SizeBytes { get; }

    public DateTimeOffset CollectedAt { get; }

    public static Result<Evidence> Create(string id, string fileName, string mediaType, string sha256, long sizeBytes, DateTimeOffset collectedAt)
    {
        ArgumentNullException.ThrowIfNull(sha256);

        var problems = new List<Problem>();
        if (string.IsNullOrWhiteSpace(id))
        {
            problems.Add(new Problem("evidence.id_empty", "Evidence ID must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            problems.Add(new Problem("evidence.file_name_empty", $"Evidence {id} needs a file name."));
        }

        if (string.IsNullOrWhiteSpace(mediaType))
        {
            problems.Add(new Problem("evidence.media_type_empty", $"Evidence {id} needs a media type."));
        }

        if (!Sha256Pattern().IsMatch(sha256))
        {
            problems.Add(new Problem("evidence.sha256_invalid", $"Evidence {id} SHA-256 must be 64 hex characters."));
        }

        if (sizeBytes < 0)
        {
            problems.Add(new Problem("evidence.size_negative", $"Evidence {id} size must not be negative."));
        }

        return problems.Count == 0
            ? Result.Ok(new Evidence(id, fileName, mediaType, sha256.ToLowerInvariant(), sizeBytes, collectedAt))
            : Result.Fail<Evidence>(problems);
    }

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
