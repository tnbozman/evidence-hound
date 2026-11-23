using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;
using Microsoft.Extensions.DependencyInjection;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// <c>hound run</c> with the rest collector and the sophos-central library profile, against the recorded example.com tenant, updates the
/// example audit. The recording's data region is answered as a real-looking Sophos host, which the run's HTTP client sends to the
/// recorded region server, so the profile's allowed hosts are used as written.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class SophosRunFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-sophos-functional-").FullName;

    private static readonly string RegionHost = "api-eu01.central.sophos.com";

    private readonly RecordedSophos _sophos = RecordedSophos.Start();

    [Fact]
    public async Task Run_with_sophos_central_profile_updates_example_audit()
    {
        var ct = TestContext.Current.CancellationToken;

        var (exitCode, summary) = await RunAsync(ct);

        exitCode.ShouldBe(ExitCodes.Ok, summary.ToJsonString());
        summary["collectors"]![0]!["publish"]!["failed"]!.GetValue<int>().ShouldBe(0);
        var requirement = (await GetAsync(
            "requirement-assessments/?compliance_assessment__ref_id=EXAMPLE-BASELINE-2026&requirement__ref_id=EX-4.1", ct))["results"]![0]!;
        requirement["result"]!.GetValue<string>().ShouldBe("compliant");
        requirement["observation"]!.GetValue<string>().ShouldContain("firewalls run supported firmware");
        var evidence = (await GetAsync("evidences/?search=example-sophos", ct))["results"]!.AsArray()
            .Select(e => e!["ref_id"]!.GetValue<string>());
        evidence.ShouldContain("example-sophos/firewalls.json");
    }

    public void Dispose()
    {
        _sophos.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }

    private async Task<(int ExitCode, JsonNode Summary)> RunAsync(CancellationToken ct)
    {
        var files = Path.Combine(AppContext.BaseDirectory, "files");
        _sophos.Whoami(new JsonObject
        {
            ["id"] = RecordedSophos.TenantId,
            ["idType"] = "tenant",
            ["apiHosts"] = new JsonObject { ["global"] = _sophos.ApiUrl.ToString(), ["dataRegion"] = $"https://{RegionHost}" },
        });
        var secret = Path.Combine(_workDir, "sophos_client_secret");
        await File.WriteAllTextAsync(secret, RecordedSophos.ClientSecret, ct);
        var config = Path.Combine(_workDir, "hound.yaml");
        await File.WriteAllTextAsync(config, $$"""
            ciso_assistant:
              url: http://localhost:8000/api/
              token: env:HOUND_CA_TOKEN
            evidence:
              root: {{Path.Combine(_workDir, "evidence")}}
            collectors:
              - name: example-sophos
                type: rest
                schedule: "0 4 * * *"
                settings:
                  profile: sophos-central
                  profiles_path: {{Path.Combine(files, "profiles")}}
                  client_id: {{RecordedSophos.ClientId}}
                  id_url: {{_sophos.IdUrl}}
                  api_url: {{_sophos.ApiUrl}}
                  supported_firewall_firmware: "{{RecordedSophos.SupportedFirmware}}"
                secrets:
                  client_secret: file:{{secret}}
            target:
              mapping: {{Path.Combine(files, "fixtures", "sophos-mapping.yaml")}}
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
            services => services.AddHttpClient("rest").ConfigurePrimaryHttpMessageHandler(() => new RegionRewrite(_sophos.RegionUrl)),
            ct);
        var text = output.ToString();
        return (exitCode, string.IsNullOrWhiteSpace(text) ? new JsonObject { ["stderr"] = error.ToString() } : JsonNode.Parse(text)!);
    }

    /// <summary>Sends requests for the recorded data region's host to the region server; everything else goes where it was addressed.</summary>
    private sealed class RegionRewrite(Uri region) : DelegatingHandler(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is { Host: var host } url && host == RegionHost)
            {
                request.RequestUri = new Uri(region, url.PathAndQuery);
            }

            return base.SendAsync(request, cancellationToken);
        }
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
