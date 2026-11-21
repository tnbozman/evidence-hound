using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Packaging;

/// <summary>Builds and serialises evidence manifests. Pure.</summary>
public static class Manifests
{
    /// <summary>The manifest's file name in a run folder.</summary>
    public const string FileName = "_manifest.json";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// One entry per file, sorted by file name, with its size and SHA-256. File names must be plain, unique and not start with
    /// <c>_</c>, which is kept for the store's own files.
    /// </summary>
    public static Result<EvidenceManifest> Build(string collector, string collectorVersion, DateTimeOffset collectedAt, IReadOnlyList<CollectedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var names = files.Select(f => f.FileName).ToArray();
        Problem[] problems =
        [
            .. names.Where(name => !IsPlain(name)).Select(name => new Problem("evidence.file_name_invalid", $"Evidence file name '{name}' must be a plain file name, not a path.")),
            .. names.Where(name => IsPlain(name) && name.StartsWith('_')).Select(name => new Problem("evidence.file_name_reserved", $"Evidence file name '{name}' starts with '_', which is reserved for the evidence store.")),
            .. names.GroupBy(name => name, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => new Problem("evidence.file_name_duplicate", $"Evidence file name '{g.Key}' is used more than once.")),
        ];

        return problems.Length > 0
            ? Result.Fail<EvidenceManifest>(problems)
            : Result.Ok(new EvidenceManifest(
                collector,
                collectorVersion,
                collectedAt,
                [.. files.OrderBy(f => f.FileName, StringComparer.Ordinal).Select(f => Entry(f, collectedAt))]));
    }

    /// <summary>Indented JSON with snake_case keys; the same manifest always gives the same text.</summary>
    public static string ToJson(EvidenceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new JsonObject
        {
            ["collector"] = manifest.Collector,
            ["collector_version"] = manifest.CollectorVersion,
            ["collected_at"] = manifest.CollectedAt,
            ["files"] = new JsonArray([.. manifest.Files.Select(f => new JsonObject
            {
                ["file_name"] = f.FileName,
                ["size_bytes"] = f.SizeBytes,
                ["sha256"] = f.Sha256,
                ["media_type"] = f.MediaType,
                ["collected_at"] = f.CollectedAt,
            })]),
        }.ToJsonString(Indented);
    }

    private static ManifestEntry Entry(CollectedFile file, DateTimeOffset collectedAt) =>
        new(file.FileName, file.Content.Length, Convert.ToHexStringLower(SHA256.HashData(file.Content.Span)), file.MediaType, collectedAt);

    private static bool IsPlain(string name) =>
        !string.IsNullOrWhiteSpace(name) && name is not ("." or "..") && name.IndexOfAny(['/', '\\']) < 0;
}
