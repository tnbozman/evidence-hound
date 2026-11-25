using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;
using EvidenceHound.Collectors.M365;
using EvidenceHound.Collectors.M365.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EvidenceHound.Functional.Tests;

/// <summary>s14-02: one <c>hound run</c> fills the DISP readiness audit's cyber rows from the same evidence as the Essential Eight audit.</summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class DispRunFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-disp-run-").FullName;
    private readonly RecordedTenant _tenant = RecordedTenant.Start();

    [Fact]
    public async Task One_run_fills_the_disp_cyber_rows_from_the_essential_eight_evidence()
    {
        var ct = TestContext.Current.CancellationToken;

        var (exitCode, summary) = await RunAsync(ct);

        exitCode.ShouldBe(ExitCodes.Ok, summary.ToJsonString());
        // Every target audit is planned; publish lists only audits with an update, and the shared CISO Assistant may already hold
        // an audit's results from an earlier test, so the audits come from the plan.
        var planned = summary["collectors"]![0]!["changes"]!.AsArray();
        planned.Select(a => a!["compliance_assessment"]!.GetValue<string>()).ShouldBe(["EX-E8-2026", "EX-DISP-2026"]);
        var audits = summary["collectors"]![0]!["publish"]!["audits"]!.AsArray();
        audits.Select(a => a!["failed"]!.GetValue<int>()).ShouldAllBe(failed => failed == 0);

        var e8 = (await GetAsync("requirement-assessments/?compliance_assessment__ref_id=EX-E8-2026&requirement__ref_id=ISM-1504", ct))["results"]![0]!;
        var mfa = (await GetAsync("requirement-assessments/?compliance_assessment__ref_id=EX-DISP-2026&requirement__ref_id=DISP-CYB-07", ct))["results"]![0]!;
        var policies = (await GetAsync("evidences/?search=example-m365%2Fconditional-access-policies.json", ct))["results"]!.AsArray()
            .Where(e => e!["ref_id"]!.GetValue<string>() == "example-m365/conditional-access-policies.json").ToList();
        var id = policies.ShouldHaveSingleItem()!["id"]!.GetValue<string>();
        Ids(e8["evidences"]).ShouldContain(id);
        Ids(mfa["evidences"]).ShouldContain(id);
        mfa["observation"]!.GetValue<string>().ShouldContain("identity.mfa.enforced_conditional_access");
    }

    public void Dispose()
    {
        _tenant.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }

    private static List<string> Ids(JsonNode? evidences) =>
        [.. evidences!.AsArray().Select(e => e is JsonObject o ? o["id"]!.GetValue<string>() : e!.GetValue<string>())];

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
            targets:
              - mapping: {{Path.Combine(files, "mappings", "essential-eight-2023.yaml")}}
                compliance_assessment: EX-E8-2026
              - mapping: {{Path.Combine(files, "mappings", "disp.yaml")}}
                compliance_assessment: EX-DISP-2026
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
