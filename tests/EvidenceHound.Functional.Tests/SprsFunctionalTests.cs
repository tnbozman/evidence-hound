using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s17-05: <c>hound sprs score</c> against the example CMMC Level 2 audit in CISO Assistant. The expected score is worked out here from
/// the audit's own rows, so the test holds whatever other tests have written to them. The output lands in <c>test-results/sprs/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class SprsFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-sprs-").FullName;

    [Fact]
    public async Task The_score_of_the_example_cmmc_audit_matches_its_rows()
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
            targets: []
            """, ct);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await HoundCli.RunAsync(
            ["sprs", "score", "--audit", "EX-CMMC-2026", "--config", config, "--format", "json"],
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            _ => { },
            ct);
        var results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "sprs")).FullName;
        await File.WriteAllTextAsync(Path.Combine(results, "score.json"), $"{output}{error}", ct);

        exit.ShouldBe(ExitCodes.Ok, $"{output}{error}");
        var score = JsonNode.Parse(output.ToString())!;
        var rows = await RowsAsync(ct);
        rows.Count.ShouldBe(110, "the audit selects L1 and L2, all 110 practices");
        var met = rows.Where(r => r.Result is "compliant" or "not_applicable").Select(r => r.Practice).ToHashSet(StringComparer.Ordinal);
        score["deductions"]!.AsArray().Select(d => d!["practice"]!.GetValue<string>()).ShouldNotContain(p => met.Contains(p));
        score["deductions"]!.AsArray().Count.ShouldBe(rows.Count(r => !met.Contains(r.Practice) && r.Practice != "CA.L2-3.12.4"));
        score["score"]!.GetValue<int>().ShouldBe(110 - score["deductions"]!.AsArray().Sum(d => d!["points"]!.GetValue<int>()));
    }

    public void Dispose() => Directory.Delete(_workDir, recursive: true);

    private async Task<List<(string Practice, string Result)>> RowsAsync(CancellationToken ct)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ciso.ApiUrl, "requirement-assessments/?compliance_assessment__ref_id=EX-CMMC-2026&limit=1000"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return [.. JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!["results"]!.AsArray()
            .Select(r => (r!["requirement"]!["ref_id"]!.GetValue<string>(), r["result"]!.GetValue<string>()))];
    }
}
