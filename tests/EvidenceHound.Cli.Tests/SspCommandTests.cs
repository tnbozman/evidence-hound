using System.Text.Json.Nodes;
using EvidenceHound.Core.Frameworks;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Cli.Tests;

/// <summary>s11-02: <c>hound ssp build</c> reads CISO Assistant once and prints the plan's model, each value with its source.</summary>
public sealed class SspCommandTests : IDisposable
{
    private static readonly string Token = "example-token-for-cli-tests";

    private static readonly string Ssp = """
        ssps:
          - programme: baseline
            profile: ism
            system_name: Example corporate IT
            perimeter: Example baseline
            version: "0.1"
            roles:
              system_owner: it@example.com
              authorising_official: it@example.com
              security_contact: it@example.com
            sections:
              purpose: ssp/purpose.md
              environment: ssp/environment.md
              management: ssp/management.md
              laws_and_policies: ssp/laws-and-policies.md
        """;

    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly WireMockServer _ca = WireMockServer.Start();
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-ssp-tests-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private string ScopeDir => Path.Combine(_workDir, "scope");

    [Fact]
    public async Task Builds_the_plan_from_what_ciso_assistant_holds_and_sends_only_gets()
    {
        await WriteScopeAsync(Ssp);
        Instance();

        var exit = await Build("--json");

        exit.ShouldBe(ExitCodes.Ok, _error.ToString());
        var plan = JsonNode.Parse(_output.ToString())!;
        plan["programme"]!.GetValue<string>().ShouldBe("baseline");
        var system = plan["inventory"]!.AsArray().ShouldHaveSingleItem()!;
        (system["name"]!["value"]!.GetValue<string>(), system["owner"]!["value"]!.GetValue<string>()).ShouldBe(("Example 365", "it@example.com"));
        system["owner"]!["source"]!["kind"]!.GetValue<string>().ShouldBe("ciso_assistant");
        _ca.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
    }

    [Fact]
    public async Task Strict_passes_without_gaps_and_the_poam_is_printed()
    {
        await WriteScopeAsync(Ssp);
        Instance();

        (await Build("--strict")).ShouldBe(ExitCodes.Ok, _error.ToString());
        _output.ToString().ShouldContain("No gaps.");
        _output.GetStringBuilder().Clear();

        var exit = await HoundCli.RunAsync(
            ["ssp", "poam", "--programme", "baseline", "--scope", ScopeDir, "--json"],
            _output,
            _error,
            logging => logging.AddProvider(_logs),
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = Token },
            _ct);

        exit.ShouldBe(ExitCodes.Ok, _error.ToString());
        JsonNode.Parse(_output.ToString())!["items"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Prints_the_sp_800_18_layout_with_section_files_and_the_annex()
    {
        await WriteScopeAsync(Ssp);
        Directory.CreateDirectory(Path.Combine(_workDir, "ssp"));
        await File.WriteAllTextAsync(Path.Combine(_workDir, "ssp", "purpose.md"), "Example corporate IT runs the company's mail and files.\n", _ct);
        Instance();

        (await Build()).ShouldBe(ExitCodes.Ok, _error.ToString());

        var markdown = _output.ToString();
        markdown.ShouldContain("## 9. General description and purpose\n\nExample corporate IT runs the company's mail and files.");
        markdown.ShouldContain("## 13. Security controls (annex)");
        markdown.ShouldContain("| EX-1.1 |");
        markdown.ShouldContain("_Section `environment` is not written yet._");
    }

    [Fact]
    public async Task Json_carries_the_annex_with_poam_links()
    {
        await WriteScopeAsync(Ssp);
        Instance();

        (await Build("--json")).ShouldBe(ExitCodes.Ok, _error.ToString());

        var plan = JsonNode.Parse(_output.ToString())!;
        var row = plan["annex"]!.AsArray().ShouldHaveSingleItem()!;
        row["control"]!.GetValue<string>().ShouldBe("EX-1.1");
        row["asd_status"]!["value"]!.GetValue<string>().ShouldBe("Not assessed"); // The fixture holds no row for EX-1.1.
        row["responsible_entity"]!["value"]!.GetValue<string>().ShouldBe("Consumer");
        row["control_types"]!.AsArray().ShouldBeEmpty();
        row["maturity_levels"]!.AsArray().Select(l => l!.GetValue<string>()).ShouldBe(["1"]);
        row["status"]!["value"].ShouldNotBeNull();
        plan["classification"]!["value"].ShouldBeNull();
    }

    [Fact]
    public async Task A_scope_problem_stops_before_ciso_assistant_is_read()
    {
        await WriteScopeAsync(Ssp.Replace("perimeter: Example baseline", "perimeter: Nowhere", StringComparison.Ordinal));

        var exit = await Build();

        exit.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("Nowhere");
        _ca.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_programme_is_refused()
    {
        await WriteScopeAsync(Ssp);

        var exit = await BuildFor("no-such-programme");

        exit.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("no-such-programme");
        _ca.LogEntries.ShouldBeEmpty();
    }

    public void Dispose()
    {
        _ca.Stop();
        _ca.Dispose();
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }

    private Task<int> Build(params string[] args) => BuildFor("baseline", args);

    private Task<int> BuildFor(string programme, params string[] args) =>
        HoundCli.RunAsync(
            ["ssp", "build", "--programme", programme, "--scope", ScopeDir, .. args],
            _output,
            _error,
            logging => logging.AddProvider(_logs),
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = Token },
            _ct);

    /// <summary>A CISO Assistant holding the example domain and its one system, owned by the IT user.</summary>
    private void Instance()
    {
        List("folders",
            new JsonObject { ["id"] = "f-global", ["name"] = "Global", ["parent_folder"] = null },
            new JsonObject { ["id"] = "f-ex", ["name"] = "Example Pty Ltd", ["parent_folder"] = new JsonObject { ["id"] = "f-global", ["str"] = "Global" } });
        Object("folders", "f-ex", new JsonObject { ["id"] = "f-ex", ["name"] = "Example Pty Ltd", ["parent_folder"] = "f-global" });
        List("users", new JsonObject { ["id"] = "u-it", ["email"] = "it@example.com" });
        List("actors", new JsonObject { ["id"] = "act-it", ["str"] = "it@example.com", ["type"] = "user", ["specific"] = new JsonObject { ["id"] = "u-it", ["str"] = "it@example.com" } });
        List("assets", new JsonObject { ["id"] = "a-365", ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "folder" } });
        Object("assets", "a-365", new JsonObject { ["id"] = "a-365", ["name"] = "Example 365", ["type"] = "PR", ["folder"] = "f-ex", ["parent_assets"] = new JsonArray(), ["owner"] = new JsonArray("act-it") });
        foreach (var resource in new[] { "frameworks", "loaded-libraries", "compliance-assessments", "evidences", "applied-controls", "requirement-assessments", "teams", "roles", "role-assignments", "user-groups", "perimeters", "entities", "solutions", "contracts", "representatives", "entity-assessments", "tiers" })
        {
            List(resource);
        }
    }

    private void List(string resource, params JsonObject[] items) =>
        _ca.Given(Request.Create().WithPath($"/api/{resource}/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["count"] = items.Length, ["results"] = new JsonArray(items) }.ToJsonString()));

    private void Object(string resource, string id, JsonObject body) =>
        _ca.Given(Request.Create().WithPath($"/api/{resource}/{id}/object/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(body.ToJsonString()));

    private async Task WriteScopeAsync(string ssp)
    {
        Directory.CreateDirectory(Path.Combine(ScopeDir, "audits"));
        Directory.CreateDirectory(Path.Combine(_workDir, "frameworks"));
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "setup.yaml"), ScopeCommandTests.Setup + """

            programmes:
              - id: baseline
                name: Example baseline
                framework: urn:example:risk:framework:example-baseline
                groups: ["1"]
                audit: EXAMPLE-BASELINE-2026

            """, _ct);
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "controls.yaml"), ScopeCommandTests.Controls, _ct);
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "audits", "example-baseline-2026.yaml"), ScopeCommandTests.Audit, _ct);
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "access.yaml"), "users:\n  - email: it@example.com\nroles:\n  - role: approver\n    domain: Example Pty Ltd\n    users: [it@example.com]\n", _ct);
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "ssp.yaml"), ssp, _ct);
        await File.WriteAllTextAsync(Path.Combine(_workDir, "frameworks", "example-baseline.json"), FrameworkSnapshotJson.Write(new FrameworkSnapshot(
            "urn:example:risk:library:example-baseline",
            "urn:example:risk:framework:example-baseline",
            1,
            [new ImplementationGroup("1", "Level one")],
            [new SnapshotNode("urn:example:risk:req_node:example-baseline:ex-1.1", "EX-1.1", null, ["1"], "Every account uses multi-factor authentication")])), _ct);
    }
}
