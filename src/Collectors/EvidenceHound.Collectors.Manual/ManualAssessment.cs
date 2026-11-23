using System.Globalization;
using System.Security.Cryptography;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Manual;

/// <summary>One file found in a drop folder: its content, when it was last written, and its attestation if it has one.</summary>
internal sealed record DroppedFile(string FileName, ReadOnlyMemory<byte> Content, DateTimeOffset ModifiedAt, Attestation? Attestation, ReadOnlyMemory<byte> AttestationContent)
{
    /// <summary>The attestation date, or when the file was written if nobody attested it.</summary>
    public DateTimeOffset DatedAt => Attestation?.Date ?? ModifiedAt;

    public string Sidecar => Attestation.SidecarFor(FileName);

    public string ContentHash => Convert.ToHexStringLower(SHA256.HashData(Content.Span));
}

/// <summary>Pure: turns the files in a drop folder into evidence and one check result per configured check.</summary>
internal static class ManualAssessment
{
    public static Result<CollectionResult> Assess(ManualSource source, IReadOnlyList<DroppedFile> files, DateTimeOffset now, string name)
    {
        // Newest first; the same content dropped twice is evidence once, under its newest name.
        var current = files
            .Where(f => f.DatedAt >= now.AddDays(-source.DueDays))
            .OrderByDescending(f => f.DatedAt).ThenBy(f => f.FileName, StringComparer.Ordinal)
            .DistinctBy(f => f.ContentHash, StringComparer.Ordinal)
            .ToArray();
        var lastSeen = files.Count > 0 ? files.Max(f => f.DatedAt) : (DateTimeOffset?)null;

        var checks = Result.Combine(source.Checks.Select(id =>
        {
            var (outcome, observation, refs) = current is [var newest, ..] ? Present(id, newest) : Missing(source, lastSeen);
            return CheckResult.Create(id, outcome, Shorten(observation), refs, now, name);
        }));

        return checks.Map(c => new CollectionResult(c, [.. current.SelectMany(Evidence)], []));
    }

    private static (CheckOutcome, string, string[]) Present(CheckId id, DroppedFile newest) => newest.Attestation switch
    {
        null => (CheckOutcome.Partial, $"Evidence present, not attested: {newest.FileName}, {Day(newest.ModifiedAt)}.", [newest.FileName]),
        { FileSha256: { } hash } a when !string.Equals(hash, newest.ContentHash, StringComparison.OrdinalIgnoreCase) =>
            (CheckOutcome.Partial, $"Evidence changed since it was attested by {a.Who} on {Day(a.Date)}: {newest.FileName}. Attest the file as it is now.", [newest.FileName, newest.Sidecar]),
        { } a when a.Outcomes.TryGetValue(id.Value, out var attested) =>
            (attested, $"{Attested(a, attested)}: {newest.FileName}{Kept(a)}.{(a.Note is null ? "" : $" {a.Note}")}", [newest.FileName, newest.Sidecar]),
        _ => (CheckOutcome.Partial, $"Evidence present, not attested for this check: {newest.Sidecar} has no outcome for {id.Value}.", [newest.FileName, newest.Sidecar]),
    };

    /// <summary>Who stands behind the outcome: the person who attested it, and the agent when it proposed what they attested.</summary>
    private static string Attested(Attestation a, CheckOutcome outcome) => a.ProposedBy is { } proposer
        ? $"Proposed by {proposer}, attested {Attestation.NameOf(outcome)} by {a.Who} on {Day(a.Date)}"
        : $"Attested {Attestation.NameOf(outcome)} by {a.Who} on {Day(a.Date)}";

    private static string Kept(Attestation a) => a.FileSha256 is { Length: >= 12 } hash ? $" (sha256 {hash[..12]})" : "";

    private static (CheckOutcome, string, string[]) Missing(ManualSource source, DateTimeOffset? lastSeen) => lastSeen switch
    {
        null => (CheckOutcome.Error, $"No evidence uploaded yet; due every {source.DueDays} days from {source.Owner}.", []),
        { } seen => (CheckOutcome.Error, $"Evidence overdue since {Day(seen.AddDays(source.DueDays))}; due every {source.DueDays} days from {source.Owner}.", []),
    };

    private static IEnumerable<CollectedEvidence> Evidence(DroppedFile file) => file.Attestation is null
        ? [CollectedEvidence.FromBytes(file.FileName, MediaType(file.FileName), file.Content)]
        : [CollectedEvidence.FromBytes(file.FileName, MediaType(file.FileName), file.Content), CollectedEvidence.FromBytes(file.Sidecar, "application/yaml", file.AttestationContent)];

    private static string MediaType(string fileName) => Path.GetExtension(fileName).ToUpperInvariant() switch
    {
        ".TXT" or ".CFG" or ".CONF" or ".LOG" => "text/plain",
        ".CSV" => "text/csv",
        ".JSON" => "application/json",
        ".XML" => "application/xml",
        ".YAML" or ".YML" => "application/yaml",
        ".PDF" => "application/pdf",
        _ => "application/octet-stream",
    };

    private static string Day(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Shorten(string observation) =>
        observation.Length <= CheckResult.MaxObservationLength ? observation : string.Concat(observation.AsSpan(0, CheckResult.MaxObservationLength - 1), "…");
}
