using System.Text.Json.Nodes;
using EvidenceHound.Cli;
using EvidenceHound.Runtime;
using EvidenceHound.Runtime.Status;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s18-01: <c>hound next --json</c> on a copy of the example repo against the local CISO Assistant: the connection is checked, a failed
/// collector and the expiring exception are listed, and the scope is planned. The output lands in <c>test-results/next/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class NextFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hound-next-").FullName;

    [Fact]
    public async Task Lists_the_failed_collector_and_the_expiring_exception_with_the_connection_checked()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = Path.Combine(_dir, "repo");
        TestRepo.CopyExample(repo);
        var config = Path.Combine(repo, "hound.yaml");
        await File.WriteAllTextAsync(config, (await File.ReadAllTextAsync(config, ct)).Replace("/var/lib/evidence-hound", Path.Combine(_dir, "var"), StringComparison.Ordinal), ct);
        var ran = new DateTimeOffset(2027, 2, 14, 16, 0, 0, TimeSpan.Zero);
        await new FileRunRecordStore(Path.Combine(_dir, "var", "evidence")).SaveAsync(
            new RunRecord("example-m365", ran, ran.AddSeconds(3), CollectorOutcome.Failed, 0, 0, 2, [new("collector.all_checks_errored", "All 2 checks errored.")], null), ct);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await HoundCli.RunAsync(
            ["next", "--repo", repo, "--config", config, "--json"],
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token, ["HOUND_TODAY"] = "2027-02-15" },
            ct);
        var results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "next")).FullName;
        await File.WriteAllTextAsync(Path.Combine(results, "next.json"), $"{output}{error}", ct);

        exit.ShouldBe(ExitCodes.Ok, $"{output}{error}");
        var json = JsonNode.Parse(output.ToString())!;
        json["connection"]!["reachable"]!.GetValue<bool>().ShouldBeTrue(output.ToString());
        json["connection"]!["token"]!.GetValue<string>().ShouldBe("accepted");
        json["connection"]!["note"]!.GetValue<string>().ShouldNotContain("Could not plan", Case.Sensitive, output.ToString());
        var items = json["items"]!.AsArray().Select(i => i!).ToList();
        items.ShouldContain(i => i["kind"]!.GetValue<string>() == "collector" && i["subject"]!.GetValue<string>() == "example-m365", output.ToString());
        items.ShouldContain(i => i["kind"]!.GetValue<string>() == "exception" && i["subject"]!.GetValue<string>() == "EXC-01", output.ToString());
        output.ToString().ShouldNotContain(ciso.Token);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
