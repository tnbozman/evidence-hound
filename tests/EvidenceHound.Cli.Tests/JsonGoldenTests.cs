using System.Text.RegularExpressions;
using EvidenceHound.Runtime;
using EvidenceHound.Runtime.Status;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s19-04: the <c>--json</c> answers an agent reads most, pinned before the command-line shell is refactored, so a refactor that changes
/// one byte of them fails here. Temporary folders and the stub's port are replaced with fixed text. To accept a deliberate change, run
/// the tests with <c>HOUND_UPDATE_GOLDEN=1</c> and review the diff of <c>golden/</c>.
/// </summary>
public sealed partial class JsonGoldenTests : IDisposable
{
    private static readonly DateTimeOffset Ran = new(2027, 2, 14, 16, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Directory.CreateTempSubdirectory("hound-golden-").FullName;
    private readonly ScopeTestInstance _instance = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private string Repo => Path.Combine(_dir, "repo");

    private string Config => Path.Combine(Repo, "hound.yaml");

    [Fact]
    public async Task Next_json_is_unchanged() =>
        await MatchesAsync("next.json", await ExampleAsync(["next", "--repo", Repo, "--config", Config, "--json"]));

    [Fact]
    public async Task Status_json_is_unchanged() =>
        await MatchesAsync("status.json", await ExampleAsync(["status", "--config", Config, "--scope", Path.Combine(Repo, "scope"), "--json"]));

    [Fact]
    public async Task Review_pending_json_is_unchanged()
    {
        // The queue of ReviewPrepareTests: every kind of draft an agent writes, waiting for the person.
        TestFiles.CopyExampleRepo(Repo);
        await File.WriteAllTextAsync(Path.Combine(Repo, "frameworks", "example-framework.json"), OnboardSystemCommandTests.Framework, _ct);
        await File.WriteAllTextAsync(Config, ManualProposeTests.Config, _ct);
        await OnboardAgentCommandTests.DraftEverythingAsync(Repo, _ct);

        var (exit, output, error) = await OnboardAgentCommandTests.RunAsync(["review", "pending", "--repo", Repo, "--json"], "", false, Path.Combine(_dir, "home"), _ct);

        exit.ShouldBe(ExitCodes.Ok, error);
        await MatchesAsync("review-pending.json", output);
    }

    [Fact]
    public async Task Scope_plan_json_is_unchanged()
    {
        await _instance.WriteScopeAsync(_ct);
        _instance.HeldStructure();

        var (exit, output, error) = await ReviewSessionTests.RunAsync(_instance, ["scope", "plan", _instance.ScopeDir, "--json"], "", false, _ct);

        exit.ShouldBe(ExitCodes.Ok, error);
        await MatchesAsync("scope-plan.json", output);
    }

    /// <summary>The example repo, offline, with a failed collector run and an old upload, on a fixed day.</summary>
    private async Task<string> ExampleAsync(string[] args)
    {
        TestFiles.CopyExampleRepo(Repo);
        await File.WriteAllTextAsync(Config, (await File.ReadAllTextAsync(Config, _ct)).Replace("/var/lib/evidence-hound", Path.Combine(_dir, "var"), StringComparison.Ordinal), _ct);
        var upload = Directory.CreateDirectory(Path.Combine(_dir, "var", "manual", "example-incident-response-plan")).FullName;
        await File.WriteAllTextAsync(Path.Combine(upload, "irp.pdf"), "plan", _ct);
        File.SetLastWriteTimeUtc(Path.Combine(upload, "irp.pdf"), Ran.UtcDateTime.AddDays(-30));
        await new FileRunRecordStore(Path.Combine(_dir, "var", "evidence")).SaveAsync(
            new RunRecord("example-m365", Ran, Ran.AddSeconds(3), CollectorOutcome.Failed, 0, 0, 2, [new("collector.all_checks_errored", "All 2 checks errored.")], null),
            _ct);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await HoundCli.RunAsync(args, new StringReader(""), output, error, new Dictionary<string, string?> { ["HOUND_TODAY"] = "2027-02-15" }, false, _ct);

        exit.ShouldBe(ExitCodes.Ok, error.ToString());
        return output.ToString();
    }

    private async Task MatchesAsync(string name, string actual)
    {
        var normalised = Port().Replace(actual.Replace(_dir, "<dir>", StringComparison.Ordinal).Replace(_instance.Repo, "<repo>", StringComparison.Ordinal), "localhost:<port>");
        var golden = Path.Combine(SourceFolder(), "golden", name);
        if (Environment.GetEnvironmentVariable("HOUND_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(golden)!);
            await File.WriteAllTextAsync(golden, normalised, _ct);
        }

        File.Exists(golden).ShouldBeTrue($"{golden} is missing; run with HOUND_UPDATE_GOLDEN=1 to write it");
        normalised.ShouldBe(await File.ReadAllTextAsync(golden, _ct));
    }

    private static string SourceFolder() => Path.Combine(ExampleRepoTests.RepoRoot, "tests", "EvidenceHound.Cli.Tests");

    [GeneratedRegex(@"localhost:\d+")]
    private static partial Regex Port();

    public void Dispose()
    {
        _instance.Dispose();
        Directory.Delete(_dir, recursive: true);
    }
}
