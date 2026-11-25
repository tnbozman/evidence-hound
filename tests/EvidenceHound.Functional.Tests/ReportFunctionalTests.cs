using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Reporting;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s12-04: <c>hound report snapshot</c> against the seeded CISO Assistant: every list it reads exists in this version, both example
/// programmes are counted by their own groups, and the diff between two snapshots lists a requirement whose result changed between them.
/// The snapshots land in <c>test-results/reports/</c>, which CI keeps.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class ReportFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-report-functional-").FullName;

    [Fact]
    public async Task Two_snapshots_show_the_requirement_whose_result_changed()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = Path.Combine(_workDir, "hound.yaml");
        await File.WriteAllTextAsync(config, $$"""
            ciso_assistant:
              url: http://localhost:8000/api/
              token: env:HOUND_CA_TOKEN
            evidence:
              root: {{Path.Combine(_workDir, "evidence")}}
            collectors: []
            target:
              mapping: {{Path.Combine(AppContext.BaseDirectory, "files", "mappings", "essential-eight-2023.yaml")}}
              compliance_assessment: EX-E8-2026
            """, ct);
        var row = await RowAsync("EX-E8-2026", "ISM-1511", ct);
        var was = row["result"]!.GetValue<string>();
        try
        {
            var before = await SnapshotAsync("2026-10", config, ct);
            await PatchAsync(row["id"]!.GetValue<string>(), was == "non_compliant" ? "compliant" : "non_compliant", ct);
            var after = await SnapshotAsync("2026-11", config, ct);

            before.Programmes.Select(p => p.Id).ShouldBe(["essential-eight", "disp", "ai6"]);
            before.Programmes[0].Counts.Select(c => c.Group).Distinct().ShouldBe(["1", "2"]);
            before.Programmes[1].Counts.Select(c => c.Group).Distinct().ShouldBe(["M"]);
            before.Programmes[2].Counts.Select(c => c.Group).Distinct().ShouldBe(["F"]);
            var change = Snapshots.Diff(before, after).Requirements.ShouldHaveSingleItem();
            change.RefId.ShouldBe("ISM-1511");
            change.From.ShouldNotBeNull();
            Names.Of(change.From.Value).ShouldBe(was);
        }
        finally
        {
            await PatchAsync(row["id"]!.GetValue<string>(), was, ct);
        }
    }

    public void Dispose() => Directory.Delete(_workDir, recursive: true);

    private async Task<PostureSnapshot> SnapshotAsync(string period, string config, CancellationToken ct)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await HoundCli.RunAsync(
            ["report", "snapshot", "--period", period, "--config", config, "--scope", CisoAssistantFixture.ExampleScope],
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            ct);
        exit.ShouldBe(ExitCodes.Ok, output.ToString() + error);
        var file = Path.Combine(_workDir, "evidence", "reports", $"posture-{period}.json");
        var json = await File.ReadAllTextAsync(file, ct);
        var results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "reports")).FullName;
        await File.WriteAllTextAsync(Path.Combine(results, $"posture-{period}.json"), json, ct);
        return PostureSnapshotJson.Read(json, file).Match(s => s, ps => throw new InvalidOperationException(ps[0].Message));
    }

    private async Task<JsonNode> RowAsync(string audit, string refId, CancellationToken ct)
    {
        using var http = Http();
        var page = await http.GetFromJsonAsync<JsonNode>(new Uri(ciso.ApiUrl, $"requirement-assessments/?compliance_assessment__ref_id={audit}&requirement__ref_id={refId}"), ct);
        return page!["results"]!.AsArray().First()!;
    }

    private async Task PatchAsync(string id, string result, CancellationToken ct)
    {
        using var http = Http();
        // StringContent, not PatchAsJsonAsync: a chunked body (no Content-Length) reaches Django empty, so the PATCH would change nothing.
        using var content = new StringContent(new JsonObject { ["result"] = result }.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.PatchAsync(new Uri(ciso.ApiUrl, $"requirement-assessments/{id}/"), content, ct);
        response.EnsureSuccessStatusCode();
    }

    private HttpClient Http()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);
        return http;
    }
}
