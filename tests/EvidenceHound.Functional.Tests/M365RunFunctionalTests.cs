using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;
using EvidenceHound.Collectors.M365;
using EvidenceHound.Collectors.M365.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EvidenceHound.Functional.Tests;

/// <summary><c>hound run</c> with the m365 collector against the recorded example.com tenant updates the example audit.</summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class M365RunFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-m365-functional-").FullName;

    private readonly RecordedTenant _tenant = RecordedTenant.Start();

    [Fact]
    public async Task Run_with_m365_collector_updates_example_audit()
    {
        var ct = TestContext.Current.CancellationToken;

        var (exitCode, summary) = await RunAsync(ct);

        exitCode.ShouldBe(ExitCodes.Ok, summary.ToJsonString());
        summary["collectors"]![0]!["publish"]!["failed"]!.GetValue<int>().ShouldBe(0);
        var requirement = (await GetAsync(
            "requirement-assessments/?compliance_assessment__ref_id=EXAMPLE-BASELINE-2026&requirement__ref_id=EX-1.2", ct))["results"]![0]!;
        requirement["result"]!.GetValue<string>().ShouldBe("compliant");
        requirement["observation"]!.GetValue<string>().ShouldContain("phishing-resistant authentication strength is required for all 14 privileged roles");
        requirement["evidences"]!.AsArray().Count.ShouldBe(3);
        var evidence = (await GetAsync("evidences/?search=example-m365", ct))["results"]!.AsArray()
            .Select(e => e!["ref_id"]!.GetValue<string>());
        evidence.ShouldContain("example-m365/conditional-access-policies.json");
    }

    public void Dispose()
    {
        _tenant.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }

    private async Task<(int ExitCode, JsonNode Summary)> RunAsync(CancellationToken ct)
    {
        var files = Path.Combine(AppContext.BaseDirectory, "files");
        var config = Path.Combine(_workDir, "hound.yaml");
        await File.WriteAllTextAsync(config, $$"""
            ciso_assistant:
              url: http://localhost:8000/api/
              token: env:HOUND_CA_TOKEN
            evidence:
              root: {{Path.Combine(_workDir, "evidence")}}
            collectors:
              - name: example-m365
                type: m365
                schedule: "0 3 * * *"
                settings:
                  tenant_id: {{RecordedTenant.TenantId}}
                  client_id: {{RecordedTenant.ClientId}}
                  graph_url: {{_tenant.Url}}
                  break_glass_accounts: {{RecordedTenant.BreakGlass}}
                  supported_windows_builds: "10.0.22631, 10.0.26100"
            target:
              mapping: {{Path.Combine(files, "fixtures", "m365-mapping.yaml")}}
              compliance_assessment: EXAMPLE-BASELINE-2026
              health_control: AC-AUTO-EVIDENCE
            """, ct);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            ["run", "--config", config],
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            services => services.Replace(ServiceDescriptor.Singleton<IGraphCredentialFactory>(new TestCredentialFactory())),
            ct);
        var text = output.ToString();
        return (exitCode, string.IsNullOrWhiteSpace(text) ? new JsonObject { ["stderr"] = error.ToString() } : JsonNode.Parse(text)!);
    }

    private async Task<JsonNode> GetAsync(string path, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ciso.ApiUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
    }
}
