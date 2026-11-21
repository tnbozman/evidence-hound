using System.Text.Json.Nodes;
using EvidenceHound.Core.Frameworks;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Cli.Tests;

/// <summary><c>hound scope plan</c> reads CISO Assistant and prints what applying the scope would change; <c>hound scope apply</c> writes it.</summary>
public sealed class ScopePlanCommandTests : IDisposable
{
    private static readonly string Token = "example-token-for-cli-tests";
    private static readonly FrameworkSnapshot Snapshot = new(
        "urn:example:risk:library:example-baseline",
        "urn:example:risk:framework:example-baseline",
        1,
        [new ImplementationGroup("1", "Level one")],
        [new SnapshotNode("urn:example:risk:req_node:example-baseline:ex-1.1", "EX-1.1", null, ["1"], "Every account uses multi-factor authentication")]);

    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly WireMockServer _ca = WireMockServer.Start();
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-scope-plan-tests-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private string ScopeDir => Path.Combine(_workDir, "scope");

    [Fact]
    public async Task Plan_against_an_empty_instance_lists_every_create_as_markdown_and_sends_only_gets()
    {
        await WriteScopeAsync();
        EmptyInstance();

        var exitCode = await Plan();

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var markdown = _output.ToString();
        markdown.ShouldStartWith("## Scope plan\n");
        markdown.ShouldContain("7 to create, 0 to update, 0 to delete. No drift.");
        markdown.ShouldContain("| Create | framework | urn:example:risk:framework:example-baseline |");
        markdown.ShouldContain("| Create | domain | Example Pty Ltd |");
        markdown.ShouldContain("| Create | perimeter | Example Pty Ltd/Example baseline | name: Example baseline<br>domain: Example Pty Ltd |");
        markdown.ShouldContain("| Create | system | Example 365 |");
        markdown.ShouldContain("| Create | audit | EXAMPLE-BASELINE-2026 |");
        markdown.ShouldContain("| Create | control | AC-MFA |");
        markdown.IndexOf("| domain |", StringComparison.Ordinal).ShouldBeLessThan(markdown.IndexOf("| control |", StringComparison.Ordinal));
        _ca.LogEntries.ShouldNotBeEmpty();
        _ca.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
    }

    [Fact]
    public async Task Plan_as_json_lists_changes_field_by_field_and_drift()
    {
        await WriteScopeAsync();
        EmptyInstance(perimeters: [new JsonObject { ["id"] = "p-old", ["folder"] = new JsonObject { ["id"] = "f-global", ["str"] = "Global" } }]);
        Object("perimeters", "p-old", new JsonObject { ["id"] = "p-old", ["name"] = "Old perimeter", ["ref_id"] = "PER-OLD", ["folder"] = "f-global" });

        var exitCode = await Plan("--format", "json");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var plan = JsonNode.Parse(_output.ToString())!;
        var system = plan["changes"]!.AsArray().Single(c => c!["kind"]!.GetValue<string>() == "system")!;
        (system["action"]!.GetValue<string>(), system["key"]!.GetValue<string>()).ShouldBe(("create", "Example 365"));
        system["fields"]!.AsArray().Select(f => (f!["field"]!.GetValue<string>(), (string?)f["old"], f["new"]!.GetValue<string>()))
            .ShouldContain(("type", (string?)null, "primary"));
        var drift = plan["drift"]!.AsArray().ShouldHaveSingleItem()!;
        (drift["kind"]!.GetValue<string>(), drift["key"]!.GetValue<string>(), drift["reason"]!.GetValue<string>(), drift["managed"]!.GetValue<bool>())
            .ShouldBe(("perimeter", "Global/Old perimeter", "not_in_scope", false));
    }

    [Fact]
    public async Task Plan_refuses_an_invalid_scope_without_reading_ciso_assistant()
    {
        await WriteScopeAsync(ScopeCommandTests.Audit.Replace("systems: [Example 365]", "systems: [Example tablets]", StringComparison.Ordinal));

        var exitCode = await Plan();

        exitCode.ShouldBe(ExitCodes.Configuration);
        _logs.Messages.ShouldContain(m => m.Contains("scope.reference_unknown", StringComparison.Ordinal));
        _ca.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Plan_needs_the_ciso_assistant_token()
    {
        await WriteScopeAsync();

        var exitCode = await Plan(settings: new() { ["HOUND_CA_URL"] = $"{_ca.Url}/api/" });

        exitCode.ShouldBe(ExitCodes.Configuration);
        _logs.Messages.ShouldContain(m => m.Contains("HOUND_CA_TOKEN", StringComparison.Ordinal));
        _ca.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Apply_dry_run_prints_the_plan_and_writes_nothing()
    {
        await WriteScopeAsync();
        EmptyInstance();

        var exitCode = await Run("apply", "--dry-run");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        _output.ToString().ShouldStartWith("## Scope plan\n");
        _ca.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
    }

    [Fact]
    public async Task Apply_writes_domains_perimeters_and_systems_and_reports_each_write()
    {
        await WriteScopeAsync();
        EmptyInstance(folders: [new JsonObject { ["id"] = "f-ex", ["name"] = "Example Pty Ltd", ["parent_folder"] = new JsonObject { ["id"] = "f-global", ["str"] = "Global" } }]);
        Object("folders", "f-ex", new JsonObject { ["id"] = "f-ex", ["name"] = "Example Pty Ltd", ["parent_folder"] = "f-global" });
        Accept("POST", "/api/perimeters/");
        Accept("POST", "/api/assets/");
        Accept("PATCH", "/api/folders/f-ex/");

        var exitCode = await Run("apply");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var report = JsonNode.Parse(_output.ToString())!;
        (report["written"]!.GetValue<int>(), report["failed"]!.GetValue<int>(), report["skipped"]!.GetValue<int>(), report["not_applied"]!.GetValue<int>()).ShouldBe((3, 0, 0, 4));
        report["changes"]!.AsArray().Select(c => (c!["action"]!.GetValue<string>(), c["kind"]!.GetValue<string>(), c["outcome"]!.GetValue<string>())).ShouldBe(
            [("update", "domain", "written"), ("create", "perimeter", "written"), ("create", "system", "written")]);
        _ca.LogEntries.Where(e => e.RequestMessage!.Method != "GET").Select(e => (e.RequestMessage!.Method, e.RequestMessage.Path)).ShouldBe(
            [("PATCH", "/api/folders/f-ex/"), ("POST", "/api/perimeters/"), ("POST", "/api/assets/")]);
    }

    [Fact]
    public async Task Apply_with_a_failed_write_reports_it_and_exits_with_publish_failed()
    {
        await WriteScopeAsync();
        EmptyInstance();
        _ca.Given(Request.Create().WithPath("/api/folders/").UsingPost()).RespondWith(Response.Create().WithStatusCode(400).WithBody("""{"name": ["This field is required."]}"""));

        var exitCode = await Run("apply");

        exitCode.ShouldBe(ExitCodes.PublishFailed);
        var report = JsonNode.Parse(_output.ToString())!;
        (report["written"]!.GetValue<int>(), report["failed"]!.GetValue<int>(), report["skipped"]!.GetValue<int>()).ShouldBe((0, 1, 2));
    }

    [Fact]
    public async Task Apply_refuses_an_invalid_scope_without_reading_ciso_assistant()
    {
        await WriteScopeAsync(ScopeCommandTests.Audit.Replace("systems: [Example 365]", "systems: [Example tablets]", StringComparison.Ordinal));

        var exitCode = await Run("apply");

        exitCode.ShouldBe(ExitCodes.Configuration);
        _ca.LogEntries.ShouldBeEmpty();
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        _ca.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }

    private Task<int> Plan(params string[] args) => Plan(null, args);

    private Task<int> Run(string command, params string[] args) =>
        HoundCli.RunAsync(
            ["scope", command, ScopeDir, .. args],
            _output,
            _error,
            logging => logging.AddProvider(_logs),
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = Token },
            _ct);

    private void Accept(string method, string path) =>
        _ca.Given(Request.Create().WithPath(path).UsingMethod(method))
            .RespondWith(Response.Create().WithStatusCode(method == "POST" ? 201 : 200).WithBody(new JsonObject { ["id"] = "new-id" }.ToJsonString()));

    private Task<int> Plan(Dictionary<string, string?>? settings, params string[] args) =>
        HoundCli.RunAsync(
            ["scope", "plan", ScopeDir, .. args],
            _output,
            _error,
            logging => logging.AddProvider(_logs),
            settings ?? new() { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = Token },
            _ct);

    /// <summary>A CISO Assistant with only its Global folder: nothing the scope declares exists yet.</summary>
    private void EmptyInstance(JsonObject[]? perimeters = null, JsonObject[]? folders = null)
    {
        List("folders", [new JsonObject { ["id"] = "f-global", ["name"] = "Global", ["parent_folder"] = null }, .. folders ?? []]);
        foreach (var resource in new[] { "users", "actors", "frameworks", "loaded-libraries", "assets", "compliance-assessments", "evidences", "applied-controls", "requirement-assessments" })
        {
            List(resource);
        }

        List("perimeters", perimeters ?? []);
    }

    private void List(string resource, params JsonObject[] items) =>
        _ca.Given(Request.Create().WithPath($"/api/{resource}/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["count"] = items.Length, ["results"] = new JsonArray(items) }.ToJsonString()));

    private void Object(string resource, string id, JsonObject body) =>
        _ca.Given(Request.Create().WithPath($"/api/{resource}/{id}/object/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(body.ToJsonString()));

    private async Task WriteScopeAsync(string audit = ScopeCommandTests.Audit)
    {
        Directory.CreateDirectory(Path.Combine(ScopeDir, "audits"));
        Directory.CreateDirectory(Path.Combine(_workDir, "frameworks"));
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "setup.yaml"), ScopeCommandTests.Setup, _ct);
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "controls.yaml"), ScopeCommandTests.Controls, _ct);
        await File.WriteAllTextAsync(Path.Combine(ScopeDir, "audits", "example-baseline-2026.yaml"), audit, _ct);
        await File.WriteAllTextAsync(Path.Combine(_workDir, "frameworks", "example-baseline.json"), FrameworkSnapshotJson.Write(Snapshot), _ct);
    }
}
