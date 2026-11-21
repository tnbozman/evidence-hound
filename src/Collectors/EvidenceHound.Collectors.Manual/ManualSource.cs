using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Manual;

/// <summary>The config keys of a manual source.</summary>
public static class ManualSettings
{
    public const string DropFolder = "drop_folder";

    public const string Patterns = "patterns";

    public const string Checks = "checks";

    public const string DueDays = "due_days";

    public const string Owner = "owner";
}

/// <summary>
/// One manual source, read from a collector instance's settings: the folder export files are dropped in, the file patterns it
/// accepts, the checks it supports, how often fresh evidence is due and who owns it.
/// </summary>
public sealed record ManualSource(string DropFolder, IReadOnlyList<string> Patterns, IReadOnlyList<CheckId> Checks, int DueDays, string Owner)
{
    public static Result<ManualSource> FromSettings(CollectorSettings settings) => throw new NotImplementedException();

    /// <summary>Whether <paramref name="fileName"/> matches one of the accepted patterns. Attestation sidecars never match.</summary>
    public bool Accepts(string fileName) => throw new NotImplementedException();
}
