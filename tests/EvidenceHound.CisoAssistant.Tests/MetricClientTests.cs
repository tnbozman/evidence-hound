using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EvidenceHound.CisoAssistant.Tests;

/// <summary>s16-02: metric definitions, instances, dashboards and widgets written to CISO Assistant's metrology API (v4.3.4 shapes).</summary>
public sealed class MetricClientTests : IDisposable
{
    private const string State = "0123456789abcdef";
    private readonly FakeCisoAssistant _ca = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public MetricClientTests()
    {
        List("folders", Folder("f-global", "Global", null), Folder("f-ex", "Example Pty Ltd", "f-global"));
        List("users", new JsonObject { ["id"] = "u-it", ["email"] = "it@example.com" });
        List("actors", new JsonObject { ["id"] = "act-it", ["str"] = "it@example.com", ["type"] = "user", ["specific"] = new JsonObject { ["id"] = "u-it", ["str"] = "it@example.com" } });
        List("applied-controls", new JsonObject { ["id"] = "ac-mfa", ["ref_id"] = "AC-MFA", ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "Example Pty Ltd" } });
        List("compliance-assessments", new JsonObject { ["id"] = "ca-e8", ["ref_id"] = "EX-E8-2026", ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "Example Pty Ltd" } });
        List("terminologies",
            new JsonObject { ["id"] = "t-pct", ["name"] = "percentage", ["field_path"] = "metric_definition.unit" },
            new JsonObject { ["id"] = "t-users", ["name"] = "users", ["field_path"] = "metric_definition.unit" });
    }

    public void Dispose() => _ca.Dispose();

    [Fact]
    public async Task Creates_definition_instance_and_dashboard()
    {
        List("metrology/metric-definitions", new JsonObject { ["id"] = "md-cov", ["ref_id"] = "EX-MET-COV", ["folder"] = new JsonObject { ["id"] = "f-global", ["str"] = "Global" } });
        List("metrology/metric-instances", new JsonObject { ["id"] = "mi-cov", ["ref_id"] = "EX-MET-COV-CORP", ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "Example Pty Ltd" } });
        List("metrology/dashboards", new JsonObject { ["id"] = "d-1", ["ref_id"] = "EX-DASH", ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "Example Pty Ltd" } });
        List("metrology/dashboard-widgets");
        foreach (var resource in new[] { "metric-definitions", "metric-instances", "dashboards", "dashboard-widgets" })
        {
            Respond("POST", $"/api/metrology/{resource}/", 201, new JsonObject { ["id"] = $"new-{resource}" });
        }

        (await Create(Declared(ScopeKind.MetricDefinition, "EX-MET-MFA",
            ("name", "MFA coverage"), ("unit", "percentage"), ("direction", "higher_is_better"), ("decimals", "1"), ("frequency", "monthly"), ("target", "95"),
            ("expression", "covered / total * 100"), ("inputs", "covered=EX-MET-COV sum, total=urn:example:metric:staff one")))).Value();
        Posted("metric-definitions").ShouldBe(Json(new JsonObject
        {
            ["ref_id"] = "EX-MET-MFA",
            ["name"] = "MFA coverage",
            ["description"] = $"[hound scope {State}]",
            ["folder"] = "f-global",
            ["category"] = "quantitative",
            ["unit"] = "t-pct",
            ["choices_definition"] = null,
            ["higher_is_better"] = true,
            ["decimals"] = 1,
            ["default_collection_frequency"] = "monthly",
            ["default_target"] = 95.0,
            ["expression"] = "covered / total * 100",
            ["inputs"] = new JsonArray(
                new JsonObject { ["key"] = "covered", ["definition"] = "md-cov", ["combine"] = "sum" },
                new JsonObject { ["key"] = "total", ["definition"] = "urn:example:metric:staff", ["combine"] = "one" }),
        }));

        (await Create(Declared(ScopeKind.MetricInstance, "EX-MET-COV-CORP2",
            ("name", "Accounts with MFA"), ("definition", "EX-MET-COV"), ("domain", "Example Pty Ltd"), ("frequency", "monthly"), ("grace_days", "5"),
            ("target", "95"), ("owners", "it@example.com"), ("controls", "AC-MFA")))).Value();
        Posted("metric-instances").ShouldBe(Json(new JsonObject
        {
            ["ref_id"] = "EX-MET-COV-CORP2",
            ["name"] = "Accounts with MFA",
            ["description"] = $"[hound scope {State}]",
            ["folder"] = "f-ex",
            ["metric_definition"] = "md-cov",
            ["status"] = "active",
            ["collection_frequency"] = "monthly",
            ["collection_grace_days"] = 5,
            ["target_value"] = 95.0,
            ["owner"] = new JsonArray("act-it"),
            ["applied_controls"] = new JsonArray("ac-mfa"),
        }));

        (await Create(Declared(ScopeKind.Dashboard, "EX-DASH-NEW", ("name", "Security committee"), ("domain", "Example Pty Ltd")))).Value();
        Posted("dashboards").ShouldBe(Json(new JsonObject
        {
            ["ref_id"] = "EX-DASH-NEW",
            ["name"] = "Security committee",
            ["description"] = $"[hound scope {State}]",
            ["folder"] = "f-ex",
        }));

        (await Create(Declared(ScopeKind.DashboardWidget, "EX-DASH/E8 progress",
            ("dashboard", "EX-DASH"), ("title", "E8 progress"), ("builtin", "audit EX-E8-2026 progress"), ("chart", "kpi_card"), ("time_range", "last_30_days"),
            ("aggregation", "none"), ("position", "6, 0, 6, 2")))).Value();
        Posted("dashboard-widgets").ShouldBe(Json(new JsonObject
        {
            ["dashboard"] = "d-1",
            ["folder"] = "f-ex",
            ["title"] = "E8 progress",
            ["metric_instance"] = null,
            ["target_model"] = "ComplianceAssessment",
            ["target_object_id"] = "ca-e8",
            ["metric_key"] = "progress",
            ["text_content"] = null,
            ["chart_type"] = "kpi_card",
            ["time_range"] = "last_30_days",
            ["aggregation"] = "none",
            ["position_x"] = 6,
            ["position_y"] = 0,
            ["width"] = 6,
            ["height"] = 2,
        }));
    }

    [Fact]
    public async Task Writes_a_qualitative_definition_with_its_levels_and_a_metric_widget()
    {
        List("metrology/metric-definitions");
        List("metrology/metric-instances", new JsonObject { ["id"] = "mi-pol", ["ref_id"] = "EX-MET-POL-CORP", ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "Example Pty Ltd" } });
        List("metrology/dashboards", new JsonObject { ["id"] = "d-1", ["ref_id"] = "EX-DASH", ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "Example Pty Ltd" } });
        List("metrology/dashboard-widgets");
        Respond("POST", "/api/metrology/metric-definitions/", 201, new JsonObject { ["id"] = "md-new" });
        Respond("POST", "/api/metrology/dashboard-widgets/", 201, new JsonObject { ["id"] = "w-new" });

        (await Create(Declared(ScopeKind.MetricDefinition, "EX-MET-POL",
            ("name", "Policy maturity"), ("levels", "Missing, Drafted, Approved"), ("direction", "higher_is_better"), ("target", "Approved")))).Value();
        (await Create(Declared(ScopeKind.DashboardWidget, "EX-DASH/Policy",
            ("dashboard", "EX-DASH"), ("title", "Policy"), ("metric", "EX-MET-POL-CORP"), ("chart", "line"), ("time_range", "last_year"), ("aggregation", "none"), ("position", "0, 2, 12, 3")))).Value();

        var definition = JsonNode.Parse(Logged("/api/metrology/metric-definitions/"))!;
        definition["category"]!.GetValue<string>().ShouldBe("qualitative");
        definition["unit"].ShouldBeNull();
        definition["choices_definition"]!.ToJsonString().ShouldBe("""[{"ref_id":"missing","name":"Missing"},{"ref_id":"drafted","name":"Drafted"},{"ref_id":"approved","name":"Approved"}]""");
        definition["default_target"]!.GetValue<double>().ShouldBe(3);
        var widget = JsonNode.Parse(Logged("/api/metrology/dashboard-widgets/"))!;
        (widget["metric_instance"]!.GetValue<string>(), widget["target_model"], widget["width"]!.GetValue<int>()).ShouldBe(("mi-pol", null, 12));
    }

    private Task<Core.Results.Result<Core.Results.Unit>> Create(ScopeObject declared) =>
        _ca.Writer.WriteAsync(new ScopeWrite(new ScopeChange(declared.Kind, declared.Key, ScopeAction.Create, [], State), declared, "Global"), _ct);

    private string Posted(string resource) => Json(JsonNode.Parse(Logged($"/api/metrology/{resource}/"))!);

    private string Logged(string path) =>
        _ca.Server.LogEntries.Single(e => e.RequestMessage!.Method == "POST" && e.RequestMessage.Path == path).RequestMessage!.Body!;

    private void Respond(string method, string path, int status, JsonObject body) =>
        _ca.Server.Given(Request.Create().WithPath(path).UsingMethod(method)).RespondWith(Response.Create().WithStatusCode(status).WithBody(body.ToJsonString()));

    /// <summary>A JSON object as text with its keys sorted, so key order does not matter.</summary>
    private static string Json(JsonNode node) =>
        new JsonObject([.. node.AsObject().OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone()))]).ToJsonString();

    private static ScopeObject Declared(ScopeKind kind, string key, params (string Field, string Value)[] fields) =>
        new(kind, key, fields.ToDictionary(f => f.Field, f => f.Value, StringComparer.Ordinal));

    private static JsonObject Folder(string id, string name, string? parent) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["parent_folder"] = parent is null ? null : new JsonObject { ["id"] = parent, ["str"] = "parent" },
    };

    private void List(string resource, params JsonObject[] items) =>
        _ca.Server.Given(Request.Create().WithPath($"/api/{resource}/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["count"] = items.Length, ["results"] = new JsonArray(items) }.ToJsonString()));
}
