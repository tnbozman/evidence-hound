using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EvidenceHound.Collectors.M365.Tests;

/// <summary>
/// A fictional <c>example.com</c> tenant replayed from the recorded Graph responses in <c>tests/fixtures/m365</c>. Each file
/// <c>&lt;name&gt;.json</c> is the first page of a collection, and <c>&lt;name&gt;.N.json</c> the page whose <c>$skiptoken</c> is N;
/// <c>{{graph}}</c> in a file stands for this server's address. The name is also the evidence file the collector writes.
/// </summary>
internal sealed class RecordedTenant : IDisposable
{
    public static readonly string TenantId = "00000000-0000-4000-8000-00000000e001";

    public static readonly string ClientId = "00000000-0000-4000-8000-00000000c001";

    public static readonly string BreakGlass = "00000000-0000-4000-8000-0000000000b1, 00000000-0000-4000-8000-0000000000b2";

    /// <summary>The Graph path each recorded collection answers.</summary>
    public static readonly IReadOnlyDictionary<string, string> Paths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["users"] = "/v1.0/users",
        ["mfa-registration"] = "/v1.0/reports/authenticationMethods/userRegistrationDetails",
        ["conditional-access-policies"] = "/v1.0/identity/conditionalAccess/policies",
        ["role-assignments"] = "/v1.0/roleManagement/directory/roleAssignments",
        ["role-eligibility-schedules"] = "/v1.0/roleManagement/directory/roleEligibilitySchedules",
        ["role-assignment-schedule-instances"] = "/v1.0/roleManagement/directory/roleAssignmentScheduleInstances",
    };

    private readonly RecordedApi _api = RecordedApi.Start();

    private RecordedTenant(Func<string, JsonArray, JsonArray>? edit)
    {
        foreach (var (name, path) in Paths)
        {
            var pages = Pages(name, _api.Url);
            if (edit is not null)
            {
                pages = [new JsonObject { ["value"] = edit(name, Merge(pages)) }];
            }

            for (var i = 0; i < pages.Count; i++)
            {
                var request = Request.Create().WithPath(path).UsingGet();
                request = i == 0 ? request : request.WithParam("$skiptoken", $"{i + 1}");
                _api.Server.Given(request).AtPriority(i == 0 ? 10 : 1)
                    .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(pages[i].ToJsonString()));
            }
        }
    }

    public Uri Url => _api.Url;

    public IHttpClientFactory HttpClients => _api.HttpClients;

    public WireMock.Server.WireMockServer Server => _api.Server;

    private static string Folder => Path.Combine(AppContext.BaseDirectory, "m365");

    /// <summary>Starts the tenant; <paramref name="edit"/> may change a collection's items (all pages merged into one).</summary>
    public static RecordedTenant Start(Func<string, JsonArray, JsonArray>? edit = null) => new(edit);

    /// <summary>Every recorded collection's items, all pages merged, for pure tests that need no server.</summary>
    public static Dictionary<string, JsonArray> Items() =>
        Paths.Keys.ToDictionary(name => name, name => Merge(Pages(name, new Uri("https://graph.example.test/"))), StringComparer.Ordinal);

    /// <summary>Settings for a collector instance that reads this tenant.</summary>
    public Dictionary<string, string> Settings() => new(StringComparer.Ordinal)
    {
        ["tenant_id"] = TenantId,
        ["client_id"] = ClientId,
        ["graph_url"] = Url.ToString(),
        ["break_glass_accounts"] = BreakGlass,
    };

    /// <summary>Answers <paramref name="name"/>'s path with an error instead of the recording.</summary>
    public RecordedTenant Fail(string name, int status, string code, string message)
    {
        _api.Server.Given(Request.Create().WithPath(Paths[name]).UsingGet()).AtPriority(0)
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json")
                .WithBody(new JsonObject { ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString()));
        return this;
    }

    /// <summary>Answers the Privileged Identity Management paths as Graph does for a tenant without the licence for it.</summary>
    public RecordedTenant WithoutPim()
    {
        Fail("role-eligibility-schedules", 400, "AadPremiumLicenseRequired", "The tenant needs to have Microsoft Entra ID P2 or Microsoft Entra ID Governance license.");
        return Fail("role-assignment-schedule-instances", 400, "AadPremiumLicenseRequired", "The tenant needs to have Microsoft Entra ID P2 or Microsoft Entra ID Governance license.");
    }

    /// <summary>Answers every path with 401, as Graph does for a token it does not accept.</summary>
    public RecordedTenant RefuseAll()
    {
        foreach (var name in Paths.Keys)
        {
            Fail(name, 401, "InvalidAuthenticationToken", "Access token validation failure. Invalid audience.");
        }

        return this;
    }

    public void Dispose() => _api.Dispose();

    private static List<JsonObject> Pages(string name, Uri graph)
    {
        var files = Directory.GetFiles(Folder, $"{name}*.json")
            .Where(f => Path.GetFileName(f) == $"{name}.json" || System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), $"^{name}\\.[0-9]+\\.json$"))
            .Order(StringComparer.Ordinal);
        return [.. files.Select(f => JsonNode.Parse(File.ReadAllText(f).Replace("{{graph}}", graph.ToString(), StringComparison.Ordinal))!.AsObject())];
    }

    private static JsonArray Merge(IEnumerable<JsonObject> pages) => [.. pages.SelectMany(p => p["value"]!.AsArray()).Select(i => i?.DeepClone())];
}
