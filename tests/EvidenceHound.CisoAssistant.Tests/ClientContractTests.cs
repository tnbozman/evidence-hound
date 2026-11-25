using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
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
    public async Task Scope_documents_are_the_evidence_marked_by_the_scope()
    {
        Respond("/api/evidences/", 200, Page(
            new JsonObject { ["id"] = "ev-policy", ["ref_id"] = "identity-policy", ["description"] = "Signed policy.\n[hound scope 0123456789abcdef]" },
            new JsonObject { ["id"] = "ev-0001", ["ref_id"] = "ev-0001", ["description"] = "Collected by example-fake." }));

        var ids = (await _ca.Client.ListScopeDocumentIdsAsync(_ct)).Value();

        ids.ShouldBe(["ev-policy"]);
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
    public async Task Rows_by_ref_are_found_in_the_audit_with_their_urn_and_groups()
    {
        Respond("/api/requirement-assessments/", 200, Page(Row("ra-1", "bnd", "1"), Row("ra-2", "idn", "1", "2")));

        var result = await _ca.Client.FindRequirementAssessmentsAsync("EX-ESSENTIALS", RequirementKey.Ref("ACT-01"), _ct);

        result.ShouldBe(Result.Ok<IReadOnlyList<RemoteRequirementAssessment>>([
            new("ra-1", "urn:example:req:bnd:act-01", "ACT-01", ["1"]),
            new("ra-2", "urn:example:req:idn:act-01", "ACT-01", ["1", "2"]),
        ]), new RowsComparer());
        OnlyRequest().RawQuery.ShouldBe("?compliance_assessment__ref_id=EX-ESSENTIALS&requirement__ref_id=ACT-01");
    }

    [Fact]
    public async Task Rows_by_urn_are_selected_by_requirement_urn()
    {
        Respond("/api/requirement-assessments/", 200, Page(Row("ra-2", "idn")));

        var result = await _ca.Client.FindRequirementAssessmentsAsync("EX-ESSENTIALS", RequirementKey.Urn("urn:example:req:idn:act-01"), _ct);

        result.Match(rows => rows.Count, _ => 0).ShouldBe(1);
        OnlyRequest().RawQuery.ShouldBe("?compliance_assessment__ref_id=EX-ESSENTIALS&requirement__urn=urn%3Aexample%3Areq%3Aidn%3Aact-01");
    }

    [Fact]
    public async Task Requirement_with_no_row_is_not_found()
    {
        Respond("/api/requirement-assessments/", 200, Page());

        var result = await _ca.Client.FindRequirementAssessmentsAsync("EXAMPLE-BASELINE-2026", RequirementKey.Ref("EX-9.9"), _ct);

        Codes(result).ShouldBe(["ciso_assistant.not_found"]);
        Messages(result).Single().ShouldContain("requirement__ref_id=EX-9.9");
    }

    [Fact]
    public async Task Audit_selected_groups_are_read_from_its_write_data()
    {
        Respond("/api/compliance-assessments/", 200, Page(new JsonObject { ["id"] = "ca-1", ["selected_implementation_groups"] = new JsonArray("Maturity Level One") }));
        Respond("/api/compliance-assessments/ca-1/object/", 200, """{"id": "ca-1", "selected_implementation_groups": ["1", "2"]}""");

        var result = await _ca.Client.FindAuditAsync("EX-E8-2026", _ct);

        result.Match(audit => (audit.Id, string.Join(",", audit.SelectedGroups)), _ => ("", "")).ShouldBe(("ca-1", "1,2"));
        Requests()[0].RawQuery.ShouldBe("?ref_id=EX-E8-2026");
    }

    [Theory]
    [InlineData("in_progress", false, false)]
    [InlineData("in_review", false, true)]
    [InlineData("done", false, true)]
    [InlineData("in_progress", true, true)]
    public async Task Audit_in_review_done_or_locked_is_closed(string status, bool locked, bool closed)
    {
        Respond("/api/compliance-assessments/", 200, Page(new JsonObject { ["id"] = "ca-1" }));
        Respond("/api/compliance-assessments/ca-1/object/", 200, new JsonObject { ["id"] = "ca-1", ["status"] = status, ["is_locked"] = locked }.ToJsonString());

        var result = await _ca.Client.FindAuditAsync("EX-E8-Q1-2027FY-BACKUP", _ct);

        result.Match(audit => audit.Closed, _ => !closed).ShouldBe(closed);
    }

    [Fact]
    public async Task Audit_with_no_groups_selects_all()
    {
        Respond("/api/compliance-assessments/", 200, Page(new JsonObject { ["id"] = "ca-1" }));
        Respond("/api/compliance-assessments/ca-1/object/", 200, """{"id": "ca-1", "selected_implementation_groups": null}""");

        var result = await _ca.Client.FindAuditAsync("EX-E8-2026", _ct);

        result.Match(audit => audit.SelectedGroups.Count, _ => -1).ShouldBe(0);
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
    public async Task Link_evidence_adds_it_to_the_applied_controls_evidences()
    {
        Respond("/api/applied-controls/", 200, Page(new JsonObject { ["id"] = "ac-1", ["ref_id"] = "AC-AUTO" }));
        Respond("/api/applied-controls/ac-1/", 200, """{"id": "ac-1", "evidences": [{"id": "ev-old", "str": "older evidence"}]}""");
        Respond("/api/applied-controls/ac-1/", 200, """{"id": "ac-1"}""", "PATCH");

        var result = await _ca.Client.LinkEvidenceToAppliedControlAsync("AC-AUTO", "ev-new", _ct);

        result.ShouldBe(Result.Ok(Unit.Value));
        Requests()[0].RawQuery.ShouldBe("?ref_id=AC-AUTO");
        var patch = Requests().Single(r => r.Method == "PATCH");
        JsonNode.Parse(patch.Body!)!["evidences"]!.AsArray().Select(e => e!.GetValue<string>()).ShouldBe(["ev-old", "ev-new"]);
    }

    [Fact]
    public async Task Link_evidence_already_linked_sends_no_patch()
    {
        Respond("/api/applied-controls/", 200, Page(new JsonObject { ["id"] = "ac-1", ["ref_id"] = "AC-AUTO" }));
        Respond("/api/applied-controls/ac-1/", 200, """{"id": "ac-1", "evidences": ["ev-new"]}""");

        var result = await _ca.Client.LinkEvidenceToAppliedControlAsync("AC-AUTO", "ev-new", _ct);

        result.ShouldBe(Result.Ok(Unit.Value));
        Requests().ShouldNotContain(r => r.Method == "PATCH");
    }

    [Fact]
    public async Task Link_evidence_to_a_missing_control_is_not_found()
    {
        Respond("/api/applied-controls/", 200, Page());

        var result = await _ca.Client.LinkEvidenceToAppliedControlAsync("AC-NONE", "ev-new", _ct);

        Codes(result).ShouldBe(["ciso_assistant.not_found"]);
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
    public async Task Rows_of_an_audit_are_listed_with_their_state_across_pages()
    {
        var first = Row("ra-1", "bnd", "1");
        first["result"] = "compliant";
        first["observation"] = "Rule all_pass.";
        first["evidences"] = new JsonArray(new JsonObject { ["id"] = "ev-uuid-1", ["str"] = "mfa-admins.json" });
        var second = Row("ra-2", "idn");
        second["result"] = "not_assessed";
        second["observation"] = null;
        RespondPage("0", 2, first);
        RespondPage("1", 2, second);

        var result = await _ca.Client.ListRequirementAssessmentsAsync("EX-E8-2026", _ct);

        result.Match(rows => string.Join(";", rows.Select(r => $"{r.Id}|{r.Urn}|{r.RefId}|{string.Join(",", r.Groups)}|{r.Result}|{r.Observation}|{string.Join(",", r.EvidenceIds)}")), _ => "fail")
            .ShouldBe("ra-1|urn:example:req:bnd:act-01|ACT-01|1|Compliant|Rule all_pass.|ev-uuid-1;ra-2|urn:example:req:idn:act-01|ACT-01||NotAssessed||");
        Requests().Select(r => r.RawQuery).ShouldBe([
            "?compliance_assessment__ref_id=EX-E8-2026&limit=500&offset=0",
            "?compliance_assessment__ref_id=EX-E8-2026&limit=500&offset=1",
        ]);
    }

    [Fact]
    public async Task Rows_list_their_security_exceptions_and_exceptions_are_read_with_status_and_expiry()
    {
        var row = Row("ra-1", "bnd");
        row["security_exceptions"] = new JsonArray(new JsonObject { ["id"] = "se-1", ["str"] = "EXC-01" });
        RespondPage("0", 1, row);
        Respond("/api/security-exceptions/", 200, Page(
            new JsonObject { ["id"] = "se-1", ["ref_id"] = "EXC-01", ["name"] = "Legacy finance app without MFA", ["status"] = "approved", ["expiration_date"] = "2027-03-31" },
            new JsonObject { ["id"] = "se-2", ["ref_id"] = "", ["name"] = "Old kiosk", ["status"] = "draft", ["expiration_date"] = null }));

        var rows = await _ca.Client.ListRequirementAssessmentsAsync("EX-E8-2026", _ct);
        var exceptions = await _ca.Client.ListSecurityExceptionsAsync(_ct);

        rows.Match(r => r.ShouldHaveSingleItem().ExceptionIds, _ => []).ShouldBe(["se-1"]);
        exceptions.Match(e => e, _ => []).ShouldBe([
            new RemoteWaiver("se-1", "EXC-01", "Legacy finance app without MFA", "approved", new DateOnly(2027, 3, 31)),
            new RemoteWaiver("se-2", null, "Old kiosk", "draft", null),
        ]);
    }

    [Fact]
    public async Task A_bias_asset_assessments_are_read_with_their_recovery_flags_and_a_flag_is_set_with_its_evidence()
    {
        // s15-05: each run reads the BIA's asset assessments and sets a flag named by checks, adding the checks' evidence to those linked.
        Respond("/api/business-impact-analysis/", 200, Page(new JsonObject { ["id"] = "bia-1", ["ref_id"] = "EX-BIA-2026" }));
        Respond("/api/asset-assessments/", 200, Page(new JsonObject
        {
            ["id"] = "aa-1",
            ["asset"] = new JsonObject { ["id"] = "a-365", ["str"] = "Example 365" },
            ["recovery_documented"] = true,
            ["recovery_tested"] = false,
            ["recovery_targets_met"] = false,
            ["evidences"] = new JsonArray(new JsonObject { ["id"] = "ev-old", ["str"] = "Restore runbook" }),
        }));
        Respond("/api/asset-assessments/aa-1/", 200, """{"id": "aa-1"}""", "PATCH");

        var held = await _ca.Client.ListRecoveryAsync("EX-BIA-2026", _ct);
        var set = await _ca.Client.UpdateRecoveryAsync("aa-1", "recovery_tested", true, ["ev-old", "ev-new"], _ct);

        var assessment = held.Match(h => h, _ => new Dictionary<string, HeldRecovery>()).ShouldHaveSingleItem();
        assessment.Key.ShouldBe("Example 365");
        (assessment.Value.Id, assessment.Value.Flags["recovery_documented"], assessment.Value.Flags["recovery_tested"]).ShouldBe(("aa-1", true, false));
        assessment.Value.EvidenceIds.ShouldBe(["ev-old"]);
        set.ShouldBeOfType<Result<Unit>.Ok>();
        var patch = Requests().Single(r => r.Method == "PATCH");
        JsonNode.Parse(patch.Body!)!.ToJsonString().ShouldBe("""{"recovery_tested":true,"evidences":["ev-old","ev-new"]}""");
        Requests().ShouldContain(r => r.Method == "GET" && r.Path == "/api/asset-assessments/" && r.RawQuery!.Contains("bia=bia-1"));
    }

    [Fact]
    public async Task Listing_rows_stops_at_an_empty_page_whatever_the_count_says()
    {
        RespondPage("0", 5, Row("ra-1", "bnd"));
        RespondPage("1", 5);

        var result = await _ca.Client.ListRequirementAssessmentsAsync("EX-E8-2026", _ct);

        result.Match(rows => rows.Count, _ => -1).ShouldBe(1);
        Requests().Count.ShouldBe(2);
    }

    [Fact]
    public async Task Find_evidence_returns_the_existing_one_with_its_latest_hash()
    {
        Respond("/api/evidences/", 200, Page(new JsonObject { ["id"] = "ev-uuid-1", ["ref_id"] = "ev-0001" }));
        Respond("/api/evidence-revisions/", 200, Page(new JsonObject { ["id"] = "rev-2", ["version"] = 2, ["attachment_hash"] = new string('a', 64) }));

        var result = await _ca.Client.FindEvidenceAsync("ev-0001", _ct);

        result.Match(found => found.ShouldHaveSingleItem(), _ => new RemoteEvidence("", null)).ShouldBe(new RemoteEvidence("ev-uuid-1", new string('a', 64)));
    }

    [Fact]
    public async Task Find_evidence_that_does_not_exist_creates_nothing()
    {
        Respond("/api/evidences/", 200, Page(new JsonObject { ["id"] = "ev-uuid-9", ["ref_id"] = "ev-00011" }));

        var result = await _ca.Client.FindEvidenceAsync("ev-0001", _ct);

        result.Match(found => found.Count, _ => -1).ShouldBe(0);
        Requests().ShouldNotContain(r => r.Method == "POST");
    }

    [Theory]
    [InlineData("active", ControlState.Active)]
    [InlineData("in_progress", ControlState.InProgress)]
    [InlineData("To do", ControlState.ToDo)]
    public async Task Applied_control_status_is_read_by_ref_id(string status, ControlState expected)
    {
        Respond("/api/applied-controls/", 200, Page(new JsonObject { ["id"] = "ac-1", ["ref_id"] = "AC-MFA", ["status"] = status }));

        var result = await _ca.Client.FindAppliedControlStatusAsync("AC-MFA", _ct);

        result.ShouldBe(Result.Ok(expected));
        OnlyRequest().RawQuery.ShouldBe("?ref_id=AC-MFA");
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
    public async Task Json_bodies_have_a_content_length_because_the_api_ignores_chunked_bodies()
    {
        Respond("/api/evidences/", 200, Page());
        Respond("/api/evidences/", 201, """{"id": "ev-uuid-1"}""", "POST");
        Respond("/api/requirement-assessments/ra-1/", 200, """{"id": "ra-1"}""", "PATCH");

        await _ca.Client.FindOrCreateEvidenceAsync("ev-0001", "mfa-admins.json", _ct);
        await _ca.Client.UpdateRequirementAssessmentAsync("ra-1", Values(), _ct);

        var withBodies = Requests().Where(r => r.Method != "GET").ToList();
        withBodies.Count.ShouldBe(2);
        withBodies.ShouldAllBe(r => r.Headers!.ContainsKey("Content-Length") && !r.Headers.ContainsKey("Transfer-Encoding"));
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

    private static JsonObject Row(string id, string domain, params string[] groups) => new()
    {
        ["id"] = id,
        ["requirement"] = new JsonObject
        {
            ["id"] = $"node-{domain}",
            ["urn"] = $"urn:example:req:{domain}:act-01",
            ["ref_id"] = "ACT-01",
            ["implementation_groups"] = groups.Length == 0 ? null : new JsonArray([.. groups.Select(g => JsonValue.Create(g))]),
        },
    };

    private void RespondPage(string offset, int count, params JsonObject[] results) =>
        _ca.Server.Given(Request.Create().WithPath("/api/requirement-assessments/").WithParam("offset", offset).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["count"] = count, ["next"] = null, ["results"] = new JsonArray(results) }.ToJsonString()));

    private static RequirementAssessmentValues Values() =>
        new(ComplianceResult.PartiallyCompliant, "2 of 3 checks pass.", ["ev-uuid-1", "ev-uuid-2"]);

    private static string Page(params JsonObject[] results) =>
        new JsonObject { ["count"] = results.Length, ["next"] = null, ["results"] = new JsonArray(results) }.ToJsonString();

    private static List<string> Codes<T>(Result<T> result)
        where T : notnull => result.Match(_ => [], problems => problems.Select(p => p.Code).ToList());

    private static List<string> Messages<T>(Result<T> result)
        where T : notnull => result.Match(_ => [], problems => problems.Select(p => p.Message).ToList());

    private sealed class RowsComparer : IEqualityComparer<Result<IReadOnlyList<RemoteRequirementAssessment>>>
    {
        public bool Equals(Result<IReadOnlyList<RemoteRequirementAssessment>>? x, Result<IReadOnlyList<RemoteRequirementAssessment>>? y) =>
            Flat(x) == Flat(y);

        public int GetHashCode(Result<IReadOnlyList<RemoteRequirementAssessment>> obj) => Flat(obj).GetHashCode(StringComparison.Ordinal);

        private static string Flat(Result<IReadOnlyList<RemoteRequirementAssessment>>? result) =>
            result is null ? "" : result.Match(rows => string.Join(";", rows.Select(r => $"{r.Id}|{r.Urn}|{r.RefId}|{string.Join(",", r.Groups)}")), _ => "fail");
    }

    private List<IRequestMessage> Requests() => [.. _ca.Server.LogEntries.Select(entry => entry.RequestMessage!)];

    private IRequestMessage OnlyRequest() => Requests().ShouldHaveSingleItem();

    private void Respond(string path, int status, string body, string method = "GET") =>
        _ca.Server.Given(Request.Create().WithPath(path).UsingMethod(method))
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));
}
