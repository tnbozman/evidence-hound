using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Planning;

/// <summary>A row's state as a short fingerprint, and the marker that records it, with the run that wrote it, at the end of the observation. Pure.</summary>
public static partial class RowState
{
    /// <summary>
    /// Fingerprint of the result, each check's outcome and each cited evidence's hash (as <c>ref_id=sha256</c>). Order does not matter;
    /// timestamps and observation wording are not part of it.
    /// </summary>
    public static string Fingerprint(ComplianceResult result, IEnumerable<string> checkOutcomes, IEnumerable<string> evidenceHashes)
    {
        ArgumentNullException.ThrowIfNull(checkOutcomes);
        ArgumentNullException.ThrowIfNull(evidenceHashes);
        var text = string.Join('\n', [Names.Of(result), .. checkOutcomes.Order(StringComparer.Ordinal), "--", .. evidenceHashes.Order(StringComparer.Ordinal)]);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    /// <summary>The observation with the marker line appended.</summary>
    public static string Mark(string observation, string runId, string fingerprint) => $"{observation}\n[hound run {runId} state {fingerprint}]";

    /// <summary>The run ID and fingerprint of the marker an observation ends with, or null when it has none.</summary>
    public static RowMark? ReadMark(string? observation) =>
        observation is not null && MarkPattern().Match(observation) is { Success: true } match
            ? new RowMark(match.Groups["run"].Value, match.Groups["state"].Value)
            : null;

    [GeneratedRegex(@"\[hound run (?<run>[^\s\]]+) state (?<state>[0-9a-f]{16})\]\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex MarkPattern();
}

/// <summary>The run that last wrote a row, and the state fingerprint it wrote.</summary>
public sealed record RowMark(string RunId, string Fingerprint);
