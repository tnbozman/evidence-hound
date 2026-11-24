using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant.Tests;

/// <summary>
/// s10-04: the evidence plan as v4.3.4 holds it (recorded from the CI probe of /api/task-templates/): evidence items, recurring task
/// templates assigned to teams through their actors, and a team's <c>team_email</c>.
/// </summary>
public sealed class TaskTemplateClientTests : IDisposable
{
    private const string Runbook = "# Admin review\n\nExport the admin list and send it to the GRC team.\n";

    private readonly FakeCisoAssistant _ca = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public TaskTemplateClientTests()
    {
        void Respond(string method, string path, JsonObject body) => SupplierClientTests.Respond(_ca, method, path, body);
        static JsonObject Ref(string id, string str) => new() { ["id"] = id, ["str"] = str };
        foreach (var resource in new[] { "users", "frameworks", "loaded-libraries", "assets", "perimeters" })
        {
            Respond("GET", $"/api/{resource}/", SupplierClientTests.List());
        }

        Respond("GET", "/api/folders/", SupplierClientTests.List(
            new JsonObject { ["id"] = "f-global", ["name"] = "Global", ["parent_folder"] = null },
            new JsonObject { ["id"] = "f-example", ["name"] = "Example Pty Ltd", ["parent_folder"] = "f-global", ["content_type"] = "DO" }));
        Respond("GET", "/api/folders/f-example/object/", new JsonObject { ["id"] = "f-example", ["name"] = "Example Pty Ltd", ["parent_folder"] = "f-global", ["description"] = "" });
        Respond("GET", "/api/teams/", SupplierClientTests.List(
            new JsonObject { ["id"] = "t-grc", ["name"] = "GRC team", ["folder"] = Ref("f-example", "Example Pty Ltd"), ["members"] = new JsonArray(), ["deputies"] = new JsonArray() },
            new JsonObject
            {
                ["id"] = "t-owners", ["name"] = "Example 365 owners", ["folder"] = Ref("f-example", "Example Pty Ltd"), ["team_email"] = "owners-365@example.com",
                ["members"] = new JsonArray(), ["deputies"] = new JsonArray(),
            }));
        Respond("GET", "/api/actors/", SupplierClientTests.List(
            new JsonObject { ["id"] = "a-grc", ["type"] = "team", ["specific"] = "t-grc", ["str"] = "GRC team" },
            new JsonObject { ["id"] = "a-owners", ["type"] = "team", ["specific"] = "t-owners", ["str"] = "Example 365 owners" }));
        Respond("GET", "/api/filtering-labels/", SupplierClientTests.List(new JsonObject { ["id"] = "l-manual", ["label"] = "method:manual" }));
        Respond("POST", "/api/filtering-labels/", new JsonObject { ["id"] = "l-new", ["label"] = "collector:example-m365" });
        Respond("GET", "/api/applied-controls/", SupplierClientTests.List(new JsonObject { ["id"] = "ac-mfa", ["ref_id"] = "AC-MFA", ["name"] = "Multi-factor authentication", ["folder"] = Ref("f-example", "Example Pty Ltd") }));
        Respond("GET", "/api/evidences/", SupplierClientTests.List(
            new JsonObject
            {
                ["id"] = "ev-admin", ["ref_id"] = "EV-ADMIN-REVIEW", ["name"] = "EV-ADMIN-REVIEW: Multi-factor authentication", ["folder"] = Ref("f-example", "Example Pty Ltd"),
                ["owner"] = new JsonArray(Ref("a-owners", "Example 365 owners")), ["filtering_labels"] = new JsonArray(Ref("l-manual", "method:manual")),
                ["applied_controls"] = new JsonArray(Ref("ac-mfa", "Multi-factor authentication")), ["description"] = "[hound scope 0123456789abcdef]",
            },
            new JsonObject { ["id"] = "ev-doc", ["ref_id"] = "identity-policy", ["name"] = "Identity policy", ["folder"] = Ref("f-example", "Example Pty Ltd") }));
        Respond("GET", "/api/evidences/ev-doc/object/", new JsonObject { ["id"] = "ev-doc", ["ref_id"] = "identity-policy", ["name"] = "Identity policy", ["folder"] = "f-example", ["description"] = "" });
        Respond("GET", "/api/evidences/ev-admin/object/", new JsonObject { ["id"] = "ev-admin", ["ref_id"] = "EV-ADMIN-REVIEW", ["folder"] = "f-example", ["description"] = "[hound scope 0123456789abcdef]" });
        Respond("GET", "/api/evidence-revisions/", SupplierClientTests.List());
        Respond("GET", "/api/compliance-assessments/", SupplierClientTests.List(new JsonObject { ["id"] = "ca-1", ["ref_id"] = "EXAMPLE-BASELINE-2026", ["folder"] = Ref("f-example", "Example Pty Ltd") }));
        Respond("GET", "/api/compliance-assessments/ca-1/object/", new JsonObject { ["id"] = "ca-1", ["ref_id"] = "EXAMPLE-BASELINE-2026", ["selected_implementation_groups"] = new JsonArray("1") });
        Respond("GET", "/api/requirement-assessments/", SupplierClientTests.List(new JsonObject
        {
            ["id"] = "ra-12", ["compliance_assessment"] = Ref("ca-1", "EXAMPLE-BASELINE-2026"),
            ["requirement"] = new JsonObject { ["urn"] = "urn:example:risk:req_node:example-baseline:ex-1.2", ["ref_id"] = "EX-1.2", ["implementation_groups"] = new JsonArray("1", "2") },
        }));
        Respond("GET", "/api/task-templates/", SupplierClientTests.List(new JsonObject
        {
            ["id"] = "tt-1", ["ref_id"] = "EV-ADMIN-REVIEW", ["name"] = "EV-ADMIN-REVIEW: Multi-factor authentication", ["description"] = Runbook + "\n[hound scope 0123456789abcdef]",
            ["folder"] = Ref("f-example", "Example Pty Ltd"), ["assigned_to"] = new JsonArray(Ref("a-owners", "Example 365 owners"), Ref("a-grc", "GRC team")),
            ["is_recurrent"] = true, ["task_date"] = "2027-02-23", ["enabled"] = true,
            ["schedule"] = new JsonObject { ["interval"] = 3, ["frequency"] = "MONTHLY", ["days_of_week"] = new JsonArray(), ["weeks_of_month"] = new JsonArray(), ["months_of_year"] = new JsonArray() },
            ["evidences"] = new JsonArray(Ref("ev-admin", "EV-ADMIN-REVIEW")), ["applied_controls"] = new JsonArray(Ref("ac-mfa", "Multi-factor authentication")),
            ["requirement_assessments"] = new JsonArray(Ref("ra-12", "EX-1.2")),
            ["next_occurrence"] = "2027-02-23", ["next_occurrence_status"] = "pending", ["last_occurrence_status"] = "completed", ["status"] = null, ["observation"] = "",
        }));
        foreach (var resource in new[] { "evidences", "task-templates", "teams" })
        {
            Respond("POST", $"/api/{resource}/", new JsonObject { ["id"] = "new" });
        }
    }

    public void Dispose() => _ca.Dispose();

    [Fact]
    public async Task Creates_recurring_task_with_assignees()
    {
        (await Create(ScopeKind.TaskTemplate, "EV-ADMIN-REVIEW", [("first_due", "2027-02-23")],
            ("name", "EV-ADMIN-REVIEW: Multi-factor authentication"), ("description", Runbook), ("domain", "Example Pty Ltd"), ("assignees", "Example 365 owners, GRC team"),
            ("schedule", "every 12 months"), ("evidence", "EV-ADMIN-REVIEW"), ("controls", "AC-MFA"), ("requirements", "EXAMPLE-BASELINE-2026/EX-1.2"), ("enabled", "true"))).Value();

        var body = Body("/api/task-templates/");
        (body["ref_id"]!.GetValue<string>(), body["folder"]!.GetValue<string>(), body["is_recurrent"]!.GetValue<bool>(), body["enabled"]!.GetValue<bool>()).ShouldBe(("EV-ADMIN-REVIEW", "f-example", true, true));
        body["description"]!.GetValue<string>().ShouldStartWith(Runbook);
        body["task_date"]!.GetValue<string>().ShouldBe("2027-02-23");
        (body["schedule"]!["frequency"]!.GetValue<string>(), body["schedule"]!["interval"]!.GetValue<int>()).ShouldBe(("YEARLY", 1));
        Ids(body, "assigned_to").ShouldBe(["a-owners", "a-grc"]);
        Ids(body, "evidences").ShouldBe(["ev-admin"]);
        Ids(body, "applied_controls").ShouldBe(["ac-mfa"]);
        Ids(body, "requirement_assessments").ShouldBe(["ra-12"]);
        body.ContainsKey("status").ShouldBeFalse();
    }

    [Fact]
    public async Task Creates_an_evidence_item_with_its_owner_and_labels_making_a_missing_label()
    {
        (await Create(ScopeKind.Evidence, "EV-MFA", [],
            ("name", "EV-MFA: Multi-factor authentication"), ("domain", "Example Pty Ltd"), ("owner", "Example 365 owners"), ("controls", "AC-MFA"),
            ("labels", "collector:example-m365, method:manual"))).Value();

        Body("/api/filtering-labels/")["label"]!.GetValue<string>().ShouldBe("collector:example-m365");
        var body = Body("/api/evidences/");
        (body["ref_id"]!.GetValue<string>(), body["name"]!.GetValue<string>(), body["folder"]!.GetValue<string>()).ShouldBe(("EV-MFA", "EV-MFA: Multi-factor authentication", "f-example"));
        Ids(body, "owner").ShouldBe(["a-owners"]);
        Ids(body, "filtering_labels").ShouldBe(["l-new", "l-manual"]);
        Ids(body, "applied_controls").ShouldBe(["ac-mfa"]);
    }

    [Fact]
    public async Task A_teams_email_is_written()
    {
        (await Create(ScopeKind.Team, "Backup owners", [], ("domain", "Example Pty Ltd"), ("email", "backup-owners@example.com"))).Value();

        Body("/api/teams/")["team_email"]!.GetValue<string>().ShouldBe("backup-owners@example.com");
    }

    [Fact]
    public async Task Reads_task_templates_and_evidence_items_but_never_an_occurrence()
    {
        var query = new ScopeStateQuery("Global", [], new Dictionary<string, IReadOnlyList<RequirementKey>>()) { EvidencePlan = true, Access = true };

        var held = (await _ca.Scope.ReadAsync(query, _ct)).Value();

        var task = held.Single(a => a.Held.Kind == ScopeKind.TaskTemplate).Held;
        task.Key.ShouldBe("EV-ADMIN-REVIEW");
        task.Fields.ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "EV-ADMIN-REVIEW: Multi-factor authentication",
            ["description"] = Runbook,
            ["domain"] = "Example Pty Ltd",
            ["assignees"] = "Example 365 owners, GRC team",
            ["schedule"] = "every 3 months",
            ["evidence"] = "EV-ADMIN-REVIEW",
            ["controls"] = "AC-MFA",
            ["requirements"] = "EXAMPLE-BASELINE-2026/EX-1.2",
            ["enabled"] = "true",
        }, ignoreOrder: true);
        held.Single(a => a.Held.Kind == ScopeKind.TaskTemplate).AppliedState.ShouldBe("0123456789abcdef");
        var evidence = held.Single(a => a.Held.Kind == ScopeKind.Evidence).Held;
        (evidence.Key, evidence.Fields["owner"], evidence.Fields["labels"], evidence.Fields["controls"]).ShouldBe(("EV-ADMIN-REVIEW", "Example 365 owners", "method:manual", "AC-MFA"));
        held.Where(a => a.Held.Kind == ScopeKind.Document).Select(a => a.Held.Key).ShouldBe(["identity-policy"]);
        held.Single(a => a.Held.Kind == ScopeKind.Team && a.Held.Key == "Example 365 owners").Held.Fields["email"].ShouldBe("owners-365@example.com");
        (await _ca.Scope.ReadAsync(query with { EvidencePlan = false }, _ct)).Value().ShouldNotContain(a => a.Held.Kind == ScopeKind.TaskTemplate);
    }

    private Task<Core.Results.Result<Core.Results.Unit>> Create(ScopeKind kind, string key, (string Name, string Value)[] initial, params (string Name, string Value)[] fields)
    {
        var declared = new ScopeObject(kind, key, fields.ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal))
        {
            Initial = initial.ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal),
        };
        return _ca.Writer.WriteAsync(new ScopeWrite(new ScopeChange(kind, key, ScopeAction.Create, [], null), declared, "Global"), _ct);
    }

    private JsonObject Body(string path) =>
        JsonNode.Parse(_ca.Server.LogEntries.Single(e => e.RequestMessage!.Method == "POST" && e.RequestMessage.Path == path).RequestMessage!.Body!)!.AsObject();

    private static string[] Ids(JsonObject body, string field) => [.. body[field]!.AsArray().Select(i => i!.GetValue<string>())];
}
