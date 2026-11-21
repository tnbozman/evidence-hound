using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EvidenceHound.CisoAssistant.Tests;

public sealed class ClientContractTests : IDisposable
{
    private static readonly string Scenario = "flaky";
    private readonly FakeCisoAssistant _ca = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public void Dispose() => _ca.Dispose();

    [Fact]
    public async Task Sends_token_header()
    {
        Respond("/api/health/", 200, """{"status": "ok"}""");

        var result = await _ca.Client.CheckHealthAsync(_ct);

        result.ShouldBe(Result.Ok(Unit.Value));
        OnlyRequest().Headers!["Authorization"].ShouldBe([$"Token {FakeCisoAssistant.Token}"]);
    }

    [Fact]
    public async Task Retries_on_503_then_succeeds()
    {
        _ca.Server.Given(Request.Create().WithPath("/api/health/").UsingGet())
            .InScenario(Scenario).WillSetStateTo("recovered")
            .RespondWith(Response.Create().WithStatusCode(503));
        _ca.Server.Given(Request.Create().WithPath("/api/health/").UsingGet())
            .InScenario(Scenario).WhenStateIs("recovered")
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"status": "ok"}"""));

        var result = await _ca.Client.CheckHealthAsync(_ct);

        result.ShouldBe(Result.Ok(Unit.Value));
        _ca.Server.LogEntries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Retries_on_429_then_succeeds()
    {
        _ca.Server.Given(Request.Create().WithPath("/api/health/").UsingGet())
            .InScenario(Scenario).WillSetStateTo("recovered")
            .RespondWith(Response.Create().WithStatusCode(429));
        _ca.Server.Given(Request.Create().WithPath("/api/health/").UsingGet())
            .InScenario(Scenario).WhenStateIs("recovered")
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"status": "ok"}"""));

        (await _ca.Client.CheckHealthAsync(_ct)).ShouldBe(Result.Ok(Unit.Value));
    }

    [Fact]
    public async Task Maps_401_to_auth_error()
    {
        Respond("/api/health/", 401, """{"detail": "Invalid token."}""");

        var result = await _ca.Client.CheckHealthAsync(_ct);

        Codes(result).ShouldBe(["ciso_assistant.auth"]);
    }

    [Fact]
    public async Task Maps_403_to_auth_error()
    {
        Respond("/api/applied-controls/", 403, """{"detail": "You do not have permission to perform this action."}""");

        var result = await _ca.Client.UpdateAppliedControlAsync("AC-MFA", ControlState.Active, _ct);

        Codes(result).ShouldBe(["ciso_assistant.auth"]);
    }

    [Fact]
    public async Task Maps_404_to_not_found()
    {
        Respond("/api/requirement-assessments/ra-1/", 404, """{"detail": "Not found."}""", "PATCH");

        var result = await _ca.Client.UpdateRequirementAssessmentAsync("ra-1", Values(), _ct);

        Codes(result).ShouldBe(["ciso_assistant.not_found"]);
    }

    [Fact]
    public async Task Maps_400_to_rejected_with_the_api_message()
    {
        Respond("/api/requirement-assessments/ra-1/", 400, """{"result": ["\"maybe\" is not a valid choice."]}""", "PATCH");

        var result = await _ca.Client.UpdateRequirementAssessmentAsync("ra-1", Values(), _ct);

        Codes(result).ShouldBe(["ciso_assistant.rejected"]);
        Messages(result).Single().ShouldContain("is not a valid choice");
    }

    [Fact]
    public async Task Maps_500_after_retries_to_unavailable()
    {
        Respond("/api/health/", 500, "Server Error");

        var result = await _ca.Client.CheckHealthAsync(_ct);

        Codes(result).ShouldBe(["ciso_assistant.unavailable"]);
        _ca.Server.LogEntries.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task Maps_an_unreachable_server_to_unavailable()
    {
        _ca.Server.Stop();

        var result = await _ca.Client.CheckHealthAsync(_ct);

        Codes(result).ShouldBe(["ciso_assistant.unavailable"]);
        Messages(result).Single().ShouldNotContain(FakeCisoAssistant.Token);
    }

    [Fact]
    public async Task Selector_builds_expected_query_string()
    {
        Respond("/api/requirement-assessments/", 200, Page(new JsonObject { ["id"] = "ra-1" }));

        var result = await _ca.Client.FindRequirementAssessmentAsync("EXAMPLE-BASELINE-2026", "EX-1.1", _ct);

        result.ShouldBe(Result.Ok("ra-1"));
        var query = OnlyRequest().RawQuery;
        query.ShouldBe("?compliance_assessment__ref_id=EXAMPLE-BASELINE-2026&requirement__ref_id=EX-1.1");
    }

    [Fact]
    public async Task Lookup_with_no_match_is_not_found()
    {
        Respond("/api/requirement-assessments/", 200, Page());

        var result = await _ca.Client.FindRequirementAssessmentAsync("EXAMPLE-BASELINE-2026", "EX-9.9", _ct);

        Codes(result).ShouldBe(["ciso_assistant.not_found"]);
        Messages(result).Single().ShouldContain("requirement__ref_id=EX-9.9");
    }

    [Fact]
    public async Task Lookup_with_several_matches_is_ambiguous()
    {
        Respond("/api/requirement-assessments/", 200, Page(new JsonObject { ["id"] = "ra-1" }, new JsonObject { ["id"] = "ra-2" }));

        var result = await _ca.Client.FindRequirementAssessmentAsync("EXAMPLE-BASELINE-2026", "EX-1.1", _ct);

        Codes(result).ShouldBe(["ciso_assistant.ambiguous"]);
    }

    [Fact]
    public async Task Update_requirement_assessment_patches_result_observation_and_evidence_ids()
    {
        Respond("/api/requirement-assessments/ra-1/", 200, """{"id": "ra-1"}""", "PATCH");

        var result = await _ca.Client.UpdateRequirementAssessmentAsync("ra-1", Values(), _ct);

        result.ShouldBe(Result.Ok(Unit.Value));
        var body = JsonNode.Parse(OnlyRequest().Body!)!;
        body["result"]!.GetValue<string>().ShouldBe("partially_compliant");
        body["observation"]!.GetValue<string>().ShouldBe("2 of 3 checks pass.");
        body["evidences"]!.AsArray().Select(e => e!.GetValue<string>()).ShouldBe(["ev-uuid-1", "ev-uuid-2"]);
    }

    [Fact]
    public async Task Update_applied_control_finds_it_by_ref_id_then_patches_status()
    {
        Respond("/api/applied-controls/", 200, Page(new JsonObject { ["id"] = "ac-1", ["ref_id"] = "AC-MFA" }));
        Respond("/api/applied-controls/ac-1/", 200, """{"id": "ac-1"}""", "PATCH");

        var result = await _ca.Client.UpdateAppliedControlAsync("AC-MFA", ControlState.InProgress, _ct);

        result.ShouldBe(Result.Ok(Unit.Value));
        Requests()[0].RawQuery.ShouldBe("?ref_id=AC-MFA");
        JsonNode.Parse(Requests()[^1].Body!)!["status"]!.GetValue<string>().ShouldBe("in_progress");
    }

    [Fact]
    public async Task Find_or_create_evidence_returns_the_existing_one_with_its_latest_hash()
    {
        Respond("/api/evidences/", 200, Page(
            new JsonObject { ["id"] = "ev-uuid-9", ["ref_id"] = "ev-00011" },
            new JsonObject { ["id"] = "ev-uuid-1", ["ref_id"] = "ev-0001" }));
        Respond("/api/evidence-revisions/", 200, Page(new JsonObject { ["id"] = "rev-2", ["version"] = 2, ["attachment_hash"] = new string('a', 64) }));

        var result = await _ca.Client.FindOrCreateEvidenceAsync("ev-0001", "mfa-admins.json", _ct);

        result.ShouldBe(Result.Ok(new RemoteEvidence("ev-uuid-1", new string('a', 64))));
        Requests()[0].RawQuery.ShouldBe("?search=ev-0001");
        Requests()[^1].RawQuery.ShouldBe("?evidence=ev-uuid-1&ordering=-version&limit=1");
    }

    [Fact]
    public async Task Find_or_create_evidence_creates_a_missing_one_with_name_and_ref_id()
    {
        Respond("/api/evidences/", 200, Page());
        Respond("/api/evidences/", 201, """{"id": "ev-uuid-1"}""", "POST");

        var result = await _ca.Client.FindOrCreateEvidenceAsync("ev-0001", "mfa-admins.json", _ct);

        result.ShouldBe(Result.Ok(new RemoteEvidence("ev-uuid-1", null)));
        var body = JsonNode.Parse(Requests()[^1].Body!)!;
        body["ref_id"]!.GetValue<string>().ShouldBe("ev-0001");
        body["name"]!.GetValue<string>().ShouldBe("mfa-admins.json");
    }

    [Fact]
    public async Task Upload_evidence_revision_posts_the_file_as_multipart()
    {
        Respond("/api/evidence-revisions/", 201, """{"id": "rev-3"}""", "POST");

        var result = await _ca.Client.UploadEvidenceRevisionAsync("ev-uuid-1", new EvidenceFile("mfa-admins.json", Encoding.UTF8.GetBytes("{\"admins\": 4}")), _ct);

        result.ShouldBe(Result.Ok(Unit.Value));
        var request = OnlyRequest();
        request.Headers!["Content-Type"][0].ShouldStartWith("multipart/form-data");
        request.Body!.ShouldContain("name=evidence");
        request.Body!.ShouldContain("ev-uuid-1");
        request.Body!.ShouldContain("filename=mfa-admins.json");
        request.Body!.ShouldContain("{\"admins\": 4}");
    }

    [Fact]
    public async Task Evidence_ref_id_matching_several_is_ambiguous()
    {
        Respond("/api/evidences/", 200, Page(new JsonObject { ["id"] = "a", ["ref_id"] = "ev-0001" }, new JsonObject { ["id"] = "b", ["ref_id"] = "ev-0001" }));

        var result = await _ca.Client.FindOrCreateEvidenceAsync("ev-0001", "mfa-admins.json", _ct);

        Codes(result).ShouldBe(["ciso_assistant.ambiguous"]);
    }

    [Fact]
    public async Task Evidence_search_failure_is_returned()
    {
        Respond("/api/evidences/", 401, """{"detail": "Invalid token."}""");

        var result = await _ca.Client.FindOrCreateEvidenceAsync("ev-0001", "mfa-admins.json", _ct);

        Codes(result).ShouldBe(["ciso_assistant.auth"]);
    }

    [Fact]
    public async Task Long_rejection_messages_are_shortened()
    {
        Respond("/api/requirement-assessments/ra-1/", 400, new string('x', 2000), "PATCH");

        var result = await _ca.Client.UpdateRequirementAssessmentAsync("ra-1", Values(), _ct);

        Messages(result).Single().ShouldEndWith(new string('x', 500) + "...");
    }

    private static RequirementAssessmentValues Values() =>
        new(ComplianceResult.PartiallyCompliant, "2 of 3 checks pass.", ["ev-uuid-1", "ev-uuid-2"]);

    private static string Page(params JsonObject[] results) =>
        new JsonObject { ["count"] = results.Length, ["next"] = null, ["results"] = new JsonArray(results) }.ToJsonString();

    private static List<string> Codes<T>(Result<T> result)
        where T : notnull => result.Match(_ => [], problems => problems.Select(p => p.Code).ToList());

    private static List<string> Messages<T>(Result<T> result)
        where T : notnull => result.Match(_ => [], problems => problems.Select(p => p.Message).ToList());

    private List<IRequestMessage> Requests() => [.. _ca.Server.LogEntries.Select(entry => entry.RequestMessage!)];

    private IRequestMessage OnlyRequest() => Requests().ShouldHaveSingleItem();

    private void Respond(string path, int status, string body, string method = "GET") =>
        _ca.Server.Given(Request.Create().WithPath(path).UsingMethod(method))
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));
}
