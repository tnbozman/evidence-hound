using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// <c>hound onboard &lt;system&gt;</c> without a model: supplier first, then one decision per requirement, saved after each and resumed after a
/// stop. Uses a three-requirement framework so every answer is typed.
/// </summary>
public sealed class OnboardSystemCommandTests : IDisposable
{
    internal const string System = "EXAMPLE-ASSETS";

    internal const string Framework = """
        {
          "library_urn": "urn:example:library",
          "framework_urn": "urn:example:framework",
          "version": 1,
          "groups": [{"ref_id": "1", "name": "Level one"}, {"ref_id": "2", "name": "Level two"}],
          "nodes": [
            {"urn": "urn:example:req:a", "ref_id": "EX-1", "parent_urn": null, "groups": ["1", "2"], "title": "Patch internet-facing services within two weeks."},
            {"urn": "urn:example:req:b", "ref_id": "EX-2", "parent_urn": null, "groups": ["2"], "title": "Multi-factor authentication for privileged users."},
            {"urn": "urn:example:req:c", "ref_id": "EX-3", "parent_urn": null, "groups": ["2"], "title": "Back up important data daily."},
            {"urn": "urn:example:req:d", "ref_id": "EX-4", "parent_urn": null, "groups": ["3"], "title": "Level three only."}
          ]
        }
        """;

    internal const string SupplierAnswers = "Alex Example\nAlex Example\nExample Asset API\nSUP-EXAMPLE\nExample Assets Ltd\nSOL-EXAMPLE-ASSETS\nAsset inventory\nCTR-EXAMPLE-ASSETS\nAsset inventory subscription\nHosts the asset inventory.\n\n";

    private readonly string _root = Directory.CreateTempSubdirectory("hound-onboard-system-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public OnboardSystemCommandTests()
    {
        TestFiles.CopyExampleRepo(Repo);
        File.WriteAllText(Path.Combine(Repo, "frameworks", "example-framework.json"), Framework);
    }

    [Fact]
    public async Task Unapproved_supplier_stops_and_resumes_into_one_decision_per_requirement()
    {
        var (exit, output, error) = await RunAsync(SupplierAnswers);

        exit.ShouldBe(ExitCodes.Ok, error);
        (output + error).ShouldContain("SUP-EXAMPLE is not approved yet");
        var supplierFile = Path.Combine(Repo, "scope", "third-parties", "sup-example.yaml");
        var drafted = await File.ReadAllTextAsync(supplierFile, _ct);
        drafted.ShouldContain("status: draft");
        drafted.ShouldContain("perimeter: \"Corporate IT\"");
        Session()["stage"]!.GetValue<string>().ShouldBe("awaiting_supplier");

        // Still not approved: nothing more is asked.
        (await RunAsync("")).Exit.ShouldBe(ExitCodes.Ok);
        Session()["stage"]!.GetValue<string>().ShouldBe("awaiting_supplier");

        // The person approves the supplier: the contract is made active.
        await File.WriteAllTextAsync(supplierFile, drafted.Replace("status: draft", "status: active", StringComparison.Ordinal), _ct);

        // First requirement decided, then save and stop.
        (exit, output, error) = await RunAsync("m\nAsk the owner for the patch report.\ns\n");
        exit.ShouldBe(ExitCodes.Ok, error);
        error.ShouldContain("[1/3] EX-1");
        error.ShouldContain("GET /v1/devices (paged by pageToken)");
        Session()["decisions"]!.AsArray().Count.ShouldBe(1);

        // Resumed: not applicable needs a justification and an approver; an empty one is asked again.
        (exit, output, error) = await RunAsync("n\nNo privileged users.\n\nOnly the supplier administers it.\nSam Example\nc\nGET /v1/devices shows backups.\n");
        exit.ShouldBe(ExitCodes.Ok, error);
        error.ShouldNotContain("[1/3]");
        error.ShouldContain("[2/3] EX-2");
        output.ShouldContain("All 3 requirements decided: 1 API check(s), 1 manual, 1 not applicable, 0 for another system.");
        var session = Session();
        session["stage"]!.GetValue<string>().ShouldBe("decided");
        var decisions = session["decisions"]!.AsArray();
        decisions.Select(d => d!["key"]!.GetValue<string>()).ShouldBe(["EX-1", "EX-2", "EX-3"]);
        decisions[1]!["approver"]!.GetValue<string>().ShouldBe("Sam Example");
        decisions[1]!["accepted_by"]!.GetValue<string>().ShouldBe("Alex Example");

        // Decided: the system's audit and the onboarding record are written into the working copy, nothing else.
        var audit = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "audits", "example-assets-example-framework.yaml"), _ct);
        audit.ShouldContain("ref_id: \"EXAMPLE-ASSETS-EXAMPLE-FRAMEWORK\"");
        audit.ShouldContain("domain: \"Example Pty Ltd\"");
        audit.ShouldContain("result: not_applicable");
        audit.ShouldContain("systems: [\"EXAMPLE-ASSETS\"]");

        // The system is declared in its domain with the solution it is, so scope plan holds it back until the supplier is approved.
        var setup = ScopeLoader.Parse(new ScopeSource("setup.yaml", await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "setup.yaml"), _ct)), null, []);
        setup.Problems.ShouldBeEmpty();
        var system = setup.Scope.Setup.Domains.Single(d => d.Name == "Example Pty Ltd").Systems.Single(x => x.Name == System);
        (system.Type, system.Solution?.Value, system.Description).ShouldBe((SystemType.Primary, "SOL-EXAMPLE-ASSETS", "Example Asset API, from Example Assets Ltd."));
        output.ShouldContain("scope/setup.yaml");
        output.ShouldContain("Conclude EA-SUP-EXAMPLE");
        var record = Directory.GetFiles(Path.Combine(Repo, "onboarding"), "example-assets-*.md").ShouldHaveSingleItem();
        (await File.ReadAllTextAsync(record, _ct)).ShouldContain("| EX-2 | (none) | not_applicable | No privileged users. | Alex Example | Only the supplier administers it. Approved by Sam Example. |");
        output.ShouldContain("Wrote scope/audits/example-assets-example-framework.yaml");
        output.ShouldContain("No mappings/example-framework.yaml to add manual rows to");
        output.ShouldContain("hound scope validate");

        // Run again: already written, nothing is asked or overwritten.
        (exit, output, _) = await RunAsync("");
        exit.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("already written");
    }

    [Fact]
    public async Task Without_an_onboarding_config_a_model_session_is_refused()
    {
        var (exit, _, error) = await RunAsync(SupplierAnswers, noModel: false);

        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("Run 'hound onboard config' to choose a model");
        Directory.Exists(Path.Combine(Repo, "onboarding", System)).ShouldBeFalse();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Repo => Path.Combine(_root, "repo");

    private JsonNode Session() => JsonNode.Parse(File.ReadAllText(Path.Combine(Repo, "onboarding", System, "session.json")))!;

    private async Task<(int Exit, string Output, string Error)> RunAsync(string input, bool noModel = true)
    {
        string[] args =
        [
            "onboard", System, "--repo", Repo, "--framework", Path.Combine(Repo, "frameworks", "example-framework.json"), "--group", "2",
            "--spec", Path.Combine(ExampleRepoTests.RepoRoot, "examples", "apis", "example-asset-api.openapi.yaml"), .. noModel ? new[] { "--no-model" } : [],
        ];
        using var reader = new StringReader(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal) { ["XDG_CONFIG_HOME"] = Path.Combine(_root, "home") };
        var exit = await HoundCli.RunAsync(args, reader, output, error, settings, _ct);
        return (exit, output.ToString(), error.ToString());
    }
}
