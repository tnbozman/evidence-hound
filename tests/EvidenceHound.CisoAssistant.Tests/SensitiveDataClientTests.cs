using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant.Tests;

/// <summary>s10-06: the kinds of sensitive data a solution or system handles, as CISO Assistant's own filtering labels.</summary>
public sealed class SensitiveDataClientTests : IDisposable
{
    private readonly FakeCisoAssistant _ca = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public SensitiveDataClientTests()
    {
        SupplierClientTests.Arrange(_ca);
        static JsonObject Ref(string id, string str) => new() { ["id"] = id, ["str"] = str };
        Respond("GET", "/api/filtering-labels/", SupplierClientTests.List(new JsonObject { ["id"] = "l-cui", ["label"] = "data-cui" }));
        Respond("POST", "/api/filtering-labels/", new JsonObject { ["id"] = "l-pii", ["label"] = "data-pii" });
        Respond("GET", "/api/solutions/", SupplierClientTests.List(new JsonObject
        {
            ["id"] = "s-1",
            ["ref_id"] = "SOL-EXCLOUD-HOST",
            ["name"] = "Managed hosting",
            ["provider_entity"] = Ref("e-1", "Example Cloud Pty Ltd"),
            ["criticality"] = 3,
            ["filtering_labels"] = new JsonArray(Ref("l-cui", "data-cui")),
        }));
        Respond("GET", "/api/assets/", SupplierClientTests.List(new JsonObject { ["id"] = "a-1", ["name"] = "Customer portal", ["folder"] = Ref("f-example", "Example Pty Ltd") }));
        Respond("GET", "/api/assets/a-1/object/", new JsonObject
        {
            ["id"] = "a-1",
            ["name"] = "Customer portal",
            ["folder"] = "f-example",
            ["type"] = "PR",
            ["filtering_labels"] = new JsonArray("l-cui"),
            ["description"] = "",
        });
        Respond("POST", "/api/assets/", new JsonObject { ["id"] = "new" });
    }

    public void Dispose() => _ca.Dispose();

    [Fact]
    public async Task A_solution_and_a_system_are_written_with_their_data_labels()
    {
        (await Create(ScopeKind.Solution, "SOL-EXCLOUD-BACKUP", ("name", "Offsite backup"), ("entity", "SUP-EXCLOUD"), ("labels", "data-cui, data-pii"))).Value();
        (await Create(ScopeKind.System, "Backup vault", ("domain", "Example Pty Ltd"), ("type", "supporting"), ("labels", "data-cui"))).Value();

        Ids(Body("/api/solutions/"), "filtering_labels").ShouldBe(["l-cui", "l-pii"]);
        Ids(Body("/api/assets/"), "filtering_labels").ShouldBe(["l-cui"]);
    }

    [Fact]
    public async Task A_solution_and_a_system_are_read_with_their_data_labels()
    {
        var query = new ScopeStateQuery("Global", [], new Dictionary<string, IReadOnlyList<RequirementKey>>()) { ThirdParties = true };

        var held = (await _ca.Scope.ReadAsync(query, _ct)).Value();

        held.Single(a => a.Held.Kind == ScopeKind.Solution).Held.Fields["labels"].ShouldBe("data-cui");
        held.Single(a => a.Held.Kind == ScopeKind.System).Held.Fields["labels"].ShouldBe("data-cui");
    }

    private Task<Core.Results.Result<Core.Results.Unit>> Create(ScopeKind kind, string key, params (string Name, string Value)[] fields)
    {
        var declared = new ScopeObject(kind, key, fields.ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal));
        return _ca.Writer.WriteAsync(new ScopeWrite(new ScopeChange(kind, key, ScopeAction.Create, [], null), declared, "Global"), _ct);
    }

    private JsonObject Body(string path) =>
        JsonNode.Parse(_ca.Server.LogEntries.Single(e => e.RequestMessage!.Method == "POST" && e.RequestMessage.Path == path).RequestMessage!.Body!)!.AsObject();

    private static string[] Ids(JsonObject body, string field) => [.. body[field]!.AsArray().Select(i => i!.GetValue<string>())];

    private void Respond(string method, string path, JsonObject body) => SupplierClientTests.Respond(_ca, method, path, body);
}
