using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s12-08: <c>hound mapping crosswalk</c> against the seeded CISO Assistant: the 800-171 gap list carries today's Essential Eight results
/// from the example audit, and nothing is written. The Markdown lands in <c>test-results/reports/</c>, which CI keeps.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class CrosswalkFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-crosswalk-functional-").FullName;

    [Fact]
    public async Task The_800_171_gap_list_carries_todays_essential_eight_results()
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

        var json = JsonNode.Parse(await CrosswalkAsync(config, ct, "--json"))!;
        var markdown = await CrosswalkAsync(config, ct);

        json["audit"]!.GetValue<string>().ShouldBe("EX-E8-2026");
        json["rows"]!.AsArray().Count.ShouldBe(110);
        json["rows"]!.AsArray().SelectMany(r => r!["sources"]!.AsArray()).ShouldContain(s => s!["result"] != null);
        var results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "reports")).FullName;
        await File.WriteAllTextAsync(Path.Combine(results, "crosswalk-essential-eight-to-800-171.md"), markdown, ct);
    }

    public void Dispose() => Directory.Delete(_workDir, recursive: true);

    private async Task<string> CrosswalkAsync(string config, CancellationToken ct, params string[] extra)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await HoundCli.RunAsync(
            ["mapping", "crosswalk", "--from", "essential-eight", "--config", config, "--scope", CisoAssistantFixture.ExampleScope, .. extra],
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            ct);
        exit.ShouldBe(ExitCodes.Ok, output.ToString() + error);
        return output.ToString();
    }
}
