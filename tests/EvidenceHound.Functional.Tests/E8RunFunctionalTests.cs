using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;
using EvidenceHound.Collectors.M365;
using EvidenceHound.Collectors.M365.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s06-02: <c>hound run</c> with the recorded Microsoft 365 tenant and a manual incident register fills the example Essential Eight ML2
/// audit, and ISM-0123, listed under four strategies, gets its result in every strategy's row.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class E8RunFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private static readonly string Audit = "EX-E8-2026";

    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-e8-functional-").FullName;
    private readonly RecordedTenant _tenant = RecordedTenant.Start();

    [Fact]
    public async Task Run_fills_the_ml2_audit_and_every_row_of_a_repeated_control()
    {
        var ct = TestContext.Current.CancellationToken;

        var (exitCode, summary) = await RunAsync(ct);

        exitCode.ShouldBe(ExitCodes.Ok, summary.ToJsonString());
        summary["collectors"]!.AsArray().Select(c => c!["publish"]!["failed"]!.GetValue<int>()).ShouldAllBe(failed => failed == 0, summary.ToJsonString());
        var incidentRows = await RowsAsync("ISM-0123", ct);
        incidentRows.Count.ShouldBe(4);
        incidentRows.Select(r => r!["result"]!.GetValue<string>()).ShouldAllBe(result => result == "compliant");
        var mfa = (await RowsAsync("ISM-1504", ct)).ShouldHaveSingleItem()!;
        mfa["result"]!.GetValue<string>().ShouldNotBe("not_assessed");
        mfa["observation"]!.GetValue<string>().ShouldContain("identity.mfa.enforced_conditional_access");
    }

    public void Dispose()
    {
        _tenant.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }

    private async Task<(int ExitCode, JsonNode Summary)> RunAsync(CancellationToken ct)
    {
        var files = Path.Combine(AppContext.BaseDirectory, "files");
        var drop = Directory.CreateDirectory(Path.Combine(_workDir, "incident-register")).FullName;
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        await File.WriteAllTextAsync(Path.Combine(drop, "incident-register.csv"), "date,summary,reported_to_ciso,reported_to_asd\n", ct);
        await File.WriteAllTextAsync(Path.Combine(drop, "incident-register.csv.attest.yaml"), $"""
            who: Alex Example
            date: {today}
            note: No incidents this month; the register and reporting process were reviewed.
            outcomes:
              incident.reported_to_ciso: pass
              incident.reported_to_asd: pass
              incident.response_plan_followed: pass
            """, ct);
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
              - name: example-incident-register
                type: manual
                schedule: "0 6 * * *"
                settings:
                  drop_folder: {{drop}}
                  checks: incident.reported_to_ciso, incident.reported_to_asd, incident.response_plan_followed
                  due_days: 31
                  owner: Alex Example
            target:
              mapping: {{Path.Combine(files, "mappings", "essential-eight-2023.yaml")}}
              compliance_assessment: {{Audit}}
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

    private async Task<JsonArray> RowsAsync(string refId, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ciso.ApiUrl, $"requirement-assessments/?compliance_assessment__ref_id={Audit}&requirement__ref_id={refId}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!["results"]!.AsArray();
    }
}
