using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Onboarding.Suppliers;

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

    internal const string SupplierAnswers = "Alex Example\nalex@example.com\nExample Asset API\nSUP-EXAMPLE\nExample Assets Ltd\nSOL-EXAMPLE-ASSETS\nAsset inventory\nCTR-EXAMPLE-ASSETS\nAsset inventory subscription\nHosts the asset inventory.\n\n";

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
    public async Task Ref_id_prompts_show_the_prefix_and_take_the_rest()
    {
        // The solution's first answer has a space, so it is asked again with the rule.
        var answers = "Alex Example\nalex@example.com\nExample Asset API\nexample\nExample Assets Ltd\nexample assets\nexample-assets\nAsset inventory\nrenewal\nAsset inventory subscription\nHosts the asset inventory.\n\n";

        var (exit, output, error) = await RunAsync(answers);

        exit.ShouldBe(ExitCodes.Ok, error);
        error.ShouldContain("Supplier ref_id: SUP-");
        error.ShouldContain("Solution ref_id (what the supplier provides): SOL-");
        error.ShouldContain("Contract ref_id: CTR-");
        error.ShouldContain("upper case letters, digits and hyphens");
        (output + error).ShouldContain("SUP-EXAMPLE is not approved yet");
        var drafted = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "third-parties", "sup-example.yaml"), _ct);
        drafted.ShouldContain("ref_id: \"SUP-EXAMPLE\"");
        drafted.ShouldContain("ref_id: \"SOL-EXAMPLE-ASSETS\"");
        drafted.ShouldContain("ref_id: \"CTR-RENEWAL\"");
    }

    [Fact]
    public async Task Scale_is_shown_before_each_level()
    {
        // Dependency 3 with its reason, penetration left, maturity 9 refused then 2 with its reason, trust and criticality left.
        var answers = SupplierAnswers + "3\nHolds the asset register.\n\n9\n2\nNo certificate seen yet.\n\n\n";

        var (exit, _, error) = await RunAsync(answers);

        exit.ShouldBe(ExitCodes.Ok, error);
        foreach (var (name, low, high) in SupplierProposal.Scales)
        {
            var scale = error.IndexOf(SupplierScales.Describe(name), StringComparison.Ordinal);
            scale.ShouldBeGreaterThan(-1, name);
            error.IndexOf($"{name} ({low} to {high}; empty to decide later): ", StringComparison.Ordinal).ShouldBeGreaterThan(scale, name);
        }

        error.ShouldContain("'9' is not a whole number from 1 to 4");
        var drafted = await File.ReadAllTextAsync(Path.Combine(Repo, "scope", "third-parties", "sup-example.yaml"), _ct);
        drafted.ShouldContain("# dependency: Holds the asset register. (decided by Alex Example)\ndependency: 3\n");
        drafted.ShouldContain("# penetration: to decide (0 to 4)\n");
        drafted.ShouldContain("# maturity: No certificate seen yet. (decided by Alex Example)\nmaturity: 2\n");
        drafted.ShouldContain("# trust: to decide (1 to 4)\n");
        drafted.ShouldContain("    # criticality: to decide (1 to 4)\n");
    }

    [Fact]
    public async Task Every_opening_question_has_an_example()
    {
        var (exit, _, error) = await RunAsync(SupplierAnswers);

        exit.ShouldBe(ExitCodes.Ok, error);
        OnboardGuide.Opening.Count.ShouldBe(11);
        foreach (var (question, hint) in OnboardGuide.Opening)
        {
            hint.ShouldContain("e.g. ");
            var shown = error.IndexOf(hint, StringComparison.Ordinal);
            shown.ShouldBeGreaterThan(-1, hint);
            error.IndexOf(question.Replace("{supplier}", "Example Assets Ltd", StringComparison.Ordinal).Replace("{system}", System, StringComparison.Ordinal), shown, StringComparison.Ordinal)
                .ShouldBeGreaterThan(shown, question);
        }
    }

    [Fact]
    public async Task It_says_what_is_about_to_happen_before_the_first_question()
    {
        var (exit, _, error) = await RunAsync(SupplierAnswers);

        exit.ShouldBe(ExitCodes.Ok, error);
        var intro = error.IndexOf("Onboarding EXAMPLE-ASSETS in three steps", StringComparison.Ordinal);
        intro.ShouldBeGreaterThan(-1, error);
        intro.ShouldBeLessThan(error.IndexOf("Your name", StringComparison.Ordinal));
        error.ShouldContain("Nothing reaches CISO Assistant until 'hound scope apply'");
    }

    [Fact]
    public async Task Repo_without_setup_is_refused_before_writing()
    {
        File.Delete(Path.Combine(Repo, "scope", "setup.yaml"));

        var (exit, _, error) = await RunAsync(SupplierAnswers);

        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("is not a compliance repo");
        error.ShouldContain("scope/setup.yaml");
        error.ShouldNotContain("Your name");
        Directory.Exists(Path.Combine(Repo, "onboarding", System)).ShouldBeFalse();
    }

    [Fact]
    public async Task Without_a_spec_it_says_what_is_missing_before_starting()
    {
        var (exit, _, error) = await RunAsync(SupplierAnswers, spec: false);

        exit.ShouldBe(ExitCodes.Ok, error);
        var note = error.IndexOf("No --spec was given", StringComparison.Ordinal);
        note.ShouldBeGreaterThan(-1, error);
        note.ShouldBeLessThan(error.IndexOf("Your name", StringComparison.Ordinal));
        (await RunAsync(SupplierAnswers.Replace("SUP-EXAMPLE", "SUP-OTHER", StringComparison.Ordinal))).Error.ShouldNotContain("No --spec was given");
    }

    [Fact]
    public async Task Help_lists_the_steps_and_points_to_the_guide()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await HoundCli.RunAsync(["onboard", "--help"], new StringReader(""), output, error, configureLogging: null, _ct);

        exit.ShouldBe(ExitCodes.Ok);
        output.ToString().ShouldContain("1. The supplier");
        output.ToString().ShouldContain("2. Applicability");
        output.ToString().ShouldContain("3. The files");
        output.ToString().ShouldContain("docs/onboarding.md");
    }

    [Fact]
    public async Task System_owner_is_asked_as_a_ciso_assistant_users_email_and_a_name_is_asked_again()
    {
        var answers = SupplierAnswers.Replace("Alex Example\nalex@example.com\n", "Alex Example\nAlex Example\nalex@example.com\n", StringComparison.Ordinal);

        var (exit, output, error) = await RunAsync(answers);

        exit.ShouldBe(ExitCodes.Ok, error);
        (output + error).ShouldContain("System owner's email, as a CISO Assistant user");
        (output + error).ShouldContain("'Alex Example' is not an email");
        Session()["owner"]!.GetValue<string>().ShouldBe("alex@example.com");
    }

    [Fact]
    public async Task A_vendor_document_is_kept_with_its_hash()
    {
        var pdf = Path.Combine(_root, "example-certificate.pdf");
        await File.WriteAllBytesAsync(pdf, "%PDF-1.4\n%example\n"u8.ToArray(), _ct);
        var answers = SupplierAnswers.Replace("Hosts the asset inventory.\n\n", $"Hosts the asset inventory.\n{pdf}\n\n", StringComparison.Ordinal);

        var (exit, output, error) = await RunAsync(answers);

        exit.ShouldBe(ExitCodes.Ok, error);
        (output + error).ShouldContain("Kept documents/suppliers/sup-example/example-certificate.pdf");
        (output + error).ShouldContain("(SHA-256 ");
        Session()["supplier_provides"]!.GetValue<string>().ShouldBe("Hosts the asset inventory.");
        (output + error).ShouldNotContain("model");
    }

    [Fact]
    public async Task A_pasted_control_character_is_dropped_from_an_answer()
    {
        var answers = SupplierAnswers.Replace("Alex Example\nalex@example.com\n", "Alex Example\n\u0016alex@example.com\n", StringComparison.Ordinal);

        var (exit, _, error) = await RunAsync(answers);

        exit.ShouldBe(ExitCodes.Ok, error);
        Session()["owner"]!.GetValue<string>().ShouldBe("alex@example.com");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Repo => Path.Combine(_root, "repo");

    private JsonNode Session() => JsonNode.Parse(File.ReadAllText(Path.Combine(Repo, "onboarding", System, "session.json")))!;

    private async Task<(int Exit, string Output, string Error)> RunAsync(string input, bool spec = true)
    {
        string[] args =
        [
            "onboard", System, "--repo", Repo, "--framework", Path.Combine(Repo, "frameworks", "example-framework.json"), "--group", "2",
            .. spec ? ["--spec", Path.Combine(ExampleRepoTests.RepoRoot, "examples", "apis", "example-asset-api.openapi.yaml")] : Array.Empty<string>(),
        ];
        using var reader = new StringReader(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal) { ["XDG_CONFIG_HOME"] = Path.Combine(_root, "home") };
        var exit = await HoundCli.RunAsync(args, reader, output, error, settings, _ct);
        return (exit, output.ToString(), error.ToString());
    }
}
