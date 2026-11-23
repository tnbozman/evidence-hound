using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EvidenceHound.CisoAssistant.Tests;

/// <summary>s09-19: suppliers as v4.3.4's third-party endpoints hold them: entities, solutions, contracts and representatives.</summary>
public sealed class SupplierClientTests : IDisposable
{
    private readonly FakeCisoAssistant _ca = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public SupplierClientTests() => Arrange(_ca);

    /// <summary>A CISO Assistant holding one supplier with its solutions, contract, representative and a review concluded ok.</summary>
    internal static void Arrange(FakeCisoAssistant ca)
    {
        void Respond(string method, string path, JsonObject body) => SupplierClientTests.Respond(ca, method, path, body);
        foreach (var resource in new[] { "users", "actors", "frameworks", "loaded-libraries", "assets", "compliance-assessments", "evidences", "applied-controls" })
        {
            Respond("GET", $"/api/{resource}/", List());
        }

        Respond("GET", "/api/folders/", List(
            new JsonObject { ["id"] = "f-global", ["name"] = "Global", ["parent_folder"] = null },
            new JsonObject { ["id"] = "f-example", ["name"] = "Example Pty Ltd", ["parent_folder"] = "f-global", ["content_type"] = "DO" }));
        Respond("GET", "/api/perimeters/", List(new JsonObject { ["id"] = "p-sup", ["name"] = "Suppliers", ["folder"] = new JsonObject { ["id"] = "f-example", ["str"] = "Example Pty Ltd" } }));
        Respond("GET", "/api/perimeters/p-sup/object/", new JsonObject { ["id"] = "p-sup", ["name"] = "Suppliers", ["folder"] = "f-example", ["description"] = "" });
        Respond("GET", "/api/folders/f-example/object/", new JsonObject { ["id"] = "f-example", ["name"] = "Example Pty Ltd", ["parent_folder"] = "f-global", ["description"] = "" });
        Respond("GET", "/api/entities/", List(
            new JsonObject { ["id"] = "e-main", ["name"] = "Example Pty Ltd", ["builtin"] = true, ["folder"] = new JsonObject { ["id"] = "f-global", ["str"] = "Global" } },
            new JsonObject
            {
                ["id"] = "e-1",
                ["ref_id"] = "SUP-EXCLOUD",
                ["name"] = "Example Cloud Pty Ltd",
                ["description"] = "Hosts the customer portal.\n[hound scope 0123456789abcdef]",
                ["mission"] = "",
                ["country"] = "AU",
                ["currency"] = "AUD",
                ["folder"] = new JsonObject { ["id"] = "f-example", ["str"] = "Example Pty Ltd" },
                ["default_dependency"] = 3,
                ["default_penetration"] = 2,
                ["default_maturity"] = 3,
                ["default_trust"] = 3,
                // v4.3.4's read serializer gives legal identifiers as display text, one "name: value" per line; the write takes an object.
                ["legal_identifiers"] = "LEI: EXAMPLE0000000000000\nVAT: EX-0001",
                ["parent_entity"] = null,
                ["builtin"] = false,
                ["tier"] = new JsonObject { ["id"] = "t-2", ["str"] = "Important" },
            }));
        Respond("GET", "/api/tiers/", List(
            new JsonObject { ["id"] = "t-1", ["key"] = "critical", ["name"] = "Critical" },
            new JsonObject { ["id"] = "t-2", ["key"] = "important", ["name"] = "Important" }));
        Respond("GET", "/api/entity-assessments/", List(
            new JsonObject
            {
                ["id"] = "ea-1",
                ["ref_id"] = "EA-EXCLOUD-2026-06",
                ["name"] = "Example Cloud review 2026",
                ["description"] = "[hound scope 0123456789abcdef]",
                ["entity"] = new JsonObject { ["id"] = "e-1", ["str"] = "Example Cloud Pty Ltd" },
                ["solutions"] = new JsonArray(new JsonObject { ["id"] = "s-1", ["str"] = "Managed hosting" }),
                ["folder"] = new JsonObject { ["id"] = "f-example", ["str"] = "Example Pty Ltd" },
                ["perimeter"] = new JsonObject { ["id"] = "p-sup", ["str"] = "Suppliers" },
                ["due_date"] = "2026-06-30",
                ["criticality"] = 0,
                ["conclusion"] = "ok",
                ["expiry_date"] = "2027-06-30",
            }));
        Respond("GET", "/api/solutions/", List(
            new JsonObject { ["id"] = "s-1", ["ref_id"] = "SOL-EXCLOUD-HOST", ["name"] = "Managed hosting", ["provider_entity"] = new JsonObject { ["id"] = "e-1", ["str"] = "Example Cloud Pty Ltd" }, ["criticality"] = 3 },
            new JsonObject { ["id"] = "s-2", ["ref_id"] = "SOL-EXCLOUD-BACKUP", ["name"] = "Offsite backup", ["provider_entity"] = new JsonObject { ["id"] = "e-1", ["str"] = "Example Cloud Pty Ltd" }, ["criticality"] = 2 }));
        Respond("GET", "/api/contracts/", List(
            new JsonObject
            {
                ["id"] = "c-1",
                ["ref_id"] = "CTR-EXCLOUD-2026",
                ["name"] = "Hosting agreement 2026",
                ["status"] = "active",
                ["start_date"] = "2026-01-01",
                ["end_date"] = "2026-12-31",
                ["annual_expense"] = 12000.0,
                ["currency"] = "AUD",
                ["folder"] = new JsonObject { ["id"] = "f-example", ["str"] = "Example Pty Ltd" },
                ["provider_entity"] = new JsonObject { ["id"] = "e-1", ["str"] = "Example Cloud Pty Ltd" },
                ["solutions"] = new JsonArray(new JsonObject { ["id"] = "s-1", ["str"] = "Managed hosting" }, new JsonObject { ["id"] = "s-2", ["str"] = "Offsite backup" }),
            }));
        Respond("GET", "/api/representatives/", List(
            new JsonObject { ["id"] = "r-1", ["email"] = "account.manager@example.com", ["first_name"] = "Sam", ["last_name"] = "Example", ["role"] = "Account manager", ["entity"] = new JsonObject { ["id"] = "e-1", ["str"] = "Example Cloud Pty Ltd" } }));
        foreach (var resource in new[] { "entities", "solutions", "contracts", "representatives", "entity-assessments" })
        {
            Respond("POST", $"/api/{resource}/", new JsonObject { ["id"] = "new" });
        }

        Respond("PATCH", "/api/entities/e-main/", new JsonObject { ["id"] = "e-main" });
    }

    public void Dispose() => _ca.Dispose();

    [Fact]
    public async Task Creates_entity_solution_and_contract()
    {
        (await Create(ScopeKind.Entity, "SUP-EXCLOUD", ("name", "Example Cloud Pty Ltd"), ("description", "Hosts the customer portal."), ("country", "AU"), ("currency", "AUD"),
            ("domain", "Example Pty Ltd"), ("dependency", "3"), ("penetration", "2"), ("maturity", "3"), ("trust", "3"), ("identifiers", "{\"LEI\":\"EXAMPLE0000000000000\"}"))).Value();
        (await Create(ScopeKind.Solution, "SOL-EXCLOUD-HOST", ("name", "Managed hosting"), ("entity", "SUP-EXCLOUD"), ("criticality", "3"))).Value();
        (await Create(ScopeKind.Contract, "CTR-EXCLOUD-2026", ("name", "Hosting agreement 2026"), ("entity", "SUP-EXCLOUD"), ("domain", "Example Pty Ltd"), ("status", "active"),
            ("start_date", "2026-01-01"), ("end_date", "2026-12-31"), ("annual_expense", "12000"), ("currency", "AUD"), ("solutions", "SOL-EXCLOUD-BACKUP, SOL-EXCLOUD-HOST"))).Value();
        (await Create(ScopeKind.Representative, "account.manager@example.com", ("entity", "SUP-EXCLOUD"), ("first_name", "Sam"))).Value();

        var entity = Body("/api/entities/");
        (entity["name"]!.GetValue<string>(), entity["ref_id"]!.GetValue<string>(), entity["folder"]!.GetValue<string>(), entity["country"]!.GetValue<string>()).ShouldBe(("Example Cloud Pty Ltd", "SUP-EXCLOUD", "f-example", "AU"));
        (entity["default_dependency"]!.GetValue<int>(), entity["default_penetration"]!.GetValue<int>(), entity["default_maturity"]!.GetValue<int>(), entity["default_trust"]!.GetValue<int>()).ShouldBe((3, 2, 3, 3));
        entity["legal_identifiers"]!.ToJsonString().ShouldBe("{\"LEI\":\"EXAMPLE0000000000000\"}");
        entity["description"]!.GetValue<string>().ShouldStartWith("Hosts the customer portal.\n[hound scope ");
        var solution = Body("/api/solutions/");
        (solution["provider_entity"]!.GetValue<string>(), solution["criticality"]!.GetValue<int>()).ShouldBe(("e-1", 3));
        var contract = Body("/api/contracts/");
        (contract["provider_entity"]!.GetValue<string>(), contract["folder"]!.GetValue<string>(), contract["status"]!.GetValue<string>()).ShouldBe(("e-1", "f-example", "active"));
        (contract["start_date"]!.GetValue<string>(), contract["end_date"]!.GetValue<string>(), contract["annual_expense"]!.GetValue<decimal>()).ShouldBe(("2026-01-01", "2026-12-31", 12000m));
        contract["solutions"]!.AsArray().Select(s => s!.GetValue<string>()).ShouldBe(["s-2", "s-1"]);
        var representative = Body("/api/representatives/");
        (representative["email"]!.GetValue<string>(), representative["entity"]!.GetValue<string>()).ShouldBe(("account.manager@example.com", "e-1"));
        representative.ContainsKey("create_user").ShouldBeFalse();
        representative.ContainsKey("user").ShouldBeFalse();
    }

    [Fact]
    public async Task Reads_suppliers_when_asked_leaving_out_the_main_entity()
    {
        var query = new ScopeStateQuery("Global", [], new Dictionary<string, IReadOnlyList<RequirementKey>>()) { ThirdParties = true };

        var held = (await _ca.Scope.ReadAsync(query, _ct)).Value()
            .Where(a => a.Held.Kind is ScopeKind.Entity or ScopeKind.Solution or ScopeKind.Contract or ScopeKind.Representative)
            .ToDictionary(a => (a.Held.Kind, a.Held.Key), a => a.Held.Fields);
        var without = (await _ca.Scope.ReadAsync(query with { ThirdParties = false }, _ct)).Value();

        held.Keys.ShouldBe(
        [
            (ScopeKind.Entity, "SUP-EXCLOUD"), (ScopeKind.Solution, "SOL-EXCLOUD-HOST"), (ScopeKind.Solution, "SOL-EXCLOUD-BACKUP"),
            (ScopeKind.Contract, "CTR-EXCLOUD-2026"), (ScopeKind.Representative, "account.manager@example.com"),
        ], ignoreOrder: true);
        held[(ScopeKind.Entity, "SUP-EXCLOUD")].ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "Example Cloud Pty Ltd",
            ["description"] = "Hosts the customer portal.",
            ["country"] = "AU",
            ["currency"] = "AUD",
            ["domain"] = "Example Pty Ltd",
            ["dependency"] = "3",
            ["penetration"] = "2",
            ["maturity"] = "3",
            ["trust"] = "3",
            ["identifiers"] = "{\"LEI\":\"EXAMPLE0000000000000\",\"VAT\":\"EX-0001\"}",
        }, ignoreOrder: true);
        held[(ScopeKind.Contract, "CTR-EXCLOUD-2026")].ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "Hosting agreement 2026",
            ["entity"] = "SUP-EXCLOUD",
            ["domain"] = "Example Pty Ltd",
            ["status"] = "active",
            ["start_date"] = "2026-01-01",
            ["end_date"] = "2026-12-31",
            ["annual_expense"] = "12000",
            ["currency"] = "AUD",
            ["solutions"] = "SOL-EXCLOUD-BACKUP, SOL-EXCLOUD-HOST",
        }, ignoreOrder: true);
        held[(ScopeKind.Representative, "account.manager@example.com")]["entity"].ShouldBe("SUP-EXCLOUD");
        without.ShouldNotContain(a => a.Held.Kind == ScopeKind.Entity);
    }

    [Fact]
    public async Task Reads_supplier_assessments_with_their_conclusion_and_each_suppliers_tier_as_observed()
    {
        var query = new ScopeStateQuery("Global", [], new Dictionary<string, IReadOnlyList<RequirementKey>>()) { ThirdParties = true };

        var held = (await _ca.Scope.ReadAsync(query, _ct)).Value();

        var review = held.Where(a => a.Held.Kind == ScopeKind.EntityAssessment).ShouldHaveSingleItem();
        review.Held.Key.ShouldBe("EA-EXCLOUD-2026-06");
        review.Held.Fields.ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "Example Cloud review 2026",
            ["entity"] = "SUP-EXCLOUD",
            ["solutions"] = "SOL-EXCLOUD-HOST",
            ["domain"] = "Example Pty Ltd",
            ["perimeter"] = "Suppliers",
            ["due_date"] = "2026-06-30",
            ["conclusion"] = "ok",
        }, ignoreOrder: true);
        review.Observed.ShouldBe(new Dictionary<string, string> { ["conclusion"] = "ok", ["expiry_date"] = "2027-06-30" }, ignoreOrder: true);
        held.Single(a => a.Held.Kind == ScopeKind.Entity && a.Held.Key == "SUP-EXCLOUD").Observed["tier"].ShouldBe("important");
    }

    [Fact]
    public async Task Creates_a_supplier_assessment_with_its_questionnaire_audit_and_never_sends_a_conclusion()
    {
        _ca.Server.Given(Request.Create().WithPath("/api/frameworks/").UsingGet()).AtPriority(0)
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(List(new JsonObject { ["id"] = "fw-q", ["urn"] = "urn:example:risk:framework:example-questionnaire" }).ToJsonString()));
        var declared = new ScopeObject(ScopeKind.EntityAssessment, "EA-EXCLOUD-2027-06", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = "Example Cloud review 2027",
            ["entity"] = "SUP-EXCLOUD",
            ["solutions"] = "SOL-EXCLOUD-HOST",
            ["domain"] = "Example Pty Ltd",
            ["perimeter"] = "Suppliers",
            ["due_date"] = "2027-06-30",
            ["criticality"] = "3",
        })
        {
            Initial = new Dictionary<string, string>(StringComparer.Ordinal) { ["questionnaire"] = "urn:example:risk:framework:example-questionnaire", ["groups"] = "core" },
        };

        (await _ca.Writer.WriteAsync(new ScopeWrite(new ScopeChange(ScopeKind.EntityAssessment, declared.Key, ScopeAction.Create, [], null), declared, "Global"), _ct)).Value();

        var body = Body("/api/entity-assessments/");
        (body["ref_id"]!.GetValue<string>(), body["entity"]!.GetValue<string>(), body["folder"]!.GetValue<string>(), body["perimeter"]!.GetValue<string>()).ShouldBe(("EA-EXCLOUD-2027-06", "e-1", "f-example", "p-sup"));
        (body["due_date"]!.GetValue<string>(), body["criticality"]!.GetValue<int>()).ShouldBe(("2027-06-30", 3));
        body["solutions"]!.AsArray().Select(s => s!.GetValue<string>()).ShouldBe(["s-1"]);
        (body["create_audit"]!.GetValue<bool>(), body["framework"]!.GetValue<string>()).ShouldBe((true, "fw-q"));
        body["selected_implementation_groups"]!.AsArray().Select(g => g!.GetValue<string>()).ShouldBe(["core"]);
        body.ContainsKey("conclusion").ShouldBeFalse();
        body.ContainsKey("expiry_date").ShouldBeFalse();
    }

    [Fact]
    public async Task The_main_entity_is_read_as_the_organisation_and_patched_in_place_even_when_the_plan_says_create()
    {
        var query = new ScopeStateQuery("Global", [], new Dictionary<string, IReadOnlyList<RequirementKey>>()) { Organisation = true };
        var main = (await _ca.Scope.ReadAsync(query, _ct)).Value().Where(a => a.Held.Kind == ScopeKind.MainEntity).ShouldHaveSingleItem();
        (main.Held.Key, main.Held.Fields["name"]).ShouldBe(("organisation", "Example Pty Ltd"));

        (await Create(ScopeKind.MainEntity, "organisation", ("name", "Example Pty Ltd"), ("description", "The organisation."), ("country", "AU"))).Value();

        var body = JsonNode.Parse(_ca.Server.LogEntries.Single(e => e.RequestMessage!.Method == "PATCH" && e.RequestMessage.Path == "/api/entities/e-main/").RequestMessage!.Body!)!.AsObject();
        (body["name"]!.GetValue<string>(), body["description"]!.GetValue<string>(), body["country"]!.GetValue<string>()).ShouldBe(("Example Pty Ltd", "The organisation.", "AU"));
        _ca.Server.LogEntries.ShouldNotContain(e => e.RequestMessage!.Method == "POST");
    }

    private Task<Core.Results.Result<Core.Results.Unit>> Create(ScopeKind kind, string key, params (string Name, string Value)[] fields)
    {
        var declared = new ScopeObject(kind, key, fields.ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal));
        return _ca.Writer.WriteAsync(new ScopeWrite(new ScopeChange(kind, key, ScopeAction.Create, [], null), declared, "Global"), _ct);
    }

    private JsonObject Body(string path) =>
        JsonNode.Parse(_ca.Server.LogEntries.Single(e => e.RequestMessage!.Method == "POST" && e.RequestMessage.Path == path).RequestMessage!.Body!)!.AsObject();

    internal static JsonObject List(params JsonObject[] items) => new() { ["count"] = items.Length, ["results"] = new JsonArray([.. items]) };

    private void Respond(string method, string path, JsonObject body) => Respond(_ca, method, path, body);

    internal static void Respond(FakeCisoAssistant ca, string method, string path, JsonObject body) =>
        ca.Server.Given(Request.Create().WithPath(path).UsingMethod(method)).RespondWith(Response.Create().WithStatusCode(method == "POST" ? 201 : 200).WithBody(body.ToJsonString()));
}
