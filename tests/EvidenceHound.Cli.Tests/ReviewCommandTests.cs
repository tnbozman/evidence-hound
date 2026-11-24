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

    [Fact]
    public async Task Refuses_without_a_terminal()
    {
        await File.WriteAllTextAsync(Path.Combine(Repo, "scope", "capabilities.yaml"), ApproveCommandTests.Capabilities, _ct);

        var (exit, _, error) = await RunAsync(["review", "--repo", Repo, "--no-apply"], terminal: false, "Sam Example\na\n");

        exit.ShouldBe(ExitCodes.Usage);
        error.ShouldContain("needs a person at a terminal");
        (await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "capabilities.yaml"), _ct)).ShouldNotContain("approved_by");
    }

    [Fact]
    public async Task Accepted_items_are_written_as_approve_writes_them_and_one_sign_off_is_recorded()
    {
        await OnboardAgentCommandTests.DraftEverythingAsync(Repo, _ct);
        await File.WriteAllTextAsync(Path.Combine(Repo, "scope", "capabilities.yaml"), ApproveCommandTests.Capabilities, _ct);
        var export = Path.Combine(_root, "firewall-export.txt");
        await File.WriteAllTextAsync(export, "rule 14: deny any any\n", _ct);
        (await ManualProposeTests.ProposeAsync(Repo, export, "--result", "pass", "--observation", "The last rule denies any to any.", "--supports", "rule 14: deny any any")).Exit.ShouldBe(ExitCodes.Ok);

        // In order: the capability, the supplier, the not-applicable decision, then the manual evidence. Sam accepts three and challenges the supplier.
        var (exit, output, error) = await RunAsync(
            ["review", "--repo", Repo, "--config", Config, "--for", "60m", "--no-apply"], terminal: true,
            "Sam Example\na\nc\nIs the asset inventory really only administered by the supplier?\na\na\n");

        exit.ShouldBe(ExitCodes.Ok, error);
        string[] asked = ["[1/4] [capability] EX", "[2/4] [supplier] SUP-EXAMPLE", "[3/4] [not applicable] EXAMPLE-ASSETS EX-2", "[4/4] [manual evidence] example-firewall-config"];
        asked.Select(a => error.IndexOf(a, StringComparison.Ordinal)).ShouldBeInOrder(SortDirection.Ascending);
        asked.ShouldAllBe(a => error.Contains(a, StringComparison.Ordinal));
        error.ShouldContain("Why it matters:");
        error.ShouldContain("Recommended:");
        output.ShouldContain("Accepted 3, challenged 1, skipped 0.");

        (await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "capabilities.yaml"), _ct)).ShouldContain("approved_by: Sam Example");
        (await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "third-parties", "sup-example.yaml"), _ct)).ShouldContain("status: draft");
        File.Exists(Path.Combine(Repo, "manual", "firewall", "firewall-export.txt.attest.yaml")).ShouldBeTrue();

        var signOff = Directory.GetFiles(Path.Combine(Repo, "review", "sign-offs"), "*.yaml").ShouldHaveSingleItem();
        var record = await File.ReadAllTextAsync(signOff, _ct);
        record.ShouldContain("person: Sam Example");
        record.ShouldContain("capability:EX");
        record.ShouldContain("not_applicable:EXAMPLE-ASSETS/EX-2");
        record.ShouldContain("manual_evidence:example-firewall-config/firewall-export.txt");

        // The challenge stays queued, with the note for the agent to answer; nothing else is left.
        (exit, output, error) = await RunAsync(["review", "pending", "--json", "--repo", Repo, "--config", Config], terminal: false);
        exit.ShouldBe(ExitCodes.Ok, error);
        var item = JsonNode.Parse(output)!["items"]!.AsArray().ShouldHaveSingleItem()!;
        item["key"]!.GetValue<string>().ShouldBe("supplier:SUP-EXAMPLE/CTR-EXAMPLE-ASSETS");
        item["challenge"]!["note"]!.GetValue<string>().ShouldBe("Is the asset inventory really only administered by the supplier?");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Repo => Path.Combine(_root, "repo");

    private string Config => Path.Combine(Repo, "hound.yaml");

    private Task<(int Exit, string Output, string Error)> RunAsync(string[] args, bool terminal, string answers = "") =>
        OnboardAgentCommandTests.RunAsync(args, answers, terminal, Path.Combine(_root, "home"), _ct);
}
