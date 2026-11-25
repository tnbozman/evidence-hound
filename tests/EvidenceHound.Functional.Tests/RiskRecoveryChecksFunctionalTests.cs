using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s16-01: the ciso-assistant profile's risk and recovery checks read the risk studies, exceptions and business impact analysis the
/// example scope wrote to CISO Assistant. Each expected outcome is worked out here from CISO Assistant's own lists, so the test holds
/// whatever else the shared instance holds. The run's results land in <c>test-results/risk-recovery/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class RiskRecoveryChecksFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-risk-recovery-").FullName;

    [Fact]
    public async Task A_run_reports_risk_review_exceptions_and_recovery_tests_as_ciso_assistant_holds_them()
    {
        var ct = TestContext.Current.CancellationToken;

        var checks = await RunAsync(ct);

        var studies = (await ListAsync("risk-assessments/", ct)).Where(s => s["status"]?.GetValue<string>() != "deprecated").ToList();
        studies.ShouldNotBeEmpty("the example scope writes the EX-RISK-2026 risk study");
        checks["risk.studies_reviewed"]["outcome"]!.GetValue<string>().ShouldBe("pass", checks["risk.studies_reviewed"].ToJsonString());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var approved = (await ListAsync("security-exceptions/", ct)).Where(e => e["status"]?.GetValue<string>() == "approved").ToList();
        var expired = approved.Count(e => e["expiration_date"]?.GetValue<string>() is { } date && DateOnly.Parse(date, CultureInfo.InvariantCulture) < today);
        checks["risk.exceptions_current"]["outcome"]!.GetValue<string>().ShouldBe(
            approved.Count == 0 ? "not_applicable" : expired == 0 ? "pass" : "fail", checks["risk.exceptions_current"].ToJsonString());

        // Read as CISO Assistant's lists hold them, whatever other tests have written; the lists are kept with the results.
        var assets = await ListAsync("assets/", ct);
        var assessments = await ListAsync("resilience/asset-assessments/", ct);
        await File.WriteAllTextAsync(Path.Combine(_results, "assets.json"), new JsonArray([.. assets.Select(a => a.DeepClone())]).ToJsonString(), ct);
        await File.WriteAllTextAsync(Path.Combine(_results, "asset-assessments.json"), new JsonArray([.. assessments.Select(a => a.DeepClone())]).ToJsonString(), ct);
        var critical = assets
            .Where(HasRto)
            .Select(a => a["id"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);
        critical.ShouldNotBeEmpty("Example 365 has a recovery time objective in the example scope");
        var assessed = assessments.Where(a => Id(a["asset"]) is { } asset && critical.Contains(asset)).ToList();
        assessed.ShouldNotBeEmpty("the example business impact analysis assesses Example 365");
        var tested = assessed.Count(a => a["recovery_tested"]!.GetValue<bool>());
        var recovery = checks["bia.recovery_tested"];
        recovery["outcome"]!.GetValue<string>().ShouldBe(tested == assessed.Count ? "pass" : "fail", recovery.ToJsonString());
        recovery["observation"]!.GetValue<string>().ShouldStartWith(string.Create(CultureInfo.InvariantCulture, $"{tested} of {assessed.Count} business impact analysis assessments of critical systems"));
    }

    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "risk-recovery")).FullName;

    public void Dispose() => Directory.Delete(_workDir, recursive: true);

    /// <summary>Whether an asset has a recovery time objective: CISO Assistant's asset list gives each one set as text, such as <c>rto: 4h</c>.</summary>
    private static bool HasRto(JsonNode asset) =>
        asset["disaster_recovery_objectives"] is JsonArray objectives
        && objectives.Any(o => o?["str"] is JsonValue text && text.TryGetValue<string>(out var s) && s.StartsWith("rto: ", StringComparison.Ordinal));

    private static string? Id(JsonNode? relation) => relation switch
    {
        JsonObject o => o["id"]?.GetValue<string>(),
        JsonValue v when v.TryGetValue<string>(out var id) => id,
        _ => null,
    };

    private async Task<Dictionary<string, JsonNode>> RunAsync(CancellationToken ct)
    {
        var files = Path.Combine(AppContext.BaseDirectory, "files");
        var token = Path.Combine(_workDir, "ca_read_token");
        await File.WriteAllTextAsync(token, ciso.Token, ct);
        var evidence = Path.Combine(_workDir, "evidence");
        var config = Path.Combine(_workDir, "hound.yaml");
        await File.WriteAllTextAsync(config, $$"""
            ciso_assistant:
              url: {{ciso.ApiUrl}}
              token: env:HOUND_CA_TOKEN
            evidence:
              root: {{evidence}}
            collectors:
              - name: example-ciso-assistant
                type: rest
                schedule: "0 6 * * *"
                settings:
                  profile: ciso-assistant
                  profiles_path: {{Path.Combine(files, "profiles")}}
                  base_url: {{ciso.ApiUrl}}
                secrets:
                  token: file:{{token}}
            target:
              mapping: {{Path.Combine(files, "mappings", "asd-essentials-enterprise-it.yaml")}}
              compliance_assessment: EX-ESSENTIALS-2026
            """, ct);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await HoundCli.RunAsync(
            ["run", "--config", config, "--no-publish"],
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            _ => { },
            ct);
        var latest = Path.Combine(evidence, "_latest", "example-ciso-assistant.json");
        var results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "risk-recovery")).FullName;
        await File.WriteAllTextAsync(Path.Combine(results, "run.txt"), $"$ hound run --config hound.yaml --no-publish\n{output}{error}", ct);
        exit.ShouldBe(ExitCodes.Ok, $"{output}{error}");
        File.Copy(latest, Path.Combine(results, "example-ciso-assistant.json"), overwrite: true);

        var saved = JsonNode.Parse(await File.ReadAllTextAsync(latest, ct))!;
        return saved["checks"]!.AsArray().ToDictionary(c => c!["check_id"]!.GetValue<string>(), c => c!, StringComparer.Ordinal);
    }

    private async Task<List<JsonNode>> ListAsync(string path, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ciso.ApiUrl, path + "?limit=1000"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return [.. JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!["results"]!.AsArray().Select(n => n!)];
    }
}
