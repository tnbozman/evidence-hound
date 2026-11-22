using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>One check of one test case: the result the case expects and the result the profile gave.</summary>
public sealed record CheckComparison(string CheckId, string Expected, string Actual, string Observation)
{
    public bool Passed => Expected == Actual;
}

/// <summary>One test case of a profile, and any request the profile made that the case has no recording for.</summary>
public sealed record CaseReport(string Name, string Description, IReadOnlyList<CheckComparison> Checks, IReadOnlyList<string> Unrecorded)
{
    public bool Passed => Unrecorded.Count == 0 && Checks.All(c => c.Passed);
}

/// <summary>
/// Runs a profile offline against each test case in <c>&lt;profile&gt;.tests/</c>: the real rest collector, with the case's settings,
/// fake secrets and clock, answered from recordings instead of the network. Nothing is sent anywhere.
/// </summary>
public static class ProfileTestRunner
{
    public const string FolderSuffix = ".tests";

    public static string TestsFolder(string profilesFolder, string name) => Path.Combine(profilesFolder, name + FolderSuffix);

    public static bool HasTests(string profilesFolder, string name) =>
        Directory.Exists(TestsFolder(profilesFolder, name)) && Directory.EnumerateFiles(TestsFolder(profilesFolder, name), "*.yaml").Any();

    public static async Task<Result<IReadOnlyList<CaseReport>>> RunAsync(string profilesFolder, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profilesFolder);
        ArgumentNullException.ThrowIfNull(name);
        var loaded = ProfileFolder.Load(profilesFolder, name);
        if (loaded is not Result<LoadedProfile>.Ok { Value.Profile: var profile })
        {
            return Result.Fail<IReadOnlyList<CaseReport>>(((Result<LoadedProfile>.Fail)loaded).Errors);
        }

        if (!HasTests(profilesFolder, name))
        {
            return Result.Fail<IReadOnlyList<CaseReport>>(Missing(name));
        }

        var folder = TestsFolder(profilesFolder, name);
        var cases = Result.Combine(Directory.EnumerateFiles(folder, "*.yaml").Order(StringComparer.Ordinal)
            .Select(file => ProfileTestCase.Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file)).Bind(c => Check(c, profile, folder))));
        if (cases is not Result<IReadOnlyList<ProfileTestCase>>.Ok { Value: var valid })
        {
            return Result.Fail<IReadOnlyList<CaseReport>>(((Result<IReadOnlyList<ProfileTestCase>>.Fail)cases).Errors);
        }

        var reports = new List<CaseReport>();
        foreach (var testCase in valid)
        {
            reports.Add(await RunCaseAsync(profilesFolder, profile, testCase, folder, cancellationToken).ConfigureAwait(false));
        }

        return Result.Ok<IReadOnlyList<CaseReport>>(reports);
    }

    /// <summary>The problem for a profile with no test cases.</summary>
    public static Problem Missing(string name) =>
        new("profile.tests_missing", $"Profile '{name}' has no tests: add {name}{FolderSuffix}/ with recordings and expected results, then run hound profile test {name}.");

    /// <summary>The name a profile test uses for a check's result.</summary>
    public static string ResultName(CheckOutcome outcome) => outcome switch
    {
        CheckOutcome.Error => "unable_to_assess",
        _ => Core.Mapping.Names.Of(outcome),
    };

    private static Result<ProfileTestCase> Check(ProfileTestCase testCase, Profile profile, string folder)
    {
        var ids = profile.Checks.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var problems = testCase.Expect.Keys.Where(id => !ids.Contains(id))
            .Select(id => $"expect names '{id}', which is not a check of profile '{profile.Name}'.")
            .Concat(testCase.Responses.Where(r => r.File is { } f && !File.Exists(Path.Combine(folder, f))).Select(r => $"the recording '{r.File}' does not exist."))
            .ToArray();
        return problems.Length == 0
            ? Result.Ok(testCase)
            : Result.Fail<ProfileTestCase>(new Problem("profile.test_invalid", $"Test case '{testCase.Name}': {string.Join(" ", problems)}"));
    }

    private static async Task<CaseReport> RunCaseAsync(string profilesFolder, Profile profile, ProfileTestCase testCase, string folder, CancellationToken cancellationToken)
    {
        using var handler = new ReplayHandler(testCase.Responses, folder);
        var settings = new Dictionary<string, string>(testCase.Settings, StringComparer.Ordinal)
        {
            [RestSettings.Profile] = profile.Name,
            [RestSettings.ProfilesPath] = profilesFolder,
        };
        var secrets = testCase.Secrets.ToDictionary(p => p.Key, p => new Secret(p.Value), StringComparer.Ordinal);
        var context = new CollectorContext($"{profile.Name}-test", new CollectorSettings(settings), secrets, new FixedClock(testCase.Now), NullLogger.Instance, new ReplayClients(handler));
        var result = await new RestCollector(NoDelay.Instance).CollectAsync(context, cancellationToken).ConfigureAwait(false);
        var checks = result.Match(r => r.Checks, _ => []);
        var failure = result.Match(_ => "", problems => string.Join("; ", problems.Select(p => $"{p.Code}: {p.Message}")));
        var comparisons = testCase.Expect.Select(e => checks.FirstOrDefault(c => c.CheckId.Value == e.Key) is { } actual
                ? new CheckComparison(e.Key, e.Value, ResultName(actual.Outcome), actual.Observation)
                : new CheckComparison(e.Key, e.Value, "missing", failure))
            .ToArray();
        return new CaseReport(testCase.Name, testCase.Description, comparisons, [.. handler.Unrecorded]);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class NoDelay : IDelay
    {
        public static NoDelay Instance { get; } = new();

        public Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ReplayClients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
