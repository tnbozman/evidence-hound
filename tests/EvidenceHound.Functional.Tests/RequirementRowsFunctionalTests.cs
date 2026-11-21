using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>s06-01: an Essentials action repeated in two domains, named by URN, gets its own result in each domain's row.</summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class RequirementRowsFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private static readonly string Audit = "EX-ESSENTIALS-2026";
    private static readonly string Boundary = "urn:intuitem:risk:req_node:asd-essentials-enterprise-it:boundary-services:pro-01:it.pro-01.1";
    private static readonly string Identity = "urn:intuitem:risk:req_node:asd-essentials-enterprise-it:identity-services:pro-01:it.pro-01.1";

    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-rows-").FullName;

    public void Dispose() => Directory.Delete(_workDir, recursive: true);

    [Fact]
    public async Task Action_named_by_urn_in_two_domains_gets_a_result_per_domain()
    {
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(_workDir, "mapping.yaml"), $"""
            framework_urn: urn:intuitem:risk:framework:asd-essentials-enterprise-it
            requirements:
              - urn: {Boundary}
                rule: all_pass
                checks: [network.boundary.secure_design]
              - urn: {Identity}
                rule: all_pass
                checks: [identity.secure_design]
            """, ct);
        await File.WriteAllTextAsync(Path.Combine(_workDir, "config.yaml"), $"""
            ciso_assistant:
              url: {ciso.ApiUrl}
              token: env:HOUND_CA_TOKEN
            evidence:
              root: evidence
            collectors: []
            target:
              mapping: mapping.yaml
              compliance_assessment: {Audit}
              controls: {"{}"}
            """, ct);
        await File.WriteAllTextAsync(Path.Combine(_workDir, "checks.json"), """
            {"checks": [
              {"check_id": "network.boundary.secure_design", "outcome": "pass", "observation": "Example boundary design reviewed.", "evidence_refs": [], "collected_at": "2026-01-15T09:30:00Z", "source": "example"},
              {"check_id": "identity.secure_design", "outcome": "fail", "observation": "Example identity design not reviewed.", "evidence_refs": [], "collected_at": "2026-01-15T09:30:00Z", "source": "example"}
            ]}
            """, ct);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            ["publish", "--config", Path.Combine(_workDir, "config.yaml"), "--checks", Path.Combine(_workDir, "checks.json"), "--evidence-dir", _workDir],
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            ct);

        exitCode.ShouldBe(ExitCodes.Ok, output + error.ToString());
        (await ResultOf(Boundary, ct)).ShouldBe("compliant");
        (await ResultOf(Identity, ct)).ShouldBe("non_compliant");
    }

    private async Task<string> ResultOf(string urn, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(ciso.ApiUrl, $"requirement-assessments/?compliance_assessment__ref_id={Audit}&requirement__urn={Uri.EscapeDataString(urn)}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var rows = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!["results"]!.AsArray();
        return rows.ShouldHaveSingleItem()!["result"]!.GetValue<string>();
    }
}
