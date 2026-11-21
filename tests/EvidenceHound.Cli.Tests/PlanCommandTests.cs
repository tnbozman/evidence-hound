using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Cli.Tests;

/// <summary><c>hound plan</c> without <c>--dry-run</c>: read CISO Assistant and plan only what differs, sending nothing.</summary>
public sealed class PlanCommandTests : IDisposable
{
    private static readonly string Token = "example-token-for-cli-tests";
    private static readonly string[] Refs = ["EX-1.1", "EX-1.2", "EX-2.1", "EX-3.1", "EX-4.1"];
    private static readonly string KnownHash = "4f2a1c0b7e9d3a5f6c8b0e2d4a6f8c1e3b5d7f9a0c2e4b6d8f1a3c5e7b9d0f2a";
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly WireMockServer _ca = WireMockServer.Start();

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        _ca.Dispose();
    }

    [Fact]
    public async Task Plan_reads_ciso_assistant_and_lists_only_what_differs_without_changing_anything()
    {
        FakeCurrentState();

        var exitCode = await Plan(new() { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = Token });

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var plan = JsonNode.Parse(_output.ToString())!;
        plan["run_id"]!.GetValue<string>().ShouldMatch("^[0-9]{8}T[0-9]{6}Z$");
        var audit = plan["audits"]!.AsArray().ShouldHaveSingleItem()!;
        audit["compliance_assessment"]!.GetValue<string>().ShouldBe("EXAMPLE-BASELINE-2026");
        (audit["changed"]!.GetValue<int>(), audit["unchanged"]!.GetValue<int>(), audit["drift"]!.GetValue<int>()).ShouldBe((5, 0, 0));
        var messages = plan["messages"]!.AsArray();
        messages.Where(m => m!["message_type"]!.GetValue<string>() == "upload_attachment").Select(m => m!["selector"]!["ref_id"]!.GetValue<string>()).ShouldNotContain("ev-0001");
        messages.Where(m => m!["message_type"]!.GetValue<string>() == "update_applied_control").Select(m => m!["selector"]!["ref_id"]!.GetValue<string>()).ShouldBe(["AC-MFA"]);
        _ca.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
    }

    [Fact]
    public async Task Plan_without_dry_run_needs_the_ciso_assistant_token()
    {
        var exitCode = await Plan(new() { ["HOUND_CA_URL"] = $"{_ca.Url}/api/" });

        exitCode.ShouldBe(ExitCodes.Configuration);
        _logs.Messages.ShouldContain(m => m.Contains("HOUND_CA_TOKEN", StringComparison.Ordinal));
        _ca.LogEntries.ShouldBeEmpty();
    }

    private Task<int> Plan(Dictionary<string, string?> settings) =>
        HoundCli.RunAsync(
            [
                "plan",
                "--config", Path.Combine(AppContext.BaseDirectory, "files", "examples", "example-pty-ltd.yaml"),
                "--checks", Path.Combine(AppContext.BaseDirectory, "files", "fixtures", "checks-sample.json"),
            ],
            _output,
            _error,
            logging => logging.AddProvider(_logs),
            settings,
            TestContext.Current.CancellationToken);

    /// <summary>Five unassessed rows, evidence ev-0001 already uploaded with the same hash, AC-BACKUP already active and AC-MFA not yet in progress.</summary>
    private void FakeCurrentState()
    {
        var rows = new JsonArray([.. Refs.Select(r => (JsonNode)new JsonObject
        {
            ["id"] = $"ra-{r}",
            ["requirement"] = new JsonObject { ["urn"] = $"urn:example:risk:req_node:example-baseline:{r.ToLowerInvariant()}", ["ref_id"] = r, ["implementation_groups"] = null },
            ["result"] = "not_assessed",
            ["observation"] = null,
            ["evidences"] = new JsonArray(),
        })]);
        _ca.Given(Request.Create().WithPath("/api/compliance-assessments/").UsingGet()).RespondWith(Json(200, """{"count": 1, "results": [{"id": "ca-uuid"}]}"""));
        _ca.Given(Request.Create().WithPath("/api/compliance-assessments/ca-uuid/object/").UsingGet()).RespondWith(Json(200, """{"id": "ca-uuid", "selected_implementation_groups": []}"""));
        _ca.Given(Request.Create().WithPath("/api/requirement-assessments/").UsingGet())
            .RespondWith(Json(200, new JsonObject { ["count"] = 5, ["results"] = rows }.ToJsonString()));
        _ca.Given(Request.Create().WithPath("/api/evidences/").UsingGet()).RespondWith(Json(200, """{"count": 0, "results": []}"""));
        _ca.Given(Request.Create().WithPath("/api/evidences/").UsingGet().WithParam("search", "ev-0001")).AtPriority(1)
            .RespondWith(Json(200, """{"count": 1, "results": [{"id": "ev-uuid-1", "ref_id": "ev-0001"}]}"""));
        _ca.Given(Request.Create().WithPath("/api/evidence-revisions/").UsingGet())
            .RespondWith(Json(200, new JsonObject { ["count"] = 1, ["results"] = new JsonArray(new JsonObject { ["attachment_hash"] = KnownHash }) }.ToJsonString()));
        _ca.Given(Request.Create().WithPath("/api/applied-controls/").UsingGet().WithParam("ref_id", "AC-BACKUP"))
            .RespondWith(Json(200, """{"count": 1, "results": [{"id": "ac-1", "ref_id": "AC-BACKUP", "status": "active"}]}"""));
        _ca.Given(Request.Create().WithPath("/api/applied-controls/").UsingGet().WithParam("ref_id", "AC-MFA"))
            .RespondWith(Json(200, """{"count": 1, "results": [{"id": "ac-2", "ref_id": "AC-MFA", "status": "to_do"}]}"""));
    }

    private static IResponseBuilder Json(int status, string body) =>
        Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body);
}
