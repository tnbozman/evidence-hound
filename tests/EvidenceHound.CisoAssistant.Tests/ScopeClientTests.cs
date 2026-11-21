using System.Security.Cryptography;
using System.Text;
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
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"hound-scope-{Guid.NewGuid():N}");
    private static readonly string E8 = "urn:intuitem:risk:framework:essential-eight-2023";
    private static readonly string E8Library = "urn:intuitem:risk:library:essential-eight-2023";

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

    public void Dispose()
    {
        _ca.Dispose();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

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

    [Fact]
    public async Task Imports_stored_library_by_urn()
    {
        Respond("POST", $"/api/stored-libraries/{E8Library}/import/", 200, new JsonObject { ["status"] = "success" });
        var framework = new ScopeObject(ScopeKind.Framework, E8, new Dictionary<string, string> { ["version"] = "3" }) { Initial = new Dictionary<string, string> { ["library"] = E8Library } };

        (await Write(framework, ScopeAction.Create, null)).Value();

        Sent().ShouldBe([("POST", $"/api/stored-libraries/{E8Library}/import/")]);
    }

    [Fact]
    public async Task Uploads_custom_library()
    {
        const string library = "urn:example:risk:library:example-baseline";
        List("stored-libraries");
        Respond("POST", "/api/stored-libraries/upload/", 201, new JsonObject { ["urn"] = library });
        Respond("POST", $"/api/stored-libraries/{library}/import/", 200, new JsonObject { ["status"] = "success" });
        var file = await FileAsync("example-baseline.yaml", "urn: urn:example:risk:library:example-baseline\n");
        var framework = new ScopeObject(ScopeKind.Framework, "urn:example:risk:framework:example-baseline", new Dictionary<string, string>())
        {
            Initial = new Dictionary<string, string> { ["library"] = library, ["file"] = "example-baseline.yaml" },
            Files = [file],
        };

        (await Write(framework, ScopeAction.Create, null)).Value();

        Sent().ShouldBe([("POST", "/api/stored-libraries/upload/"), ("POST", $"/api/stored-libraries/{library}/import/")]);
        var upload = Logged("POST", "/api/stored-libraries/upload/");
        upload.Body.ShouldBe("urn: urn:example:risk:library:example-baseline\n");
        upload.Headers!["Content-Disposition"].ShouldHaveSingleItem().ShouldContain("filename=example-baseline.yaml");
    }

    [Fact]
    public async Task Creates_audit_with_groups_actors_and_marker()
    {
        List("frameworks", new JsonObject { ["id"] = "fw-e8", ["urn"] = E8 });
        List("compliance-assessments");
        Respond("POST", "/api/compliance-assessments/", 201, new JsonObject { ["id"] = "ca-new" });
        var audit = Declared(ScopeKind.Audit, "EX-E8-2026",
            ("name", "Essential Eight ML2 2026"), ("framework", E8), ("groups", "1, 2"), ("domain", "Example Pty Ltd"), ("perimeter", "Corporate IT"),
            ("eta", "2026-06-30"), ("due_date", "2026-07-31"), ("authors", "it@example.com"));

        (await Create(audit)).Value();

        Posted("compliance-assessments").ShouldBe(Json(new JsonObject
        {
            ["name"] = "Essential Eight ML2 2026",
            ["ref_id"] = "EX-E8-2026",
            ["description"] = $"[hound scope {State}]",
            ["framework"] = "fw-e8",
            ["perimeter"] = "p-corp",
            ["folder"] = "f-ex",
            ["selected_implementation_groups"] = new JsonArray("1", "2"),
            ["eta"] = "2026-06-30",
            ["due_date"] = "2026-07-31",
            ["authors"] = new JsonArray("act-it"),
            ["reviewers"] = new JsonArray(),
        }));
    }

    [Fact]
    public async Task Updates_document_and_uploads_only_new_files()
    {
        var kept = await FileAsync("identity-policy.pdf", "policy v1");
        var added = await FileAsync("identity-standard.pdf", "standard v1");
        List("evidences", new JsonObject { ["id"] = "ev-policy", ["ref_id"] = "identity-policy", ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "Example Pty Ltd" } });
        List("evidence-revisions", new JsonObject { ["id"] = "rev-1", ["attachment_hash"] = kept.Sha256 });
        Respond("PATCH", "/api/evidences/ev-policy/", 200, new JsonObject());
        Respond("POST", "/api/evidence-revisions/", 201, new JsonObject { ["id"] = "rev-2" });
        var document = Declared(ScopeKind.Document, "identity-policy", ("name", "Identity policy"), ("domain", "Example Pty Ltd"), ("hashes", $"{kept.Sha256}, {added.Sha256}")) with { Files = [kept, added] };

        (await Write(document, ScopeAction.Update, State)).Value();

        Sent().ShouldBe([("PATCH", "/api/evidences/ev-policy/"), ("POST", "/api/evidence-revisions/")]);
        Json(JsonNode.Parse(Logged("PATCH", "/api/evidences/ev-policy/").Body!)!).ShouldBe(Json(new JsonObject
        {
            ["name"] = "Identity policy",
            ["ref_id"] = "identity-policy",
            ["description"] = $"[hound scope {State}]",
            ["folder"] = "f-ex",
        }));
        Logged("POST", "/api/evidence-revisions/").Body!.ShouldContain("identity-standard.pdf");
    }

    [Fact]
    public async Task Creates_control_with_owners_systems_documents_and_initial_status()
    {
        List("applied-controls");
        List("evidences", new JsonObject { ["id"] = "ev-policy", ["ref_id"] = "identity-policy" });
        Respond("POST", "/api/applied-controls/", 201, new JsonObject { ["id"] = "ac-new" });
        var control = Declared(ScopeKind.Control, "AC-MFA",
            ("name", "Multi-factor authentication"), ("domain", "Example Pty Ltd"), ("owner", "it@example.com"), ("priority", "1"), ("category", "technical"),
            ("systems", "Example 365"), ("documents", "identity-policy")) with
        { Initial = new Dictionary<string, string> { ["status"] = "active" } };

        (await Create(control)).Value();

        Posted("applied-controls").ShouldBe(Json(new JsonObject
        {
            ["name"] = "Multi-factor authentication",
            ["ref_id"] = "AC-MFA",
            ["description"] = $"[hound scope {State}]",
            ["folder"] = "f-ex",
            ["owner"] = new JsonArray("act-it"),
            ["priority"] = 1,
            ["category"] = "technical",
            ["assets"] = new JsonArray("a-365"),
            ["evidences"] = new JsonArray("ev-policy"),
            ["status"] = "active",
        }));
    }

    [Fact]
    public async Task Writes_row_results_and_links_to_the_in_group_rows()
    {
        List("compliance-assessments", new JsonObject { ["id"] = "ca-e8", ["ref_id"] = "EX-E8-2026" });
        _ca.Server.Given(Request.Create().WithPath("/api/compliance-assessments/ca-e8/object/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["id"] = "ca-e8", ["selected_implementation_groups"] = new JsonArray("1", "2") }.ToJsonString()));
        List("requirement-assessments",
            new JsonObject { ["id"] = "ra-1504-ml2", ["requirement"] = new JsonObject { ["urn"] = $"{E8}:ml2:ism-1504", ["ref_id"] = "ISM-1504", ["implementation_groups"] = new JsonArray("2") } },
            new JsonObject { ["id"] = "ra-1504-ml3", ["requirement"] = new JsonObject { ["urn"] = $"{E8}:ml3:ism-1504", ["ref_id"] = "ISM-1504", ["implementation_groups"] = new JsonArray("3") } });
        List("applied-controls", new JsonObject { ["id"] = "ac-mfa", ["ref_id"] = "AC-MFA" });
        List("evidences", new JsonObject { ["id"] = "ev-policy", ["ref_id"] = "identity-policy" });
        Respond("PATCH", "/api/requirement-assessments/ra-1504-ml2/", 200, new JsonObject());
        var row = Declared(ScopeKind.Assessment, "EX-E8-2026/ISM-1504", ("status", "done"), ("result", "compliant"), ("observation", "Checked."), ("controls", "AC-MFA"), ("documents", "identity-policy"));

        (await Write(row, ScopeAction.Update, null)).Value();

        Sent().ShouldBe([("PATCH", "/api/requirement-assessments/ra-1504-ml2/")]);
        Json(JsonNode.Parse(Logged("PATCH", "/api/requirement-assessments/ra-1504-ml2/").Body!)!).ShouldBe(Json(new JsonObject
        {
            ["status"] = "done",
            ["result"] = "compliant",
            ["observation"] = "Checked.",
            ["applied_controls"] = new JsonArray("ac-mfa"),
            ["evidences"] = new JsonArray("ev-policy"),
        }));
    }

    private Task<Core.Results.Result<Core.Results.Unit>> Write(ScopeObject declared, ScopeAction action, string? state) =>
        _ca.Writer.WriteAsync(new ScopeWrite(new ScopeChange(declared.Kind, declared.Key, action, [], state), declared, "Global"), _ct);

    private async Task<ScopeFile> FileAsync(string name, string content)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, name);
        await File.WriteAllTextAsync(path, content, _ct);
        return new ScopeFile(path, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))));
    }

    /// <summary>The writes sent, in order: every request but a GET.</summary>
    private (string Method, string Path)[] Sent() =>
        [.. _ca.Server.LogEntries.Where(e => e.RequestMessage!.Method != "GET").Select(e => (e.RequestMessage!.Method, e.RequestMessage.Path))];

    private WireMock.IRequestMessage Logged(string method, string path) =>
        _ca.Server.LogEntries.Single(e => e.RequestMessage!.Method == method && e.RequestMessage.Path == path).RequestMessage!;

    private void Respond(string method, string path, int status, JsonObject body) =>
        _ca.Server.Given(Request.Create().WithPath(path).UsingMethod(method)).RespondWith(Response.Create().WithStatusCode(status).WithBody(body.ToJsonString()));

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
