using System.Globalization;
using System.IO.Enumeration;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using static EvidenceHound.Collectors.Manual.ResultProblems;

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
    private static readonly string Invalid = "manual.setting_invalid";

    public static Result<ManualSource> FromSettings(CollectorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var folder = Required(settings, ManualSettings.DropFolder);
        var checks = Required(settings, ManualSettings.Checks).Bind(ParseChecks);
        var dueDays = Required(settings, ManualSettings.DueDays).Bind(ParseDueDays);
        var owner = Required(settings, ManualSettings.Owner);
        var patterns = List(settings.GetOrDefault(ManualSettings.Patterns, "*")) is [_, ..] some ? some : ["*"];

        Problem[] problems = [.. ProblemsOf(folder), .. ProblemsOf(checks), .. ProblemsOf(dueDays), .. ProblemsOf(owner)];
        return problems.Length > 0
            ? Result.Fail<ManualSource>(problems)
            : folder.Bind(f => checks.Bind(c => dueDays.Bind(d => owner.Map(o => new ManualSource(f, patterns, c, d, o)))));
    }

    /// <summary>Whether <paramref name="fileName"/> matches one of the accepted patterns (<c>*</c> and <c>?</c>). Attestation sidecars and proposals never match.</summary>
    public bool Accepts(string fileName) =>
        !fileName.EndsWith(Attestation.Suffix, StringComparison.OrdinalIgnoreCase)
        && !fileName.EndsWith(Proposal.Suffix, StringComparison.OrdinalIgnoreCase)
        && Patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true));

    private static Result<string> Required(CollectorSettings settings, string name) =>
        settings.Find(name) is { Length: > 0 } value
            ? Result.Ok(value.Trim())
            : Result.Fail<string>(new Problem("manual.setting_missing", $"Setting '{name}' is required."));

    private static Result<IReadOnlyList<CheckId>> ParseChecks(string text) =>
        List(text) is [_, ..] ids
            ? Result.Combine(ids.Select(id => CheckId.Create(id).Match(
                Result.Ok,
                _ => Result.Fail<CheckId>(new Problem(Invalid, $"Setting '{ManualSettings.Checks}': '{id}' is not a check ID; use lowercase dot-separated segments such as network.firewall.rules_reviewed.")))))
            : Result.Fail<IReadOnlyList<CheckId>>(new Problem(Invalid, $"Setting '{ManualSettings.Checks}' must list at least one check ID."));

    private static Result<int> ParseDueDays(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days > 0
            ? Result.Ok(days)
            : Result.Fail<int>(new Problem(Invalid, $"Setting '{ManualSettings.DueDays}' must be a whole number of days greater than zero, not '{text}'."));

    private static string[] List(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
