using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Scope;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EvidenceHound.CisoAssistant.Tests;

/// <summary>
/// The CISO Assistant endpoints the client calls, checked against the committed OpenAPI snapshot of the release hound
/// supports, so an upgrade that drops one fails here rather than in production.
/// </summary>
public sealed class OpenApiSnapshotTests : IDisposable
{
    private readonly FakeCisoAssistant _ca = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public void Dispose() => _ca.Dispose();

    [Fact]
    public void Every_endpoint_hound_calls_is_in_the_snapshot()
    {
        var snapshot = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ciso-assistant", "openapi-v4.3.4.json")))!;
        var paths = snapshot["paths"]!.AsObject();

        var missing = ApiEndpoints.Used
            .Where(e => paths[e.Path]?[e.Method.ToLowerInvariant()] is null)
            .Select(e => $"{e.Method} {e.Path}");

        ApiEndpoints.Used.ShouldNotBeEmpty();
        missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_request_the_client_makes_is_a_listed_endpoint()
    {
        _ca.Server.Given(Request.Create().UsingAnyMethod()).RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(AnyObject));
        var client = _ca.Client;

        await client.CheckHealthAsync(_ct);
        await client.FindRequirementAssessmentsAsync("EX-1", RequirementKey.Ref("R"), _ct);
        await client.FindRequirementAssessmentsAsync("EX-1", RequirementKey.Urn("urn:example:req:r"), _ct);
        await client.FindAuditAsync("EX-1", _ct);
        await client.ListRequirementAssessmentsAsync("EX-1", _ct);
        await client.FindEvidenceAsync("R", _ct);
        await client.FindAppliedControlStatusAsync("R", _ct);
        await client.UpdateRequirementAssessmentAsync("x-1", new RequirementAssessmentValues(ComplianceResult.Compliant, "ok", ["x-2"]), _ct);
        await client.FindOrCreateEvidenceAsync("R", "file.json", _ct);
        await client.FindOrCreateEvidenceAsync("NEW", "new.json", _ct);
        await client.UploadEvidenceRevisionAsync("x-1", new EvidenceFile("file.json", "{}"u8.ToArray()), _ct);
        await client.LinkEvidenceToAppliedControlAsync("R", "x-9", _ct);
        await client.UpdateAppliedControlAsync("R", ControlState.Active, _ct);
        await _ca.Scope.ReadAsync(new ScopeStateQuery("Global", [], new Dictionary<string, IReadOnlyList<RequirementKey>> { ["R"] = [RequirementKey.Ref("R")] }), _ct);
        foreach (var kind in new[] { ScopeKind.Domain, ScopeKind.Perimeter, ScopeKind.System })
        {
            var declared = new ScopeObject(kind, kind == ScopeKind.Perimeter ? "Global/Global" : "Global", new Dictionary<string, string> { ["name"] = "Global" });
            foreach (var action in Enum.GetValues<ScopeAction>())
            {
                var change = new ScopeChange(kind, declared.Key, action, [], "0123456789abcdef");
                await _ca.Writer.WriteAsync(new ScopeWrite(change, action == ScopeAction.Delete ? null : declared, "Global"), _ct);
            }
        }

        var templates = ApiEndpoints.Used.Select(e => (e.Method, Pattern: new Regex("^" + Regex.Replace(Regex.Escape(e.Path), @"\\\{[a-z_]+}", "[^/]+") + "$"))).ToArray();
        var unlisted = _ca.Server.LogEntries
            .Select(entry => (entry.RequestMessage!.Method, entry.RequestMessage.Path))
            .Distinct()
            .Where(request => !templates.Any(t => t.Method == request.Method && t.Pattern.IsMatch(request.Path)))
            .Select(request => $"{request.Method} {request.Path}");

        unlisted.ShouldBeEmpty();
    }

    /// <summary>One body that lets every client call go on to its next request.</summary>
    private static readonly string AnyObject = new JsonObject
    {
        ["id"] = "x-1",
        ["count"] = 1,
        ["evidences"] = new JsonArray(),
        ["selected_implementation_groups"] = new JsonArray(),
        ["results"] = new JsonArray(new JsonObject
        {
            ["id"] = "x-1",
            ["ref_id"] = "R",
            ["name"] = "Global",
            ["folder"] = "x-1",
            ["status"] = "active",
            ["result"] = "compliant",
            ["attachment_hash"] = new string('a', 64),
            ["requirement"] = new JsonObject { ["urn"] = "urn:example:req:r", ["ref_id"] = "R", ["implementation_groups"] = new JsonArray() },
        }),
    }.ToJsonString();
}
