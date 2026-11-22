using System.CommandLine;
using System.Globalization;
using EvidenceHound.Collectors.Rest;
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

        return new Command("profile", "Work with REST profiles in the profiles folder.") { validate };
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
