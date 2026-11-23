using System.Text.Json.Nodes;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s09-37: the agent interface. <c>hound supplier draft</c>, <c>hound onboard &lt;system&gt; pending</c> and <c>decide</c> ask nothing and
/// answer in JSON, so an agent drafts a whole onboarding; everything it drafts waits for a person's <c>hound approve</c> on a terminal.
/// </summary>
public sealed class OnboardAgentCommandTests : IDisposable
{
    internal const string System = OnboardSystemCommandTests.System;

    private readonly string _root = Directory.CreateTempSubdirectory("hound-onboard-agent-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public OnboardAgentCommandTests()
    {
        TestFiles.CopyExampleRepo(Repo);
        File.WriteAllText(Path.Combine(Repo, "frameworks", "example-framework.json"), OnboardSystemCommandTests.Framework);
    }

    [Fact]
    public async Task Supplier_draft_writes_the_third_party_file_from_options()
    {
        var (exit, output, error) = await DraftSupplierAsync(Repo, _ct);

        exit.ShouldBe(ExitCodes.Ok, error);
        var drafted = JsonNode.Parse(output)!;
        drafted["file"]!.GetValue<string>().ShouldBe("scope/third-parties/sup-example.yaml");
        drafted["approved"]!.GetValue<bool>().ShouldBeFalse();
        var file = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "third-parties", "sup-example.yaml"), _ct);
        file.ShouldContain("ref_id: \"SUP-EXAMPLE\"");
        file.ShouldContain("status: draft");
        file.ShouldContain("# maturity: Holds an ISO 27001 certificate. (agent)\nmaturity: 3");
        file.ShouldContain("ref_id: \"EA-SUP-EXAMPLE\"");
        file.ShouldContain("hound approve");

        // The drafted file is valid scope, and a second draft never overwrites it.
        (await RunAsync(["scope", "validate", Path.Combine(Repo, "scope")])).Exit.ShouldBe(ExitCodes.Ok);
        (exit, _, error) = await DraftSupplierAsync(Repo, _ct);
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("already exists");

        // A level outside its scale, or without a reason, is refused before anything is written.
        (exit, _, error) = await RunAsync(
            ["supplier", "draft", "--repo", Repo, "--ref", "SUP-OTHER", "--name", "Other Ltd", "--solution-ref", "SOL-OTHER", "--solution-name", "Other", "--contract-ref", "CTR-OTHER",
             "--contract-name", "Other subscription", "--level", "trust=7:Too high.", "--level", "maturity=2"]);
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("trust is 7, outside 1 to 4");
        error.ShouldContain("maturity has no reason");
        File.Exists(Path.Combine(Repo, "scope", "third-parties", "sup-other.yaml")).ShouldBeFalse();
    }

    [Fact]
    public async Task Pending_lists_undecided_requirements_as_json()
    {
        // Without a drafted supplier, pending says what to do first.
        var (exit, _, error) = await PendingAsync();
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("hound supplier draft");

        (await DraftSupplierAsync(Repo, _ct)).Exit.ShouldBe(ExitCodes.Ok);
        (exit, var output, error) = await PendingAsync();

        exit.ShouldBe(ExitCodes.Ok, error);
        var pending = JsonNode.Parse(output)!;
        pending["system"]!.GetValue<string>().ShouldBe(System);
        pending["supplier"]!["ref_id"]!.GetValue<string>().ShouldBe("SUP-EXAMPLE");
        pending["supplier"]!["approved"]!.GetValue<bool>().ShouldBeFalse();
        pending["api"]!.GetValue<string>().ShouldContain("GET /v1/devices (paged by pageToken)");
        pending["skills"]!.AsArray().ShouldContain(s => s!["body"]!.GetValue<string>().Length > 0);
        pending["decided"]!.GetValue<int>().ShouldBe(0);
        var requirements = pending["pending"]!.AsArray();
        requirements.Select(r => r!["requirement"]!.GetValue<string>()).ShouldBe(["EX-1", "EX-2", "EX-3"]);
        requirements[1]!["text"]!.GetValue<string>().ShouldBe("Multi-factor authentication for privileged users.");
        requirements[1]!["urn"]!.GetValue<string>().ShouldBe("urn:example:req:b");
        error.ShouldBeEmpty();

        // The session is saved, so the next call needs only the system and the repo, and a decided requirement leaves the list.
        (await DecideAsync("EX-1", "--kind", "manual", "--reasoning", "The owner sends the patch report.")).Exit.ShouldBe(ExitCodes.Ok);
        (exit, output, error) = await RunAsync(["onboard", System, "pending", "--json", "--repo", Repo]);
        exit.ShouldBe(ExitCodes.Ok, error);
        pending = JsonNode.Parse(output)!;
        pending["decided"]!.GetValue<int>().ShouldBe(1);
        pending["pending"]!.AsArray().Select(r => r!["requirement"]!.GetValue<string>()).ShouldBe(["EX-2", "EX-3"]);

        // For a person: the same list as text.
        (exit, output, error) = await RunAsync(["onboard", System, "pending", "--repo", Repo]);
        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("EX-2: Multi-factor authentication for privileged users.");
        output.ShouldContain("1 of 3 decided");
    }

    [Fact]
    public async Task Decide_records_a_draft_decision()
    {
        (await DraftSupplierAsync(Repo, _ct)).Exit.ShouldBe(ExitCodes.Ok);
        (await PendingAsync()).Exit.ShouldBe(ExitCodes.Ok);

        var (exit, output, error) = await DecideAsync("EX-3", "--kind", "api_check", "--reasoning", "GET /v1/devices shows the last backup.", "--source", "example-asset-api.openapi.yaml", "--source", "GET /v1/devices");

        exit.ShouldBe(ExitCodes.Ok, error);
        var answer = JsonNode.Parse(output)!;
        (answer["requirement"]!.GetValue<string>(), answer["kind"]!.GetValue<string>(), answer["drafted_by"]!.GetValue<string>()).ShouldBe(("EX-3", "api_check", "agent"));
        answer["remaining"]!.GetValue<int>().ShouldBe(2);
        var decision = Session()["decisions"]!.AsArray().ShouldHaveSingleItem()!;
        decision["key"]!.GetValue<string>().ShouldBe("EX-3");
        decision["drafted_by"]!.GetValue<string>().ShouldBe("agent");
        decision["accepted_by"]!.GetValue<string>().ShouldBeEmpty();
        decision["sources"]!.AsArray().Select(s => s!.GetValue<string>()).ShouldBe(["example-asset-api.openapi.yaml", "GET /v1/devices"]);

        // Validated as the interactive flow is: another system needs its name, not applicable its justification, and the requirement must be in scope.
        (exit, _, error) = await DecideAsync("EX-2", "--kind", "other_system", "--reasoning", "The identity provider enforces it.");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("name the system that meets it");
        (exit, _, error) = await DecideAsync("EX-2", "--kind", "not_applicable", "--reasoning", "No privileged users.");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("not applicable needs a justification");
        (exit, _, error) = await DecideAsync("EX-9", "--kind", "manual", "--reasoning", "Not in scope.");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("EX-9 is not a requirement in scope for EXAMPLE-ASSETS");
        Session()["decisions"]!.AsArray().Count.ShouldBe(1);

        // A ref_id that several requirements share is refused, naming each by the key pending lists.
        var framework = Path.Combine(Repo, "frameworks", "example-framework.json");
        await File.WriteAllTextAsync(framework, OnboardSystemCommandTests.Framework.Replace("\"ref_id\": \"EX-3\"", "\"ref_id\": \"EX-2\"", StringComparison.Ordinal), _ct);
        var shared = Path.Combine(_root, "shared");
        TestFiles.CopyDirectory(Repo, shared);
        File.Delete(Path.Combine(shared, "onboarding", System, "session.json"));
        string[] start = ["--framework", Path.Combine(shared, "frameworks", "example-framework.json"), "--group", "2", "--owner", "alex@example.com", "--product", "Example Asset API", "--solution", "SOL-EXAMPLE-ASSETS"];
        (await RunAsync(["onboard", System, "pending", "--json", "--repo", shared, .. start])).Exit.ShouldBe(ExitCodes.Ok);
        (exit, _, error) = await RunAsync(["onboard", System, "decide", "EX-2", "--repo", shared, "--kind", "manual", "--reasoning", "Which one?"]);
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("EX-2 names 2 requirements");
        error.ShouldContain("urn:example:req:b, urn:example:req:c");
        await File.WriteAllTextAsync(framework, OnboardSystemCommandTests.Framework, _ct);

        // Deciding again replaces the draft.
        (await DecideAsync("EX-3", "--kind", "manual", "--reasoning", "The owner exports the backup report.")).Exit.ShouldBe(ExitCodes.Ok);
        Session()["decisions"]!.AsArray().ShouldHaveSingleItem()!["kind"]!.GetValue<string>().ShouldBe("manual");
        (await DecideAsync("EX-2", "--kind", "other_system", "--reasoning", "The identity provider enforces it.", "--other-system", "Example 365")).Exit.ShouldBe(ExitCodes.Ok);
        Session()["decisions"]!.AsArray().Select(d => d!["key"]!.GetValue<string>()).ShouldBe(["EX-2", "EX-3"]);
    }

    [Fact]
    public async Task Not_applicable_stays_a_draft_until_approved_on_a_terminal()
    {
        await DraftEverythingAsync();
        var session = Session();
        session["stage"]!.GetValue<string>().ShouldBe("decided");
        var notApplicable = session["decisions"]!.AsArray().Single(d => d!["key"]!.GetValue<string>() == "EX-2")!;
        notApplicable["justification"]!.GetValue<string>().ShouldBe("Only the supplier administers it.");
        notApplicable["approver"].ShouldBeNull();
        var audit = Path.Combine(Repo, "scope", "audits", "example-assets-example-framework.yaml");
        File.Exists(audit).ShouldBeFalse();

        // The interactive command writes nothing from drafts either: it points to hound approve.
        var (exit, output, error) = await RunAsync(["onboard", System, "--repo", Repo]);
        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("hound approve");
        File.Exists(audit).ShouldBeFalse();

        // A person approves on a terminal: the supplier, its review, the not-applicable decision, then the system (default domain and perimeter).
        (exit, output, error) = await RunAsync(["approve", "--repo", Repo], "Sam Example\ny\nok\ny\ny\n\n\n", terminal: true);

        exit.ShouldBe(ExitCodes.Ok, error);
        notApplicable = Session()["decisions"]!.AsArray().Single(d => d!["key"]!.GetValue<string>() == "EX-2")!;
        notApplicable["approver"]!.GetValue<string>().ShouldBe("Sam Example");
        notApplicable["approved_on"]!.GetValue<string>().ShouldBe("2026-11-23");
        Session()["decisions"]!.AsArray().Select(d => d!["accepted_by"]!.GetValue<string>()).ShouldAllBe(by => by == "Sam Example");
        (await File.ReadAllTextAsync(audit, _ct)).ShouldContain("result: not_applicable");
        var record = Directory.GetFiles(Path.Combine(Repo, "onboarding"), "example-assets-*.md").ShouldHaveSingleItem();
        (await File.ReadAllTextAsync(record, _ct)).ShouldContain("Only the supplier administers it. Approved by Sam Example.");
        output.ShouldContain("SUP-EXAMPLE is approved and its review is concluded ok");
    }

    [Fact]
    public async Task Approve_without_a_terminal_is_refused()
    {
        await DraftEverythingAsync();
        var supplier = Path.Combine(Repo, "scope", "third-parties", "sup-example.yaml");
        var before = (Supplier: await File.ReadAllTextAsync(supplier, _ct), Session: Session().ToJsonString());

        // The same answers a person would type, piped in: refused, and nothing changes.
        var (exit, output, error) = await RunAsync(["approve", "--repo", Repo], "Sam Example\ny\nok\ny\ny\n\n\n", terminal: false);

        exit.ShouldBe(ExitCodes.Usage);
        error.ShouldContain("hound approve needs a person at a terminal");
        output.ShouldBeEmpty();
        (await File.ReadAllTextAsync(supplier, _ct)).ShouldBe(before.Supplier);
        Session().ToJsonString().ShouldBe(before.Session);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>The example supplier drafted with <c>hound supplier draft</c>, as an agent would.</summary>
    internal static Task<(int Exit, string Output, string Error)> DraftSupplierAsync(string repo, CancellationToken ct) => RunAsync(
        [
            "supplier", "draft", "--repo", repo, "--system", System, "--ref", "SUP-EXAMPLE", "--name", "Example Assets Ltd", "--solution-ref", "SOL-EXAMPLE-ASSETS",
            "--solution-name", "Asset inventory", "--contract-ref", "CTR-EXAMPLE-ASSETS", "--contract-name", "Asset inventory subscription",
            "--level", "dependency=2:Holds the asset inventory only.", "--level", "maturity=3:Holds an ISO 27001 certificate.", "--json",
        ],
        "", false, Path.Combine(repo, "..", "home"), ct);

    /// <summary>A whole onboarding drafted with pending and decide only: one manual, one not applicable, one API check.</summary>
    internal static async Task DraftEverythingAsync(string repo, CancellationToken ct)
    {
        (await DraftSupplierAsync(repo, ct)).Exit.ShouldBe(ExitCodes.Ok);
        string[] start =
        [
            "onboard", System, "pending", "--json", "--repo", repo, "--framework", Path.Combine(repo, "frameworks", "example-framework.json"), "--group", "2",
            "--spec", Path.Combine(ExampleRepoTests.RepoRoot, "examples", "apis", "example-asset-api.openapi.yaml"),
            "--owner", "alex@example.com", "--product", "Example Asset API", "--solution", "SOL-EXAMPLE-ASSETS",
        ];
        string[][] steps =
        [
            start,
            ["onboard", System, "decide", "EX-1", "--repo", repo, "--kind", "manual", "--reasoning", "The owner sends the patch report."],
            ["onboard", System, "decide", "EX-2", "--repo", repo, "--kind", "not_applicable", "--reasoning", "No privileged users.", "--justification", "Only the supplier administers it."],
            ["onboard", System, "decide", "EX-3", "--repo", repo, "--kind", "api_check", "--reasoning", "GET /v1/devices shows the last backup.", "--source", "GET /v1/devices"],
        ];
        foreach (var step in steps)
        {
            var (exit, _, error) = await RunAsync(step, "", false, Path.Combine(repo, "..", "home"), ct);
            exit.ShouldBe(ExitCodes.Ok, error);
        }
    }

    internal static async Task<(int Exit, string Output, string Error)> RunAsync(string[] args, string input, bool terminal, string home, CancellationToken ct)
    {
        using var reader = new StringReader(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal) { ["XDG_CONFIG_HOME"] = home, ["HOUND_TODAY"] = "2026-11-23" };
        var exit = await HoundCli.RunAsync(args, reader, output, error, settings, terminal, ct);
        return (exit, output.ToString(), error.ToString());
    }

    private string Repo => Path.Combine(_root, "repo");

    private JsonNode Session() => JsonNode.Parse(File.ReadAllText(Path.Combine(Repo, "onboarding", System, "session.json")))!;

    private Task DraftEverythingAsync() => DraftEverythingAsync(Repo, _ct);

    private Task<(int Exit, string Output, string Error)> PendingAsync() => RunAsync(
        [
            "onboard", System, "pending", "--json", "--repo", Repo, "--framework", Path.Combine(Repo, "frameworks", "example-framework.json"), "--group", "2",
            "--spec", Path.Combine(ExampleRepoTests.RepoRoot, "examples", "apis", "example-asset-api.openapi.yaml"),
            "--owner", "alex@example.com", "--product", "Example Asset API", "--solution", "SOL-EXAMPLE-ASSETS",
        ]);

    private Task<(int Exit, string Output, string Error)> DecideAsync(string requirement, params string[] options) =>
        RunAsync(["onboard", System, "decide", requirement, "--repo", Repo, "--json", .. options]);

    private Task<(int Exit, string Output, string Error)> RunAsync(string[] args, string input = "", bool terminal = false) =>
        RunAsync(args, input, terminal, Path.Combine(_root, "home"), _ct);
}
