using System.Text.Json.Nodes;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s18-02: <c>hound review prepare</c> gets the week ready in one step: the queue, the items still without a recommendation and the skill
/// that drafts each, and a saved scope plan that the person's session applies.
/// </summary>
public sealed class ReviewPrepareTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hound-prepare-").FullName;
    private readonly ScopeTestInstance _instance = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private string Repo => Path.Combine(_root, "repo");

    [Fact]
    public async Task Lists_items_without_a_recommendation_and_their_skill()
    {
        TestFiles.CopyExampleRepo(Repo);
        await File.WriteAllTextAsync(Path.Combine(Repo, "frameworks", "example-framework.json"), OnboardSystemCommandTests.Framework, _ct);
        await File.WriteAllTextAsync(Path.Combine(Repo, "hound.yaml"), ManualProposeTests.Config, _ct);
        await OnboardAgentCommandTests.DraftEverythingAsync(Repo, _ct);

        var (exit, output, error) = await OnboardAgentCommandTests.RunAsync(["review", "prepare", "--repo", Repo, "--json"], "", false, Path.Combine(_root, "home"), _ct);

        exit.ShouldBe(ExitCodes.Ok, error);
        var json = JsonNode.Parse(output)!;
        json["items"]!.GetValue<int>().ShouldBeGreaterThan(0);
        var bare = json["without_recommendation"]!.AsArray().Select(i => i!).ToList();
        bare.ShouldNotBeEmpty();
        bare.ShouldAllBe(i => i["skill"] != null && i["key"] != null);
        bare.ShouldContain(i => i["kind"]!.GetValue<string>() == "supplier" && i["skill"]!.GetValue<string>() == "onboard-a-third-party");
        bare.ShouldNotContain(i => i["kind"]!.GetValue<string>() == "not_applicable", "its drafted reasoning is the recommendation");
        json["plan"]!["saved"].ShouldBeNull("no connection, so no plan");
        json["plan"]!["note"]!.GetValue<string>().ShouldContain("HOUND_CA_TOKEN");
    }

    [Fact]
    public async Task Saves_the_plan_the_session_applies()
    {
        await _instance.WriteScopeAsync(_ct);
        _instance.HeldStructure();
        _instance.Accept("POST", "/api/compliance-assessments/");
        _instance.Accept("POST", "/api/applied-controls/");
        _instance.Accept("PATCH", "/api/requirement-assessments/ra-1/");

        var (exit, output, error) = await RunAsync(["review", "prepare", "--repo", _instance.Repo, "--json"]);

        exit.ShouldBe(ExitCodes.Ok, error);
        var plan = JsonNode.Parse(output)!["plan"]!;
        plan["changes"]!.GetValue<int>().ShouldBe(3);
        var saved = plan["saved"]!.GetValue<string>();
        File.Exists(saved).ShouldBeTrue();
        _instance.Ca.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
        _instance.Ca.ResetLogEntries();

        (exit, output, error) = await RunAsync(["review", "--repo", _instance.Repo], "y\n", terminal: true);

        exit.ShouldBe(ExitCodes.Ok, output + error);
        output.ShouldContain("the plan prepared on 2026-11-23");
        _instance.Ca.LogEntries.Where(e => e.RequestMessage!.Method != "GET").Select(e => (e.RequestMessage!.Method, e.RequestMessage.Path)).ShouldBe(
            [("POST", "/api/compliance-assessments/"), ("POST", "/api/applied-controls/"), ("PATCH", "/api/requirement-assessments/ra-1/")]);
        output.ShouldContain("Session summary:");
        output.ShouldContain("3 change(s) applied to CISO Assistant");
        File.Exists(saved).ShouldBeFalse("an applied plan is used once");
    }

    private Task<(int Exit, string Output, string Error)> RunAsync(string[] args, string input = "", bool terminal = false) =>
        ReviewSessionTests.RunAsync(_instance, args, input, terminal, _ct);

    public void Dispose()
    {
        _instance.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}

/// <summary>s18-02: the person's session applies the prepared plan only while CISO Assistant still holds what the plan read.</summary>
public sealed class ReviewSessionTests : IDisposable
{
    private readonly ScopeTestInstance _instance = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Refuses_a_saved_plan_older_than_the_state()
    {
        await _instance.WriteScopeAsync(_ct);
        _instance.HeldStructure();
        (await RunAsync(_instance, ["review", "prepare", "--repo", _instance.Repo, "--json"], "", false, _ct)).Exit.ShouldBe(ExitCodes.Ok);
        _instance.ControlMadeByHand();

        var (exit, output, error) = await RunAsync(_instance, ["review", "--repo", _instance.Repo], "y\n", true, _ct);

        exit.ShouldBe(ExitCodes.Configuration, output + error);
        error.ShouldContain("scope.plan_stale");
        error.ShouldContain("AC-MFA");
        _instance.Ca.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
    }

    internal static Task<(int Exit, string Output, string Error)> RunAsync(ScopeTestInstance instance, string[] args, string input, bool terminal, CancellationToken ct) =>
        Hound.RunAsync(args, ct, input, instance.Settings, terminal);

    public void Dispose() => _instance.Dispose();
}
