using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EvidenceHound.CisoAssistant.Tests;

/// <summary>Writing scope changes to CISO Assistant, from recorded v4.3.4 request and response shapes.</summary>
public sealed class ScopeClientTests : IDisposable
{
    private static readonly string State = "0123456789abcdef";
    private readonly FakeCisoAssistant _ca = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public ScopeClientTests()
    {
        List("folders", Folder("f-global", "Global", null), Folder("f-ex", "Example Pty Ltd", "f-global"));
        List("assets", new JsonObject { ["id"] = "a-365", ["name"] = "Example 365", ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "Example Pty Ltd" } });
        List("perimeters", new JsonObject { ["id"] = "p-corp", ["name"] = "Corporate IT", ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "Example Pty Ltd" } });
        List("users", new JsonObject { ["id"] = "u-it", ["email"] = "it@example.com" });
        // Owners, authors and reviewers are actors: each user, team or entity has one, and a user's actor names the user as "specific".
        List("actors", new JsonObject { ["id"] = "act-it", ["str"] = "it@example.com", ["type"] = "user", ["specific"] = new JsonObject { ["id"] = "u-it", ["str"] = "it@example.com" } });
        foreach (var resource in new[] { "folders", "perimeters", "assets" })
        {
            _ca.Server.Given(Request.Create().WithPath($"/api/{resource}/").UsingPost())
                .RespondWith(Response.Create().WithStatusCode(201).WithBody(new JsonObject { ["id"] = $"new-{resource}" }.ToJsonString()));
        }

        _ca.Server.Given(Request.Create().WithPath("/api/folders/f-ex/").UsingPatch()).RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));
    }

    public void Dispose() => _ca.Dispose();

    [Fact]
    public async Task Creates_folder_perimeter_and_asset()
    {
        var domain = Declared(ScopeKind.Domain, "Example Labs", ("description", "The lab."));
        var perimeter = Declared(ScopeKind.Perimeter, "Example Pty Ltd/Office network", ("name", "Office network"), ("ref_id", "PER-OFFICE"), ("domain", "Example Pty Ltd"));
        var system = Declared(ScopeKind.System, "Example laptops",
            ("ref_id", "SYS-LAPTOPS"), ("type", "supporting"), ("description", "Staff laptops."), ("owner", "it@example.com"), ("domain", "Example Pty Ltd"), ("parents", "Example 365"));

        (await Create(domain)).Value();
        (await Create(perimeter)).Value();
        (await Create(system)).Value();

        Posted("folders").ShouldBe(Json(new JsonObject
        {
            ["name"] = "Example Labs",
            ["description"] = $"The lab.\n[hound scope {State}]",
            ["parent_folder"] = "f-global",
            ["content_type"] = "DO",
        }));
        Posted("perimeters").ShouldBe(Json(new JsonObject
        {
            ["name"] = "Office network",
            ["ref_id"] = "PER-OFFICE",
            ["description"] = $"[hound scope {State}]",
            ["folder"] = "f-ex",
        }));
        Posted("assets").ShouldBe(Json(new JsonObject
        {
            ["name"] = "Example laptops",
            ["ref_id"] = "SYS-LAPTOPS",
            ["type"] = "SP",
            ["description"] = $"Staff laptops.\n[hound scope {State}]",
            ["folder"] = "f-ex",
            ["parent_assets"] = new JsonArray("a-365"),
            ["owner"] = new JsonArray("act-it"),
        }));
    }

    [Fact]
    public async Task Update_patches_the_object_found_by_name_with_its_marker()
    {
        var domain = Declared(ScopeKind.Domain, "Example Pty Ltd", ("description", "The whole company."));

        var result = await _ca.Writer.WriteAsync(new ScopeWrite(new ScopeChange(ScopeKind.Domain, domain.Key, ScopeAction.Update, [], State), domain, "Global"), _ct);

        result.Value();
        var patch = _ca.Server.LogEntries.Single(e => e.RequestMessage!.Method == "PATCH");
        (patch.RequestMessage!.Path, Json(JsonNode.Parse(patch.RequestMessage.Body!)!)).ShouldBe(
            ("/api/folders/f-ex/", Json(new JsonObject { ["name"] = "Example Pty Ltd", ["description"] = $"The whole company.\n[hound scope {State}]" })));
    }

    [Fact]
    public async Task Unknown_owner_is_a_problem_and_nothing_is_sent()
    {
        var system = Declared(ScopeKind.System, "Example tablets", ("type", "supporting"), ("owner", "nobody@example.com"), ("domain", "Example Pty Ltd"));

        var result = await Create(system);

        result.Problems().ShouldHaveSingleItem().Message.ShouldContain("nobody@example.com");
        _ca.Server.LogEntries.ShouldNotContain(e => e.RequestMessage!.Method != "GET");
    }

    [Fact]
    public async Task Missing_domain_is_a_problem()
    {
        var perimeter = Declared(ScopeKind.Perimeter, "Example Labs/Lab", ("name", "Lab"), ("domain", "Example Labs"));

        var result = await Create(perimeter);

        result.Problems().ShouldHaveSingleItem().Code.ShouldBe("ciso_assistant.not_found");
    }

    private Task<Core.Results.Result<Core.Results.Unit>> Create(ScopeObject declared) =>
        _ca.Writer.WriteAsync(new ScopeWrite(new ScopeChange(declared.Kind, declared.Key, ScopeAction.Create, [], State), declared, "Global"), _ct);

    private string Posted(string resource) =>
        Json(JsonNode.Parse(_ca.Server.LogEntries.Single(e => e.RequestMessage!.Method == "POST" && e.RequestMessage.Path == $"/api/{resource}/").RequestMessage!.Body!)!);

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
