using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Cli.Tests;

/// <summary>s17-05: <c>hound sprs score</c> works out the DoD Assessment Methodology score from the CMMC Level 2 audit in CISO Assistant.</summary>
public sealed class SprsScoreCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly WireMockServer _ca = WireMockServer.Start();
    private readonly string _dir = Directory.CreateTempSubdirectory("hound-sprs-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public SprsScoreCommandTests()
    {
        TestFiles.CopyExampleRepo(_dir);
        // Two practices met, one not, the plan in place; every other practice is missing from this small audit.
        _ca.Given(Request.Create().WithPath("/api/requirement-assessments/").WithParam("compliance_assessment__ref_id", "EX-CMMC-2026").UsingGet())
            .RespondWith(Response.Create().WithBody(List(
                Row("ra-1", "AC.L1-3.1.1", "compliant"),
                Row("ra-2", "AC.L1-3.1.2", "non_compliant"),
                Row("ra-3", "IA.L2-3.5.3", "partially_compliant"),
                Row("ra-4", "CA.L2-3.12.4", "compliant")).ToJsonString()));
        Respond("GET", "/api/evidences/", List());
        Respond("POST", "/api/evidences/", new JsonObject { ["id"] = "ev-sprs" });
        Respond("POST", "/api/evidence-revisions/", new JsonObject { ["id"] = "rev-1" });
        Respond("GET", "/api/applied-controls/", List(new JsonObject { ["id"] = "ac-sprs", ["ref_id"] = "AC-SPRS", ["name"] = "SPRS score kept current", ["status"] = "active", ["evidences"] = new JsonArray() }));
        Respond("GET", "/api/applied-controls/ac-sprs/", new JsonObject { ["id"] = "ac-sprs", ["evidences"] = new JsonArray() });
        Respond("PATCH", "/api/applied-controls/ac-sprs/", new JsonObject { ["id"] = "ac-sprs" });
    }

    [Fact]
    public async Task Prints_the_score_and_each_deduction_with_its_practice()
    {
        var exitCode = await Run("sprs", "score", "--audit", "EX-CMMC-2026");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var text = _output.ToString();
        // 313 points in all; only 3.1.1 (5) and the plan are met.
        text.ShouldContain("SPRS score for EX-CMMC-2026 on 2026-12-02: -198 (from 110, NIST SP 800-171 DoD Assessment Methodology 1.2.1)");
        text.ShouldContain("  -5  3.1.2  AC.L1-3.1.2  non_compliant");
        text.ShouldContain("  -5  3.5.3  IA.L2-3.5.3  partially_compliant  3 points instead of 5 if MFA is implemented for remote and privileged users, but not for general users; a person decides.");
        text.ShouldContain("  -1  3.1.3  not in the audit");
        text.ShouldNotContain("system security plan");
        _ca.LogEntries.ShouldNotContain(e => e.RequestMessage!.Method != "GET");
    }

    [Fact]
    public async Task Json_lists_the_score_and_deductions()
    {
        var exitCode = await Run("sprs", "score", "--audit", "EX-CMMC-2026", "--format", "json");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var json = JsonNode.Parse(_output.ToString())!;
        json["audit"]!.GetValue<string>().ShouldBe("EX-CMMC-2026");
        json["score"]!.GetValue<int>().ShouldBe(-198);
        json["date"]!.GetValue<string>().ShouldBe("2026-12-02");
        json["ssp_missing"]!.GetValue<bool>().ShouldBeFalse();
        var first = json["deductions"]!.AsArray()[0]!;
        (first["requirement"]!.GetValue<string>(), first["practice"]!.GetValue<string>(), first["points"]!.GetValue<int>()).ShouldBe(("3.1.2", "AC.L1-3.1.2", 5));
    }

    [Fact]
    public async Task With_a_control_the_score_is_uploaded_as_evidence_on_it()
    {
        var exitCode = await Run("sprs", "score", "--audit", "EX-CMMC-2026", "--control", "AC-SPRS");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var created = _ca.LogEntries.Single(e => e.RequestMessage!.Method == "POST" && e.RequestMessage.Path == "/api/evidences/");
        JsonNode.Parse(created.RequestMessage!.Body!)!["ref_id"]!.GetValue<string>().ShouldBe("hound-sprs/EX-CMMC-2026-2026-12-02");
        _ca.LogEntries.ShouldContain(e => e.RequestMessage!.Method == "POST" && e.RequestMessage.Path == "/api/evidence-revisions/");
        JsonNode.Parse(_ca.LogEntries.Single(e => e.RequestMessage!.Method == "PATCH").RequestMessage!.Body!)!["evidences"]![0]!.GetValue<string>().ShouldBe("ev-sprs");
        _output.ToString().ShouldContain("uploaded hound-sprs/EX-CMMC-2026-2026-12-02 to applied control AC-SPRS");
    }

    [Fact]
    public async Task Without_a_compliant_system_security_plan_the_score_says_an_assessment_cannot_be_completed()
    {
        _ca.Given(Request.Create().WithPath("/api/requirement-assessments/").WithParam("compliance_assessment__ref_id", "EX-CMMC-2026").UsingGet()).AtPriority(-1)
            .RespondWith(Response.Create().WithBody(List(Row("ra-1", "AC.L1-3.1.1", "compliant")).ToJsonString()));

        var exitCode = await Run("sprs", "score", "--audit", "EX-CMMC-2026");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        _output.ToString().ShouldContain("No system security plan (3.12.4) is recorded as compliant, so an assessment could not be completed (DFARS 252.204-7012).");
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
        [.. args, "--config", Path.Combine(_dir, "hound.yaml")],
        _output,
        _error,
        logging => logging.AddProvider(_logs),
        new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = "example-token-for-cli-tests", ["HOUND_TODAY"] = "2026-12-02" },
        _ct);

    private static JsonObject Row(string id, string refId, string result) => new()
    {
        ["id"] = id,
        ["result"] = result,
        ["requirement"] = new JsonObject
        {
            ["urn"] = "urn:intuitem:risk:req_node:cmmc-2.0:" + refId.ToLowerInvariant(),
            ["ref_id"] = refId,
            ["implementation_groups"] = new JsonArray(refId.Contains(".L1-", StringComparison.Ordinal) ? "L1" : "L2"),
        },
    };

    private static JsonObject List(params JsonObject[] items) => new() { ["count"] = items.Length, ["results"] = new JsonArray([.. items]) };

    private void Respond(string method, string path, JsonObject body) =>
        _ca.Given(Request.Create().WithPath(path).UsingMethod(method)).RespondWith(Response.Create().WithStatusCode(method == "POST" ? 201 : 200).WithBody(body.ToJsonString()));
}
