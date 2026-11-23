using System.CommandLine;
using System.Globalization;
using EvidenceHound.Collectors.Rest;
using EvidenceHound.Collectors.Rest.Engine;
using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound profile</c>: work with the REST profiles in the organisation's profiles folder.</summary>
internal static class ProfileCommands
{
    public const string ProfilesSetting = "HOUND_PROFILES_PATH";

    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration)
    {
        var logger = loggers.CreateLogger("hound.profile");
        var validate = new Command("validate", "Check a profile in the profiles folder and list every problem with its code.") { NameArgument(), FolderOption() };
        validate.SetAction(async (parseResult, cancellationToken) =>
        {
            var folder = Folder(parseResult, configuration);
            var loaded = ProfileFolder.Load(folder, parseResult.GetRequiredValue<string>("name"));
            return await loaded.Match(
                async profile => ProfileValidator.Validate(profile.Profile, RestCollector.Validation) switch
                {
                    [] => await Valid(output, profile.Profile, cancellationToken),
                    var problems => Log.Problems(logger, WithCodes(problems)),
                },
                problems => Task.FromResult(Log.Problems(logger, WithCodes(problems))));
        });

        var test = new Command("test", "Run a profile offline against its recorded test cases (<name>.tests/) and compare each check's result.") { NameArgument(), FolderOption() };
        test.SetAction((parseResult, cancellationToken) =>
            TestAsync(output, logger, Folder(parseResult, configuration), parseResult.GetRequiredValue<string>("name"), cancellationToken));

        var diff = new Command("diff", "Show how a copied profile differs from the library version named in its source.") { NameArgument(), FolderOption(), LibraryOption() };
        diff.SetAction((parseResult, cancellationToken) =>
            DiffAsync(output, logger, Folder(parseResult, configuration), parseResult.GetRequiredValue<string>("name"), Library(parseResult, configuration), cancellationToken));

        return new Command("profile", "Work with REST profiles in the profiles folder.") { validate, test, diff };
    }

    public const string LibrarySetting = "HOUND_LIBRARY_PATH";

    internal static Option<DirectoryInfo> LibraryOption() =>
        new("--library") { Description = $"A checkout of the evidence-hound repository, holding the profile library. Default: {LibrarySetting}." };

    private static string? Library(ParseResult parseResult, IConfiguration configuration) =>
        parseResult.GetValue<DirectoryInfo>("--library")?.FullName ?? configuration[LibrarySetting];

    private static async Task<int> TestAsync(TextWriter output, ILogger logger, string folder, string name, CancellationToken cancellationToken)
    {
        var result = await ProfileTestRunner.RunAsync(folder, name, cancellationToken);
        if (result is not Result<IReadOnlyList<CaseReport>>.Ok { Value: var cases })
        {
            return Log.Problems(logger, WithCodes(((Result<IReadOnlyList<CaseReport>>.Fail)result).Errors));
        }

        foreach (var report in cases)
        {
            await output.WriteLineAsync(Describe(name, report).AsMemory(), cancellationToken);
        }

        return cases.All(c => c.Passed) ? ExitCodes.Ok : ExitCodes.ProfileTestsFailed;
    }

    private static string Describe(string name, CaseReport report)
    {
        var lines = new List<string> { report.Passed ? $"{name}/{report.Name}: passed ({Count(report.Checks.Count, "check", "checks")})" : $"{name}/{report.Name}: FAILED" };
        lines.AddRange(report.Checks.Where(c => !c.Passed).Select(c => $"  {c.CheckId}: expected {c.Expected}, got {c.Actual}. {c.Observation}"));
        lines.AddRange(report.Unrecorded.Select(r => $"  no recording for {r}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<int> DiffAsync(TextWriter output, ILogger logger, string folder, string name, string? library, CancellationToken cancellationToken)
    {
        if (library is null)
        {
            return Log.Problems(logger, [new Problem("profile.library_missing", $"profile.library_missing: give --library <evidence-hound checkout> or set {LibrarySetting}.")]);
        }

        var loaded = ProfileFolder.Load(folder, name);
        if (loaded is not Result<LoadedProfile>.Ok { Value: var local })
        {
            return Log.Problems(logger, WithCodes(((Result<LoadedProfile>.Fail)loaded).Errors));
        }

        if (local.Profile.Source is not { } source)
        {
            return Log.Problems(logger, [new Problem("profile.source_missing", $"profile.source_missing: profile '{name}' has no source, so there is no library version to compare with.")]);
        }

        var file = ProfileDiff.LibraryFile(library, source);
        if (file is not Result<string>.Ok { Value: var path } || !File.Exists(path))
        {
            return Log.Problems(logger, [new Problem("profile.source_not_found", $"profile.source_not_found: '{source}' is not in the library at {library}.")]);
        }

        var libraryText = await File.ReadAllTextAsync(path, cancellationToken);
        var libraryVersion = ProfileParser.Parse(libraryText, path).Match(p => p.Version, _ => "unreadable");
        var changes = ProfileDiff.Compare(libraryText, await File.ReadAllTextAsync(local.Path, cancellationToken)).Where(l => l.Kind != DiffKind.Same).ToArray();
        await output.WriteLineAsync($"{name}: local {local.Profile.Version}, library {libraryVersion} ({source})".AsMemory(), cancellationToken);
        await output.WriteLineAsync((changes.Length == 0 ? "No differences." : string.Join(Environment.NewLine, changes.Select(c => c.ToString()))).AsMemory(), cancellationToken);
        return ExitCodes.Ok;
    }

    internal static Argument<string> NameArgument() => new("name") { Description = "The profile's name, its file name in the profiles folder without .yaml." };

    internal static Option<DirectoryInfo> FolderOption() =>
        new("--profiles") { Description = $"The profiles folder. Default: {ProfilesSetting}, else {ProfileFolder.DefaultPath}." };

    internal static string Folder(ParseResult parseResult, IConfiguration configuration) =>
        parseResult.GetValue<DirectoryInfo>("--profiles")?.FullName ?? configuration[ProfilesSetting] ?? ProfileFolder.DefaultPath;

    private static async Task<int> Valid(TextWriter output, Profile profile, CancellationToken cancellationToken)
    {
        var counts = string.Join(", ", Count(profile.Queries.Count, "query", "queries"), Count(profile.Datasets.Count, "dataset", "datasets"), Count(profile.Checks.Count, "check", "checks"));
        await output.WriteLineAsync($"{profile.Name} {profile.Version}: valid ({counts})".AsMemory(), cancellationToken);
        return ExitCodes.Ok;
    }

    /// <summary>Each problem's message starting with its code, so a profile author can look the code up.</summary>
    internal static IEnumerable<Problem> WithCodes(IEnumerable<Problem> problems) => problems.Select(p => new Problem(p.Code, $"{p.Code}: {p.Message}"));

    private static string Count(int count, string one, string many) => string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? one : many)}");
}
