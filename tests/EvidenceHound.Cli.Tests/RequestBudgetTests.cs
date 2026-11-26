using System.Text.Json.Nodes;
using WireMock.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s18-04: how many requests <c>scope plan</c> and <c>run</c> send for the example repo against an empty fake CISO Assistant. A request
/// added later must be added to the budget here with the reason for it.
/// </summary>
public sealed class RequestBudgetTests : IDisposable
{
    /// <summary>Every read <c>scope plan</c> makes for the example scope, by path, and why.</summary>
    private static readonly Dictionary<string, string> ScopePlanBudget = new(StringComparer.Ordinal)
    {
        ["folders/"] = "the root folder, its domains, and the folders roles are held on",
        ["users/"] = "owners and approvers by email, and each user's MFA and roles for the access review",
        ["actors/"] = "owners that are teams or entities",
        ["user-groups/"] = "the teams the access file declares",
        ["teams/"] = "the teams the access file declares",
        ["roles/"] = "the roles the access review compares",
        ["role-assignments/"] = "who holds which role",
        ["frameworks/"] = "the frameworks the audits use",
        ["loaded-libraries/"] = "the loaded version of each framework's library",
        ["stored-libraries/?urn=urn:evidence-hound:risk:library:ai6"] = "the AI6 framework's stored library",
        ["stored-libraries/?urn=urn:evidence-hound:risk:library:disp-membership"] = "the DISP framework's stored library",
        ["stored-libraries/?urn=urn:example:risk:library:example-baseline"] = "the example baseline's stored library",
        ["stored-libraries/?urn=urn:example:risk:library:example-risk"] = "the example risk library",
        ["perimeters/"] = "the scope's perimeters",
        ["assets/"] = "the scope's systems",
        ["compliance-assessments/"] = "the scope's audits",
        ["applied-controls/"] = "the scope's controls",
        ["evidences/"] = "the scope's documents",
        ["task-templates/"] = "the evidence plan's tasks",
        ["risk-assessments/"] = "the risk studies",
        ["security-exceptions/"] = "the exceptions",
        ["risk-acceptances/"] = "the risk acceptances",
        ["resilience/business-impact-analysis/"] = "the BIAs",
        ["metrology/metric-definitions/"] = "the metric definitions",
        ["metrology/metric-instances/"] = "the metric instances",
        ["metrology/dashboards/"] = "the dashboards",
        ["settings/feature-flags/"] = "the feature flags the scope sets",
        ["settings/general/"] = "the general settings the scope sets",
    };

    private readonly WireMockServer _ca = WireMockServer.Start();
    private readonly string _dir = Directory.CreateTempSubdirectory("hound-budget-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scope_plan_stays_within_budget()
    {
        TestFiles.CopyExampleRepo(_dir);
        EmptyInstance();

        var (exit, error) = await RunAsync(["scope", "plan", Path.Combine(_dir, "scope"), "--config", Path.Combine(_dir, "hound.yaml")]);

        exit.ShouldBe(ExitCodes.Ok, error);
        var sent = Sent();
        sent.ShouldAllBe(r => r.Method == "GET");
        sent.GroupBy(r => r.Key).Where(g => g.Count() > 1).Select(g => g.Key).ShouldBeEmpty("each list is read once per command");
        sent.Select(r => r.Key).Except(ScopePlanBudget.Keys).ShouldBeEmpty("a new read needs a line in the budget with its reason");
        sent.Count.ShouldBeLessThanOrEqualTo(ScopePlanBudget.Count);
    }

    [Fact]
    public async Task Run_reads_the_state_once_per_audit()
    {
        var config = await RunConfigAsync();
        EmptyInstance();
        HeldAudit();
        _ca.Given(Request.Create().UsingAnyMethod()).AtPriority(90).WithTitle("writes")
            .RespondWith(Response.Create().WithStatusCode(201).WithBody(new JsonObject { ["id"] = "new-id" }.ToJsonString()));

        var (exit, error) = await RunAsync(["run", "--config", config]);

        exit.ShouldBe(ExitCodes.Ok, error);
        var reads = Sent().Where(r => r.Method == "GET").Select(r => r.Key).ToList();
        var all = string.Join("\n", reads);
        reads.Count(r => r.StartsWith("compliance-assessments/", StringComparison.Ordinal)).ShouldBe(2, all);
        reads.Count(r => r.StartsWith("requirement-assessments/", StringComparison.Ordinal)).ShouldBe(1, "the audit's rows are listed once, not looked up row by row\n" + all);
    }

    /// <summary>The example audit, held with its two rows not yet assessed, and each control it links (one stub answers every ref_id).</summary>
    private void HeldAudit()
    {
        _ca.Given(Request.Create().WithPath("/api/applied-controls/").UsingGet()).AtPriority(1)
            .RespondWith(Response.Create().WithBody(new JsonObject { ["count"] = 1, ["results"] = new JsonArray(new JsonObject { ["id"] = "ac-1", ["status"] = "to_do" }) }.ToJsonString()));
        _ca.Given(Request.Create().WithPath("/api/compliance-assessments/").WithParam("ref_id", "EXAMPLE-BASELINE-2026").UsingGet()).AtPriority(1)
            .RespondWith(Response.Create().WithBody(new JsonObject { ["count"] = 1, ["results"] = new JsonArray(new JsonObject { ["id"] = "ca-1" }) }.ToJsonString()));
        _ca.Given(Request.Create().WithPath("/api/compliance-assessments/ca-1/object/").UsingGet()).AtPriority(1)
            .RespondWith(Response.Create().WithBody(new JsonObject { ["id"] = "ca-1", ["selected_implementation_groups"] = new JsonArray() }.ToJsonString()));
        static JsonObject Row(string id, string refId) => new()
        {
            ["id"] = id,
            ["requirement"] = new JsonObject { ["urn"] = $"urn:example:risk:req_node:example-baseline:{refId.ToLowerInvariant()}", ["ref_id"] = refId, ["implementation_groups"] = new JsonArray() },
            ["result"] = "not_assessed",
            ["observation"] = "",
            ["evidences"] = new JsonArray(),
        };
        _ca.Given(Request.Create().WithPath("/api/requirement-assessments/").WithParam("compliance_assessment__ref_id", "EXAMPLE-BASELINE-2026").UsingGet()).AtPriority(1)
            .RespondWith(Response.Create().WithBody(new JsonObject { ["count"] = 2, ["results"] = new JsonArray(Row("ra-1", "EX-1.1"), Row("ra-3", "EX-3.1")) }.ToJsonString()));
    }

    private void EmptyInstance()
    {
        _ca.Given(Request.Create().UsingGet()).AtPriority(100)
            .RespondWith(Response.Create().WithBody(new JsonObject { ["count"] = 0, ["results"] = new JsonArray() }.ToJsonString()));
        _ca.Given(Request.Create().WithPath("/api/folders/").UsingGet()).AtPriority(1)
            .RespondWith(Response.Create().WithBody(new JsonObject { ["count"] = 1, ["results"] = new JsonArray(new JsonObject { ["id"] = "f-global", ["name"] = "Global", ["parent_folder"] = null }) }.ToJsonString()));
    }

    private async Task<string> RunConfigAsync()
    {
        var example = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "files", "examples", "example-pty-ltd.yaml"), _ct);
        var yaml = example
            .Replace("root: ../.dev/evidence", $"root: {Path.Combine(_dir, "evidence")}", StringComparison.Ordinal)
            .Replace("mapping: ../mappings/example.yaml", $"mapping: {Path.Combine(AppContext.BaseDirectory, "files", "mappings", "example.yaml")}", StringComparison.Ordinal);
        var path = Path.Combine(_dir, "hound.yaml");
        await File.WriteAllTextAsync(path, yaml, _ct);
        return path;
    }

    /// <summary>Each request as its method and its path under <c>/api/</c>, with the query but without the paging.</summary>
    private List<(string Method, string Key)> Sent() =>
        [.. _ca.LogEntries.Select(e => (e.RequestMessage!.Method, Key(e)))];

    private static string Key(ILogEntry entry)
    {
        var path = entry.RequestMessage!.Path["/api/".Length..];
        var query = string.Join("&", (entry.RequestMessage.Query ?? new Dictionary<string, WireMock.Types.WireMockList<string>>()).Where(q => q.Key is not ("limit" or "offset")).Select(q => $"{q.Key}={q.Value}"));
        return query.Length == 0 ? path : $"{path}?{query}";
    }

    private async Task<(int Exit, string Error)> RunAsync(string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await HoundCli.RunAsync(args, output, error, null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = "example-token-for-cli-tests", ["HOUND_TODAY"] = "2026-11-25" }, _ct);
        return (exit, $"{output}{error}");
    }

    public void Dispose()
    {
        _ca.Stop();
        _ca.Dispose();
        Directory.Delete(_dir, recursive: true);
    }
}
