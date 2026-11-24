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

    [Fact]
    public async Task An_agents_recommendation_is_recorded_shown_in_the_session_and_cleared_once_accepted()
    {
        // s10-05 rehearsal: a drafted supplier had no recommendation, and the agent had no way to give one.
        await OnboardAgentCommandTests.DraftEverythingAsync(Repo, _ct);
        const string Key = "supplier:SUP-EXAMPLE/CTR-EXAMPLE-ASSETS";

        var (exit, _, error) = await RunAsync(
            ["review", "recommend", Key, "--repo", Repo, "--answer", "accept", "--reason", "Its trust page shows an ISO 27001 certificate valid to 2027.",
                "--source", "documents/suppliers/sup-example/trust-centre.md", "--json"], terminal: false);
        exit.ShouldBe(ExitCodes.Ok, error);

        (exit, var output, error) = await RunAsync(["review", "pending", "--json", "--repo", Repo], terminal: false);
        exit.ShouldBe(ExitCodes.Ok, error);
        var item = JsonNode.Parse(output)!["items"]!.AsArray().Single(i => i!["key"]!.GetValue<string>() == Key)!;
        item["recommendation"]!["answer"]!.GetValue<string>().ShouldBe("accept");
        item["recommendation"]!["reasons"]!.GetValue<string>().ShouldContain("ISO 27001");
        item["recommendation"]!["sources"]!.AsArray().Select(s => s!.GetValue<string>()).ShouldBe(["documents/suppliers/sup-example/trust-centre.md"]);

        // An item that is not waiting, an answer the item cannot take, and a recommendation without a source are refused.
        (exit, _, error) = await RunAsync(["review", "recommend", "supplier:SUP-NONE/CTR-NONE", "--repo", Repo, "--answer", "accept", "--reason", "r", "--source", "s"], terminal: false);
        (exit, error.Contains("is not waiting on a person", StringComparison.Ordinal)).ShouldBe((ExitCodes.Configuration, true));
        (exit, _, error) = await RunAsync(["review", "recommend", Key, "--repo", Repo, "--answer", "blocker", "--reason", "r", "--source", "s"], terminal: false);
        (exit, error.Contains("takes accept or decline", StringComparison.Ordinal)).ShouldBe((ExitCodes.Configuration, true));
        (exit, _, error) = await RunAsync(["review", "recommend", Key, "--repo", Repo, "--answer", "accept", "--reason", "r"], terminal: false);
        (exit, error.Contains("--source", StringComparison.Ordinal)).ShouldBe((ExitCodes.Configuration, true));

        // The person reads it in the session; once they accept the item, the recommendation goes with it.
        (exit, _, error) = await RunAsync(["review", "--repo", Repo, "--no-apply"], terminal: true, "Sam Example\na\ns\ns\ns\n");
        exit.ShouldBe(ExitCodes.Ok, error);
        error.ShouldContain("Recommended: accept. Its trust page shows an ISO 27001 certificate valid to 2027. (sources: documents/suppliers/sup-example/trust-centre.md)");

        // The supplier's first review is due in a month: it is not asked now, nor listed, until two weeks before.
        error.ShouldNotContain("[now ready] [review]");
        (exit, output, error) = await RunAsync(["review", "pending", "--json", "--repo", Repo], terminal: false);
        exit.ShouldBe(ExitCodes.Ok, error);
        JsonNode.Parse(output)!["items"]!.AsArray().ShouldNotContain(i => i!["kind"]!.GetValue<string>() == "supplier_review");
        (await File.ReadAllTextAsync(Path.Combine(Repo, "review", "recommendations.json"), _ct)).ShouldNotContain(Key);
    }

    [Fact]
    public async Task An_unchanged_reattestation_is_covered_once_a_standing_approval_is_signed()
    {
        // The first attestation of a source is always reviewed, and offers no rule.
        await ProposeAsync("january.txt");
        var (exit, output, error) = await RunAsync(["review", "--repo", Repo, "--no-apply"], terminal: true, "Sam Example\na\n");
        exit.ShouldBe(ExitCodes.Ok, error);
        error.ShouldNotContain("without review");

        // The next export has the same checks and results: accepting it offers the rule, for twelve months.
        await ProposeAsync("february.txt");
        (exit, output, error) = await RunAsync(["review", "--repo", Repo, "--no-apply"], terminal: true, "Sam Example\na\ny\n");
        exit.ShouldBe(ExitCodes.Ok, error);
        error.ShouldContain("without review for 12 months");
        (await File.ReadAllTextAsync(Path.Combine(Repo, "review", "standing-approvals.json"), _ct)).ShouldContain("\"source\": \"example-firewall-config\"");

        // From then on an unchanged export is listed as covered and attested in the session without a question.
        await ProposeAsync("march.txt");
        (exit, output, error) = await RunAsync(["review", "pending", "--json", "--repo", Repo], terminal: false);
        exit.ShouldBe(ExitCodes.Ok, error);
        JsonNode.Parse(output)!["items"]!.AsArray().ShouldHaveSingleItem()!["covered_by"]!["approved_by"]!.GetValue<string>().ShouldBe("Sam Example");
        (exit, output, error) = await RunAsync(["review", "--repo", Repo, "--no-apply"], terminal: true, "Sam Example\n");
        exit.ShouldBe(ExitCodes.Ok, error);
        error.ShouldNotContain("a accept, c challenge");
        output.ShouldContain("1 by standing approval");
        output.ShouldContain("Standing approvals: unchanged re-attestation of example-firewall-config, signed by Sam Example");
        File.Exists(Path.Combine(Repo, "manual", "firewall", "march.txt.attest.yaml")).ShouldBeTrue();
        var signOff = Directory.GetFiles(Path.Combine(Repo, "review", "sign-offs"), "*.yaml").Order(StringComparer.Ordinal).Last();
        (await File.ReadAllTextAsync(signOff, _ct)).ShouldContain("standing:");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private async Task ProposeAsync(string name)
    {
        var export = Path.Combine(_root, name);
        await File.WriteAllTextAsync(export, $"{name}\nrule 14: deny any any\n", _ct);
        var (exit, _, error) = await ManualProposeTests.ProposeAsync(Repo, export, "--result", "pass", "--observation", "The last rule denies any to any.", "--supports", "rule 14: deny any any");
        exit.ShouldBe(ExitCodes.Ok, error);
    }

    private string Repo => Path.Combine(_root, "repo");

    private string Config => Path.Combine(Repo, "hound.yaml");

    private Task<(int Exit, string Output, string Error)> RunAsync(string[] args, bool terminal, string answers = "") =>
        OnboardAgentCommandTests.RunAsync(args, answers, terminal, Path.Combine(_root, "home"), _ct);
}
