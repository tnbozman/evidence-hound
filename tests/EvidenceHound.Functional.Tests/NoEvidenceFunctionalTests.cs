using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s15-14: a per-system audit with one requirement met by hand and every other row waiting for evidence. CISO Assistant's compliance
/// percentage counts compliant over compliant, partially and non-compliant rows only, so with the waiting rows left not assessed it read
/// 100%. hound applies them as non-compliant, saying "No evidence yet", so CISO Assistant's own figure is honest. Runs on an audit of its
/// own, deleted at the end. Each step's output lands in <c>test-results/no-evidence/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class NoEvidenceFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    public const string RefId = "EXAMPLE-NOEVIDENCE-FUNC-2026";

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-no-evidence-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "no-evidence")).FullName;

    private string Scope => Path.Combine(_repo, "scope");

    [Fact]
    public async Task Rows_with_no_evidence_are_non_compliant_so_the_audits_percentage_is_not_100()
    {
        var ct = TestContext.Current.CancellationToken;
        TestRepo.CopyExample(_repo);
        var example = await File.ReadAllTextAsync(Path.Combine(Scope, "audits", "ex-e8-2026.yaml"), ct);
        var audit = example[..example.IndexOf("documents:", StringComparison.Ordinal)]
            .Replace("ref_id: EX-E8-2026", $"ref_id: {RefId}", StringComparison.Ordinal)
            .Replace("name: Essential Eight ML2 2026", "name: Essential Eight no-evidence test", StringComparison.Ordinal)
            .Replace("systems: [Example 365, Example laptops, Example backup service]", "systems: [Example laptops]", StringComparison.Ordinal)
            + example[example.IndexOf("assessments:", StringComparison.Ordinal)..];
        await File.WriteAllTextAsync(Path.Combine(Scope, "audits", "example-noevidence-func-2026.yaml"), audit, ct);
        var config = Path.Combine(_repo, "hound.yaml");
        try
        {
            var applied = await StepAsync("1-apply", ["scope", "apply", Scope, "--config", config], ct);
            applied.Exit.ShouldBe(ExitCodes.Ok, applied.Error);

            var rows = await RowsAsync(ct);
            var counts = rows.GroupBy(r => r!["result"]!.GetValue<string>()).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(_results, "2-results.json"), new JsonObject([.. counts.Select(c => KeyValuePair.Create(c.Key, (JsonNode?)c.Value))]).ToJsonString(), ct);
            var ours = JsonNode.Parse(applied.Output)!["changes"]!.AsArray()
                .Where(c => c!["kind"]!.GetValue<string>() == "assessment" && c["key"]!.GetValue<string>().StartsWith(RefId, StringComparison.Ordinal))
                .GroupBy(c => $"{c!["action"]}/{c["outcome"]}").Select(g => $"{g.Key}: {g.Count()}");
            var open = rows.Where(r => r!["result"]!.GetValue<string>() == "not_assessed").Select(r => r!["requirement"]?["str"]?.ToString() ?? r!["id"]!.ToString()).Take(5);
            counts.GetValueOrDefault("not_assessed").ShouldBe(
                0, $"no row waits as not assessed, which CISO Assistant leaves out of its percentage. Applied rows: {string.Join(", ", ours)}. Still open: {string.Join("; ", open)}");
            counts.GetValueOrDefault("compliant").ShouldBe(1);
            var nonCompliant = counts.GetValueOrDefault("non_compliant");
            nonCompliant.ShouldBeGreaterThan(40);
            var percent = 100.0 * counts["compliant"] / (counts["compliant"] + counts.GetValueOrDefault("partially_compliant") + nonCompliant);
            percent.ShouldBeLessThan(5, "CISO Assistant's compliance percentage: compliant over compliant, partially and non-compliant rows");
            rows.Where(r => r!["result"]!.GetValue<string>() == "non_compliant")
                .ShouldAllBe(r => r!["observation"]!.GetValue<string>().StartsWith("No evidence yet: ", StringComparison.Ordinal));

            var plan = await StepAsync("3-plan-after", ["scope", "plan", Scope, "--config", config, "--format", "json"], ct);
            plan.Exit.ShouldBe(ExitCodes.Ok, plan.Error);
            JsonNode.Parse(plan.Output)!["changes"]!.AsArray().Where(c => c!["key"]!.GetValue<string>().StartsWith(RefId, StringComparison.Ordinal))
                .ShouldBeEmpty("applying again changes nothing: the rows hold what the scope declares");
        }
        finally
        {
            var listed = await SendAsync(HttpMethod.Get, $"compliance-assessments/?ref_id={RefId}", ct);
            if (listed?["results"]!.AsArray().FirstOrDefault() is { } created)
            {
                await SendAsync(HttpMethod.Delete, $"compliance-assessments/{created["id"]!.GetValue<string>()}/", ct);
            }
        }
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private async Task<JsonNode?[]> RowsAsync(CancellationToken ct)
    {
        var rows = new List<JsonNode?>();
        for (var path = $"requirement-assessments/?compliance_assessment__ref_id={RefId}&requirement__assessable=true&limit=500"; path is not null;)
        {
            var page = (await SendAsync(HttpMethod.Get, path, ct))!;
            rows.AddRange(page["results"]!.AsArray());
            path = page["next"]?.GetValue<string>() is { } next ? new Uri(next).PathAndQuery[(ciso.ApiUrl.AbsolutePath.Length)..] : null;
        }

        return [.. rows];
    }

    private async Task<(int Exit, string Output, string Error)> StepAsync(string name, string[] args, CancellationToken ct)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            args, output, error, configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token }, ct);
        await File.WriteAllTextAsync(Path.Combine(_results, $"{name}.txt"), $"$ hound {string.Join(' ', args.Select(Path.GetFileName))}\n{output}{error}", ct);
        return (exitCode, output.ToString(), error.ToString());
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(method, new Uri(ciso.ApiUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);
        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.IsSuccessStatusCode.ShouldBeTrue(string.Create(CultureInfo.InvariantCulture, $"{method} {path}: {(int)response.StatusCode} {text}"));
        return text.Length == 0 ? null : JsonNode.Parse(text);
    }
}
