using System.Text.Json.Nodes;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s10-07: one review queue and one guided session. <c>hound review pending --json</c> lists everything that waits on a person, in one
/// shape, for the agent to prepare; <c>hound review</c> takes the person through it in a terminal.
/// </summary>
public sealed class ReviewCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hound-review-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public ReviewCommandTests()
    {
        TestFiles.CopyExampleRepo(Repo);
        File.WriteAllText(Path.Combine(Repo, "frameworks", "example-framework.json"), OnboardSystemCommandTests.Framework);
        File.WriteAllText(Config, ManualProposeTests.Config);
    }

    [Fact]
    public async Task Lists_every_pending_kind_in_one_shape()
    {
        await OnboardAgentCommandTests.DraftEverythingAsync(Repo, _ct);
        await File.WriteAllTextAsync(Path.Combine(Repo, "scope", "capabilities.yaml"), ApproveCommandTests.Capabilities, _ct);
        var export = Path.Combine(_root, "firewall-export.txt");
        await File.WriteAllTextAsync(export, "rule 14: deny any any\n", _ct);
        (await ManualProposeTests.ProposeAsync(Repo, export, "--result", "pass", "--observation", "The last rule denies any to any.", "--supports", "rule 14: deny any any")).Exit.ShouldBe(ExitCodes.Ok);

        var (exit, output, error) = await RunAsync(["review", "pending", "--json", "--repo", Repo, "--config", Config], terminal: false);

        exit.ShouldBe(ExitCodes.Ok, error);
        var queue = JsonNode.Parse(output)!;
        var items = queue["items"]!.AsArray();
        items.Select(i => i!["kind"]!.GetValue<string>()).ShouldBe(["capability", "supplier", "not_applicable", "manual_evidence"], ignoreOrder: true);
        items.ShouldAllBe(i => i!["key"] != null && i["title"] != null && i["why"] != null && i["effect"] != null && i["minutes"]!.GetValue<int>() > 0);
        var na = items.Single(i => i!["kind"]!.GetValue<string>() == "not_applicable")!;
        na["key"]!.GetValue<string>().ShouldBe("not_applicable:EXAMPLE-ASSETS/EX-2");
        na["recommendation"]!["reasons"]!.GetValue<string>().ShouldContain("Only the supplier administers it.");
        var manual = items.Single(i => i!["kind"]!.GetValue<string>() == "manual_evidence")!;
        manual["key"]!.GetValue<string>().ShouldBe("manual_evidence:example-firewall-config/firewall-export.txt");
        manual["recommendation"]!["sources"]!.AsArray().Select(s => s!.GetValue<string>()).ShouldContain("rule 14: deny any any");
        queue["minutes"]!.GetValue<int>().ShouldBe(items.Sum(i => i!["minutes"]!.GetValue<int>()));

        // Listing changes nothing.
        (await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "capabilities.yaml"), _ct)).ShouldNotContain("approved_by");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Repo => Path.Combine(_root, "repo");

    private string Config => Path.Combine(Repo, "hound.yaml");

    private Task<(int Exit, string Output, string Error)> RunAsync(string[] args, bool terminal, string answers = "") =>
        OnboardAgentCommandTests.RunAsync(args, answers, terminal, Path.Combine(_root, "home"), _ct);
}
