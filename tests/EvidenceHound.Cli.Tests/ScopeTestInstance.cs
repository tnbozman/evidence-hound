using System.Text.Json.Nodes;
using EvidenceHound.Core.Frameworks;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// A small scope repo (one domain, perimeter, system, audit and control) and a WireMock CISO Assistant that already holds its structure,
/// for tests that plan and apply it. <see cref="ScopePlanCommandTests"/> uses the static helpers with its own stub.
/// </summary>
internal sealed class ScopeTestInstance : IDisposable
{
    public const string Token = "example-token-for-cli-tests";

    public static readonly FrameworkSnapshot Snapshot = new(
        "urn:example:risk:library:example-baseline",
        "urn:example:risk:framework:example-baseline",
        1,
        [new ImplementationGroup("1", "Level one")],
        [new SnapshotNode("urn:example:risk:req_node:example-baseline:ex-1.1", "EX-1.1", null, ["1"], "Every account uses multi-factor authentication")]);

    public WireMockServer Ca { get; } = WireMockServer.Start();

    public string Repo { get; } = Directory.CreateTempSubdirectory("hound-scope-instance-").FullName;

    public string ScopeDir => Path.Combine(Repo, "scope");

    public Dictionary<string, string?> Settings => new() { ["HOUND_CA_URL"] = $"{Ca.Url}/api/", ["HOUND_CA_TOKEN"] = Token, ["HOUND_TODAY"] = "2026-11-23" };

    public Task WriteScopeAsync(CancellationToken ct) => WriteScopeAsync(Repo, ct);

    /// <summary>The scope files and the framework snapshot in <paramref name="repo"/>, with <paramref name="audit"/> as the one audit.</summary>
    public static async Task WriteScopeAsync(string repo, CancellationToken ct, string audit = ScopeCommandTests.Audit)
    {
        var scope = Path.Combine(repo, "scope");
        Directory.CreateDirectory(Path.Combine(scope, "audits"));
        Directory.CreateDirectory(Path.Combine(repo, "frameworks"));
        await File.WriteAllTextAsync(Path.Combine(scope, "setup.yaml"), ScopeCommandTests.Setup, ct);
        await File.WriteAllTextAsync(Path.Combine(scope, "controls.yaml"), ScopeCommandTests.Controls, ct);
        await File.WriteAllTextAsync(Path.Combine(scope, "audits", "example-baseline-2026.yaml"), audit, ct);
        await File.WriteAllTextAsync(Path.Combine(repo, "frameworks", "example-baseline.json"), FrameworkSnapshotJson.Write(Snapshot), ct);
    }

    /// <summary>The scope's framework, domain, perimeter and system as hound last wrote them; the audit and control exist elsewhere.</summary>
    public void HeldStructure() => HeldStructure(Ca);

    /// <summary>
    /// <paramref name="ca"/> holding the scope's framework, domain, perimeter and system as <c>hound</c> last wrote them, and, outside the
    /// scope's folders, objects the writer finds by ref_id: an audit with one row, and the control.
    /// </summary>
    public static void HeldStructure(WireMockServer ca)
    {
        static JsonObject In(string id, string folder, string? refId = null, string? name = null) =>
            new() { ["id"] = id, ["name"] = name, ["ref_id"] = refId, ["folder"] = new JsonObject { ["id"] = folder, ["str"] = folder } };
        static string Marked(params (string Name, string Value)[] fields) =>
            $"[hound scope {Core.Scope.ScopeMarker.Fingerprint(fields.ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal))}]";

        List(ca, "folders", new JsonObject { ["id"] = "f-global", ["name"] = "Global", ["parent_folder"] = null }, new JsonObject { ["id"] = "f-ex", ["name"] = "Example Pty Ltd", ["parent_folder"] = new JsonObject { ["id"] = "f-global", ["str"] = "Global" } });
        Object(ca, "folders", "f-ex", new JsonObject { ["id"] = "f-ex", ["name"] = "Example Pty Ltd", ["parent_folder"] = "f-global", ["description"] = Marked() });
        List(ca, "frameworks", new JsonObject { ["id"] = "fw-1", ["urn"] = Snapshot.FrameworkUrn, ["library"] = new JsonObject { ["id"] = "lib-1", ["urn"] = Snapshot.LibraryUrn } });
        List(ca, "loaded-libraries", new JsonObject { ["id"] = "lib-1", ["urn"] = Snapshot.LibraryUrn, ["version"] = Snapshot.Version });
        List(ca, "perimeters", In("p-1", "f-ex", name: "Example baseline"));
        Object(ca, "perimeters", "p-1", new JsonObject { ["id"] = "p-1", ["name"] = "Example baseline", ["folder"] = "f-ex", ["description"] = Marked(("name", "Example baseline"), ("domain", "Example Pty Ltd")) });
        List(ca, "assets", In("a-1", "f-ex", name: "Example 365"));
        Object(ca, "assets", "a-1", new JsonObject
        {
            ["id"] = "a-1",
            ["name"] = "Example 365",
            ["type"] = "PR",
            ["folder"] = "f-ex",
            ["owner"] = new JsonArray(),
            ["parent_assets"] = new JsonArray(),
            ["description"] = Marked(("type", "primary"), ("domain", "Example Pty Ltd")),
        });
        List(ca, "compliance-assessments", In("ca-1", "f-elsewhere", "EXAMPLE-BASELINE-2026"));
        ca.Given(Request.Create().WithPath("/api/compliance-assessments/ca-1/object/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["id"] = "ca-1", ["selected_implementation_groups"] = new JsonArray("1") }.ToJsonString()));
        List(ca, "requirement-assessments", new JsonObject { ["id"] = "ra-1", ["requirement"] = new JsonObject { ["urn"] = Snapshot.Nodes[0].Urn, ["ref_id"] = "EX-1.1", ["implementation_groups"] = new JsonArray("1") } });
        List(ca, "applied-controls", In("ac-1", "f-elsewhere", "AC-MFA"));
        foreach (var resource in new[] { "users", "actors", "evidences" })
        {
            List(ca, resource);
        }
    }

    /// <summary>Someone made the control in CISO Assistant by hand after the plan, which planned to create it.</summary>
    public void ControlMadeByHand()
    {
        Ca.Given(Request.Create().WithPath("/api/applied-controls/").UsingGet()).AtPriority(0)
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject
            {
                ["count"] = 1,
                ["results"] = new JsonArray(new JsonObject { ["id"] = "ac-new", ["ref_id"] = "AC-MFA", ["name"] = "Made by hand", ["folder"] = new JsonObject { ["id"] = "f-ex", ["str"] = "f-ex" } }),
            }.ToJsonString()));
        Object(Ca, "applied-controls", "ac-new", new JsonObject { ["id"] = "ac-new", ["ref_id"] = "AC-MFA", ["name"] = "Made by hand", ["folder"] = "f-ex" });
    }

    public void Accept(string method, string path) => Accept(Ca, method, path);

    public static void Accept(WireMockServer ca, string method, string path) =>
        ca.Given(Request.Create().WithPath(path).UsingMethod(method))
            .RespondWith(Response.Create().WithStatusCode(method == "POST" ? 201 : 200).WithBody(new JsonObject { ["id"] = "new-id" }.ToJsonString()));

    public void Dispose()
    {
        Ca.Stop();
        Ca.Dispose();
        Directory.Delete(Repo, recursive: true);
    }

    public static void List(WireMockServer ca, string resource, params JsonObject[] items) =>
        ca.Given(Request.Create().WithPath($"/api/{resource}/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new JsonObject { ["count"] = items.Length, ["results"] = new JsonArray(items) }.ToJsonString()));

    public static void Object(WireMockServer ca, string resource, string id, JsonObject body) =>
        ca.Given(Request.Create().WithPath($"/api/{resource}/{id}/object/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(body.ToJsonString()));
}
