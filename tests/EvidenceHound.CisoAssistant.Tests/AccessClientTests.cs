using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EvidenceHound.CisoAssistant.Tests;

/// <summary>
/// Users with the roles they hold, and teams, read and written from recorded v4.3.4 request and response shapes. CISO Assistant lets no one
/// create role assignments through the API (administrators only view them); a role on a domain is held by membership of the domain's
/// built-in user group for it, which the user's <c>user_groups</c> sets.
/// </summary>
public sealed class AccessClientTests : IDisposable
{
    private static readonly string State = "0123456789abcdef";
    private readonly FakeCisoAssistant _ca = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public AccessClientTests()
    {
        List("folders", Folder("f-global", "Global", null), Folder("f-ex", "Example Pty Ltd", "f-global"));
        List("users",
            User("u-it", "it@example.com", "Alex", "Example", active: true, $"Runs IT.\n[hound scope {State}]", mfa: true),
            User("u-admin", "admin@example.com", "", "", active: true, null, mfa: false));
        // As CISO Assistant v4.3.4 lists them: a built-in role's name is its display name (BI-RL-AUD is "Reader"), never its code.
        List("roles", Role("r-app", "Approver"), Role("r-adm", "Administrator"), Role("r-ana", "Analyst"), Role("r-aud", "Reader"), Role("r-custom", "Evidence reviewer", builtin: false));
        var team = new JsonObject
        {
            ["id"] = "t-it",
            ["name"] = "IT team",
            ["description"] = $"[hound scope {State}]",
            ["folder"] = Relation("f-ex", "Example Pty Ltd"),
            ["leader"] = Relation("u-it", "it@example.com"),
            ["deputies"] = new JsonArray(),
            ["members"] = new JsonArray(Relation("u-admin", "admin@example.com"), Relation("u-it", "it@example.com")),
        };
        List("teams", team);
        // As v4.3.4 lists them: a group's name is "<folder> - <role's display name>", translated; a built-in group stores its code (BI-UG-APP).
        List("user-groups",
            Group("g-adm", "Global - Administrator", "f-global", "Global"),
            Group("g-gap", "Global - Approver", "f-global", "Global"),
            Group("g-ex-app", "Example Pty Ltd - Approver", "f-ex", "Example Pty Ltd"),
            Group("g-ex-ana", "Example Pty Ltd - Analyst", "f-ex", "Example Pty Ltd"),
            Group("g-ex-aud", "Example Pty Ltd - Reader", "f-ex", "Example Pty Ltd"),
            Group("g-reviewers", "Global - Evidence reviewer", "f-global", "Global", builtin: false));
        Detail("teams", team);
        Detail("folders", Folder("f-ex", "Example Pty Ltd", "f-global"));
        foreach (var resource in new[] { "actors", "frameworks", "loaded-libraries", "perimeters", "assets", "compliance-assessments", "evidences", "applied-controls" })
        {
            List(resource);
        }

        List("role-assignments",
            new JsonObject
            {
                ["id"] = "ra-app",
                ["name"] = "BI-RL-APP on Example Pty Ltd for it@example.com",
                ["description"] = $"[hound scope {State}]",
                ["role"] = Relation("r-app", "Approver"),
                ["user"] = Relation("u-it", "it@example.com"),
                ["user_group"] = null,
                ["perimeter_folders"] = new JsonArray(Relation("f-ex", "Example Pty Ltd")),
                ["folder"] = Relation("f-ex", "Example Pty Ltd"),
                ["is_recursive"] = true,
                ["builtin"] = false,
            },
            new JsonObject
            {
                ["id"] = "ra-builtin",
                ["name"] = "BI-RA-ADM",
                ["role"] = Relation("r-adm", "Administrator"),
                ["user"] = null,
                ["user_group"] = Relation("g-adm", "BI-UG-ADM"),
                ["perimeter_folders"] = new JsonArray(Relation("f-global", "Global")),
                ["folder"] = Relation("f-global", "Global"),
                ["is_recursive"] = true,
                ["builtin"] = true,
            });
        foreach (var resource in new[] { "users", "teams" })
        {
            _ca.Server.Given(Request.Create().WithPath($"/api/{resource}/").UsingPost())
                .RespondWith(Response.Create().WithStatusCode(201).WithBody(new JsonObject { ["id"] = $"new-{resource}" }.ToJsonString()));
        }

        _ca.Server.Given(Request.Create().WithPath("/api/users/u-it/").UsingPatch()).RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));
    }

    public void Dispose() => _ca.Dispose();

    [Fact]
    public async Task Creates_a_user_in_the_groups_of_their_roles_and_a_team()
    {
        var user = Declared(ScopeKind.User, "ceo@example.com", ("first_name", "Sam"), ("last_name", "Example"), ("active", "true"), ("roles", "BI-RL-ADM on Global, BI-RL-APP on Example Pty Ltd"));
        var team = Declared(ScopeKind.Team, "Leadership", ("description", "The board."), ("domain", "Example Pty Ltd"), ("leader", "it@example.com"), ("members", "admin@example.com, it@example.com"));

        (await Write(user, ScopeAction.Create)).Value();
        (await Write(team, ScopeAction.Create)).Value();

        Posted("users").ShouldBe(Json(new JsonObject
        {
            ["email"] = "ceo@example.com",
            ["first_name"] = "Sam",
            ["last_name"] = "Example",
            ["is_active"] = true,
            ["observation"] = $"[hound scope {State}]",
            ["user_groups"] = new JsonArray("g-adm", "g-ex-app"),
        }));
        Posted("teams").ShouldBe(Json(new JsonObject
        {
            ["name"] = "Leadership",
            ["description"] = $"The board.\n[hound scope {State}]",
            ["folder"] = "f-ex",
            ["leader"] = "u-it",
            ["deputies"] = new JsonArray(),
            ["members"] = new JsonArray("u-admin", "u-it"),
        }));
        Sent().ShouldNotContain(r => r.Path.StartsWith("/api/role-assignments/", StringComparison.Ordinal));
        _ca.Server.LogEntries.ShouldAllBe(e => e.RequestMessage!.Body == null || !e.RequestMessage.Body.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_custom_role_is_its_user_group_and_a_built_in_one_the_domains_group_for_its_code()
    {
        var user = Declared(ScopeKind.User, "it@example.com", ("first_name", "Alex"), ("last_name", "Example"), ("active", "true"), ("roles", "BI-RL-AUD on Example Pty Ltd, Evidence reviewer on Global"));

        (await Write(user, ScopeAction.Update)).Value();

        JsonNode.Parse(Logged("PATCH", "/api/users/u-it/").Body!)!["user_groups"]!.AsArray().Select(g => g!.GetValue<string>()).ShouldBe(["g-ex-aud", "g-reviewers"]);
    }

    [Fact]
    public async Task A_user_with_no_roles_leaves_every_group()
    {
        var user = Declared(ScopeKind.User, "it@example.com", ("first_name", "Alex"), ("last_name", "Example"), ("active", "true"));

        (await Write(user, ScopeAction.Update)).Value();

        JsonNode.Parse(Logged("PATCH", "/api/users/u-it/").Body!)!["user_groups"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Deactivating_a_user_patches_is_active_and_never_deletes()
    {
        var user = Declared(ScopeKind.User, "it@example.com", ("first_name", "Alex"), ("last_name", "Example"), ("active", "false"));

        (await Write(user, ScopeAction.Update)).Value();

        Sent().ShouldBe([("PATCH", "/api/users/u-it/")]);
        Json(JsonNode.Parse(Logged("PATCH", "/api/users/u-it/").Body!)!).ShouldBe(Json(new JsonObject
        {
            ["first_name"] = "Alex",
            ["last_name"] = "Example",
            ["is_active"] = false,
            ["observation"] = $"Runs IT.\n[hound scope {State}]",
            ["user_groups"] = new JsonArray(),
        }));
    }

    [Fact]
    public async Task A_role_with_no_group_on_its_domain_is_a_problem_and_nothing_is_sent()
    {
        var user = Declared(ScopeKind.User, "it@example.com", ("first_name", "Alex"), ("active", "true"), ("roles", "BI-RL-DMA on Example Pty Ltd"));

        var problems = (await Write(user, ScopeAction.Update)).Match(_ => [], p => p);

        problems.Select(p => p.Code).ShouldContain("ciso_assistant.not_found");
        Sent().ShouldBeEmpty();
    }

    [Fact]
    public async Task Reads_users_with_the_roles_their_groups_give_and_teams_when_the_scope_declares_access()
    {
        var query = new ScopeStateQuery("Global", [], new Dictionary<string, IReadOnlyList<RequirementKey>>()) { Access = true };

        var held = (await _ca.Scope.ReadAsync(query, _ct)).Value().Where(a => a.Held.Kind is ScopeKind.User or ScopeKind.Team)
            .ToDictionary(a => (a.Held.Kind, a.Held.Key));
        var without = (await _ca.Scope.ReadAsync(query with { Access = false }, _ct)).Value();

        held.Keys.ShouldBe(
        [
            (ScopeKind.User, "it@example.com"),
            (ScopeKind.User, "admin@example.com"),
            (ScopeKind.Team, "IT team"),
        ],
            ignoreOrder: true);
        var it = held[(ScopeKind.User, "it@example.com")];
        it.AppliedState.ShouldBe(State);
        it.Held.Fields.ShouldBe(new Dictionary<string, string>
        {
            ["first_name"] = "Alex",
            ["last_name"] = "Example",
            ["active"] = "true",
            ["roles"] = "BI-RL-APP on Example Pty Ltd, Evidence reviewer on Global",
        });
        held[(ScopeKind.User, "admin@example.com")].Held.Fields["roles"].ShouldBe("BI-RL-ADM on Global");
        held[(ScopeKind.User, "admin@example.com")].Managed.ShouldBeFalse();
        held[(ScopeKind.Team, "IT team")].Held.Fields.ShouldBe(new Dictionary<string, string>
        {
            ["domain"] = "Example Pty Ltd",
            ["leader"] = "it@example.com",
            ["members"] = "admin@example.com, it@example.com",
        });
        without.ShouldNotContain(a => a.Held.Kind == ScopeKind.User || a.Held.Kind == ScopeKind.Team);
    }

    [Fact]
    public async Task Reads_each_users_roles_through_groups_and_whether_mfa_is_on()
    {
        var access = (await _ca.Scope.ReadAccessAsync(_ct)).Value();

        access.Single(u => u.Email == "it@example.com").ShouldSatisfyAllConditions(
            u => u.MfaEnabled.ShouldBeTrue(),
            u => u.Roles.ShouldBe(["BI-RL-APP on Example Pty Ltd"]));
        access.Single(u => u.Email == "admin@example.com").ShouldSatisfyAllConditions(
            u => u.MfaEnabled.ShouldBeFalse(),
            u => u.Roles.ShouldBe(["BI-RL-ADM on Global"]));
    }

    private Task<Core.Results.Result<Core.Results.Unit>> Write(ScopeObject declared, ScopeAction action) =>
        _ca.Writer.WriteAsync(new ScopeWrite(new ScopeChange(declared.Kind, declared.Key, action, [], State), declared, "Global"), _ct);

    private (string Method, string Path)[] Sent() =>
        [.. _ca.Server.LogEntries.Where(e => e.RequestMessage!.Method != "GET").Select(e => (e.RequestMessage!.Method, e.RequestMessage.Path))];

    private WireMock.IRequestMessage Logged(string method, string path) =>
        _ca.Server.LogEntries.Single(e => e.RequestMessage!.Method == method && e.RequestMessage.Path == path).RequestMessage!;

    private string Posted(string resource) =>
        Json(JsonNode.Parse(_ca.Server.LogEntries.Single(e => e.RequestMessage!.Method == "POST" && e.RequestMessage.Path == $"/api/{resource}/").RequestMessage!.Body!)!);

    private static string Json(JsonNode node) =>
        new JsonObject([.. node.AsObject().OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone()))]).ToJsonString();

    private static ScopeObject Declared(ScopeKind kind, string key, params (string Field, string Value)[] fields) =>
        new(kind, key, fields.ToDictionary(f => f.Field, f => f.Value, StringComparer.Ordinal));

    private static JsonObject Relation(string id, string str) => new() { ["id"] = id, ["str"] = str };

    private static JsonObject Folder(string id, string name, string? parent) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["parent_folder"] = parent is null ? null : Relation(parent, "parent"),
    };

    /// <summary>A user as /api/users/ lists it: the user groups carry their IDs and whether they are built-in.</summary>
    private static JsonObject User(string id, string email, string first, string last, bool active, string? observation, bool mfa) => new()
    {
        ["id"] = id,
        ["email"] = email,
        ["first_name"] = first,
        ["last_name"] = last,
        ["is_active"] = active,
        ["observation"] = observation,
        ["has_mfa_enabled"] = mfa,
        ["user_groups"] = id == "u-admin"
            ? new JsonArray(Relation("g-adm", "Global - Administrator"))
            : new JsonArray(Relation("g-ex-app", "Example Pty Ltd - Approver"), Relation("g-reviewers", "Global - Evidence reviewer")),
    };

    private static JsonObject Group(string id, string name, string folder, string folderName, bool builtin = true) =>
        new() { ["id"] = id, ["name"] = name, ["folder"] = Relation(folder, folderName), ["builtin"] = builtin };

    private static JsonObject Role(string id, string name, bool builtin = true) => new() { ["id"] = id, ["name"] = name, ["builtin"] = builtin };

    private void Detail(string resource, JsonObject item) =>
        _ca.Server.Given(Request.Create().WithPath($"/api/{resource}/{item["id"]}/object/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(item.ToJsonString()));

    private void List(string resource, params JsonObject[] items) =>
        _ca.Server.Given(Request.Create().WithPath($"/api/{resource}/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["count"] = items.Length, ["results"] = new JsonArray(items) }.ToJsonString()));
}
