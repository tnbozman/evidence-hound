using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EvidenceHound.CisoAssistant.Tests;

/// <summary>Feature flags and general settings, read and written in the shapes v4.3.4's settings endpoints use.</summary>
public sealed class SettingsClientTests : IDisposable
{
    private readonly FakeCisoAssistant _ca = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public SettingsClientTests()
    {
        foreach (var resource in new[] { "users", "actors", "frameworks", "loaded-libraries", "perimeters", "assets", "compliance-assessments", "evidences", "applied-controls" })
        {
            Respond("GET", $"/api/{resource}/", new JsonObject { ["count"] = 0, ["results"] = new JsonArray() });
        }

        Respond("GET", "/api/folders/", new JsonObject { ["count"] = 1, ["results"] = new JsonArray(new JsonObject { ["id"] = "f-global", ["name"] = "Global", ["parent_folder"] = null }) });
        Respond("GET", "/api/settings/feature-flags/", new JsonObject { ["name"] = "feature-flags", ["tprm"] = true, ["metrology"] = true, ["bia"] = false });
        Respond("GET", "/api/settings/general/", new JsonObject
        {
            ["id"] = "s-general",
            ["name"] = "general",
            ["value"] = new JsonObject { ["enforce_mfa"] = false, ["currency"] = "€", ["risk_matrix_labels"] = new JsonArray("low", "high") },
        });
        Respond("PUT", "/api/settings/feature-flags/", new JsonObject());
        Respond("PATCH", "/api/settings/general/", new JsonObject());
    }

    public void Dispose() => _ca.Dispose();

    [Fact]
    public async Task Reads_every_flag_and_general_setting_when_asked()
    {
        var query = new ScopeStateQuery("Global", [], new Dictionary<string, IReadOnlyList<RequirementKey>>()) { Settings = true };

        var held = (await _ca.Scope.ReadAsync(query, _ct)).Value()
            .Where(a => a.Held.Kind is ScopeKind.FeatureFlag or ScopeKind.Setting)
            .ToDictionary(a => (a.Held.Kind, a.Held.Key), a => a.Held.Fields["value"]);
        var without = (await _ca.Scope.ReadAsync(query with { Settings = false }, _ct)).Value();

        held.ShouldBe(new Dictionary<(ScopeKind, string), string>
        {
            [(ScopeKind.FeatureFlag, "tprm")] = "true",
            [(ScopeKind.FeatureFlag, "metrology")] = "true",
            [(ScopeKind.FeatureFlag, "bia")] = "false",
            [(ScopeKind.Setting, "enforce_mfa")] = "false",
            [(ScopeKind.Setting, "currency")] = "\"€\"",
            [(ScopeKind.Setting, "risk_matrix_labels")] = "[\"low\",\"high\"]",
        }, ignoreOrder: true);
        without.ShouldNotContain(a => a.Held.Kind == ScopeKind.FeatureFlag || a.Held.Kind == ScopeKind.Setting);
    }

    [Fact]
    public async Task A_flag_is_written_with_every_other_flag_as_it_is_because_the_endpoint_resets_flags_left_out()
    {
        (await Write(ScopeKind.FeatureFlag, "tprm", "false")).Value();

        Body("PUT", "/api/settings/feature-flags/").ShouldBe(new JsonObject { ["bia"] = false, ["metrology"] = true, ["tprm"] = false }.ToJsonString());
    }

    [Fact]
    public async Task A_general_setting_is_patched_alone_and_the_others_are_kept_by_the_server()
    {
        (await Write(ScopeKind.Setting, "enforce_mfa", "true")).Value();

        Body("PATCH", "/api/settings/general/").ShouldBe(new JsonObject { ["value"] = new JsonObject { ["enforce_mfa"] = true } }.ToJsonString());
    }

    private Task<Core.Results.Result<Core.Results.Unit>> Write(ScopeKind kind, string name, string value)
    {
        var declared = new ScopeObject(kind, name, new Dictionary<string, string>(StringComparer.Ordinal) { ["value"] = value });
        return _ca.Writer.WriteAsync(new ScopeWrite(new ScopeChange(kind, name, ScopeAction.Update, [], null), declared, "Global"), _ct);
    }

    /// <summary>The request body, with an object's keys sorted.</summary>
    private string Body(string method, string path)
    {
        var body = JsonNode.Parse(_ca.Server.LogEntries.Single(e => e.RequestMessage!.Method == method && e.RequestMessage.Path == path).RequestMessage!.Body!)!;
        return Sorted(body)!.ToJsonString();
    }

    private static JsonNode? Sorted(JsonNode? node) => node is JsonObject o
        ? new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value?.DeepClone()))))
        : node?.DeepClone();

    private void Respond(string method, string path, JsonObject body) =>
        _ca.Server.Given(Request.Create().WithPath(path).UsingMethod(method)).RespondWith(Response.Create().WithStatusCode(200).WithBody(body.ToJsonString()));
}
