using System.Text.Json.Nodes;
using EvidenceHound.Core.Reporting;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Cli.Tests;

/// <summary>s12-04: <c>hound report snapshot</c> reads CISO Assistant once and writes the period's posture snapshot.</summary>
public sealed class ReportSnapshotCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly WireMockServer _ca = WireMockServer.Start();
    private readonly string _dir = Directory.CreateTempSubdirectory("hound-report-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public ReportSnapshotCommandTests()
    {
        TestFiles.CopyExampleRepo(_dir);
        var config = Path.Combine(_dir, "hound.yaml");
        File.WriteAllText(config, File.ReadAllText(config).Replace("/var/lib/evidence-hound/evidence", Path.Combine(_dir, "evidence"), StringComparison.Ordinal));

        Audit("EX-E8-2026", "ca-e8", "in_progress", Row("ra-1", "urn:intuitem:risk:req_node:essential-eight-2023:mfa:ism-1504", "ISM-1504", ["1", "2", "3"], "compliant"), Row("ra-2", "urn:intuitem:risk:req_node:essential-eight-2023:rb:ism-1511", "ISM-1511", ["1", "2", "3"], "non_compliant"));
        Audit("EX-AI6-2026", "ca-ai6", "done", Row("ra-3", "urn:evidence-hound:risk:req_node:ai6:ai6-1.1", "AI6-1.1", ["F", "I"], "partially_compliant"));
        foreach (var resource in new[] { "security-exceptions", "findings", "incidents", "risk-scenarios", "task-nodes", "entity-assessments" })
        {
            Respond("GET", $"/api/{resource}/", List());
        }

        Respond("GET", "/api/risk-acceptances/", List(new JsonObject { ["id"] = "rk-1", ["ref_id"] = "RA-KIOSK", ["name"] = "Legacy kiosk", ["state"] = "accepted", ["expiry_date"] = "2027-05-01" }));
        Respond("GET", "/api/applied-controls/", List(new JsonObject { ["id"] = "ac-rep", ["ref_id"] = "AC-REPORTING", ["name"] = "Security reporting", ["status"] = "active", ["evidences"] = new JsonArray() }));
        Respond("GET", "/api/applied-controls/ac-rep/", new JsonObject { ["id"] = "ac-rep", ["evidences"] = new JsonArray() });
        Respond("PATCH", "/api/applied-controls/ac-rep/", new JsonObject { ["id"] = "ac-rep" });
        Respond("GET", "/api/evidences/", List());
        Respond("POST", "/api/evidences/", new JsonObject { ["id"] = "ev-rep" });
        Respond("POST", "/api/evidence-revisions/", new JsonObject { ["id"] = "rev-1" });
    }

    [Fact]
    public async Task Writes_the_period_snapshot_to_the_evidence_store_per_programme()
    {
        var exitCode = await Run("report", "snapshot", "--period", "2026-11");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var file = Path.Combine(_dir, "evidence", "reports", "posture-2026-11.json");
        File.Exists(file).ShouldBeTrue(_output.ToString());
        var snapshot = PostureSnapshotJson.Read(await File.ReadAllTextAsync(file, _ct), file).Match(s => s, ps => throw new InvalidOperationException(ps[0].Message));
        snapshot.Period.ToString().ShouldBe("2026-11");
        snapshot.Programmes.Select(p => p.Id).ShouldBe(["essential-eight", "ai6"]);
        var e8 = snapshot.Programmes[0];
        e8.Counts.First(c => c.Group == "2" && c.ParentUrn is null).ShouldBe(new ResultCount("2", null, 1, 0, 1, 0, 0));
        e8.Link.ShouldBe($"{_ca.Url}/compliance-assessments/ca-e8");
        e8.Closed.ShouldBeFalse();
        snapshot.Programmes[1].Closed.ShouldBeTrue();
        snapshot.Programmes[1].Counts[0].Group.ShouldBe("F");
        snapshot.Programmes[1].Register!.Of.ShouldBe("ai_systems");
        snapshot.Acceptances.Single().RefId.ShouldBe("RA-KIOSK");

        // Coverage from the config's target mapping for each audit, with the config's manual collectors counted as manual.
        e8.Coverage.Where(c => c.Group == "2").Sum(c => c.Automated + c.Manual + c.Uncovered).ShouldBeGreaterThan(0);
        e8.Coverage.Sum(c => c.Manual).ShouldBeGreaterThan(0);
        snapshot.Programmes[1].Coverage.Select(c => c.Group).Distinct().ShouldBe(["F"]);
        _ca.LogEntries.ShouldNotContain(e => e.RequestMessage!.Method != "GET");
    }

    [Fact]
    public async Task With_a_control_the_snapshot_is_uploaded_as_evidence_on_it()
    {
        var exitCode = await Run("report", "snapshot", "--period", "2026-11", "--control", "AC-REPORTING");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        _ca.LogEntries.ShouldContain(e => e.RequestMessage!.Method == "POST" && e.RequestMessage.Path == "/api/evidence-revisions/");
        var link = _ca.LogEntries.Single(e => e.RequestMessage!.Method == "PATCH");
        JsonNode.Parse(link.RequestMessage!.Body!)!["evidences"]![0]!.GetValue<string>().ShouldBe("ev-rep");
        var created = _ca.LogEntries.Single(e => e.RequestMessage!.Method == "POST" && e.RequestMessage.Path == "/api/evidences/");
        JsonNode.Parse(created.RequestMessage!.Body!)!["ref_id"]!.GetValue<string>().ShouldBe("hound-report/posture-2026-11");
    }

    [Fact]
    public async Task A_bad_period_is_refused()
    {
        var exitCode = await Run("report", "snapshot", "--period", "November");

        exitCode.ShouldBe(ExitCodes.Configuration);
        _ca.LogEntries.ShouldBeEmpty();
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        _ca.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private Task<int> Run(params string[] args) => HoundCli.RunAsync(
        [.. args, "--scope", Path.Combine(_dir, "scope"), "--config", Path.Combine(_dir, "hound.yaml")],
        _output,
        _error,
        logging => logging.AddProvider(_logs),
        new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = "example-token-for-cli-tests", ["HOUND_TODAY"] = "2026-12-02" },
        _ct);

    private void Audit(string refId, string id, string status, params JsonObject[] rows)
    {
        _ca.Given(Request.Create().WithPath("/api/compliance-assessments/").WithParam("ref_id", refId).UsingGet())
            .RespondWith(Response.Create().WithBody(List(new JsonObject { ["id"] = id, ["ref_id"] = refId }).ToJsonString()));
        Respond("GET", $"/api/compliance-assessments/{id}/object/", new JsonObject { ["id"] = id, ["status"] = status, ["selected_implementation_groups"] = new JsonArray() });
        _ca.Given(Request.Create().WithPath("/api/requirement-assessments/").WithParam("compliance_assessment__ref_id", refId).UsingGet())
            .RespondWith(Response.Create().WithBody(List(rows).ToJsonString()));
    }

    private static JsonObject Row(string id, string urn, string refId, string[] groups, string result) => new()
    {
        ["id"] = id,
        ["result"] = result,
        ["requirement"] = new JsonObject { ["urn"] = urn, ["ref_id"] = refId, ["implementation_groups"] = new JsonArray([.. groups.Select(g => (JsonNode)g)]) },
    };

    private static JsonObject List(params JsonObject[] items) => new() { ["count"] = items.Length, ["results"] = new JsonArray([.. items]) };

    private void Respond(string method, string path, JsonObject body) =>
        _ca.Given(Request.Create().WithPath(path).UsingMethod(method)).RespondWith(Response.Create().WithStatusCode(method == "POST" ? 201 : 200).WithBody(body.ToJsonString()));
}
