using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s11-02: <c>hound ssp build</c> against the seeded CISO Assistant assembles the Essential Eight plan from the example's systems, controls
/// and audit rows, each value with its source. The plan lands in <c>test-results/ssp/</c>, which CI prints.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class SspFunctionalTests(CisoAssistantFixture ciso)
{
    private static readonly string Ssp = """
        ssps:
          - programme: essential-eight
            profile: ism
            system_name: Example corporate IT
            perimeter: Corporate IT
            version: "0.1"
            roles:
              system_owner: it@example.com
              authorising_official: ceo@example.com
              security_contact: IT team
            documents: [identity-policy]
            sections:
              environment: ssp/environment.md
              physical: ssp/physical.md
              personnel: ssp/personnel.md
        """;

    [Fact]
    public async Task Builds_the_essential_eight_plan_from_the_seeded_records()
    {
        var ct = TestContext.Current.CancellationToken;
        var scope = CopyOfExampleScope();
        await File.WriteAllTextAsync(Path.Combine(scope, "ssp.yaml"), Ssp, ct);
        var output = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "ssp")).FullName;

        var (exit, json) = await RunAsync(["ssp", "build", "--programme", "essential-eight", "--scope", scope, "--json"], ct);
        await File.WriteAllTextAsync(Path.Combine(output, "ssp-essential-eight.json"), json, ct);

        exit.ShouldBe(ExitCodes.Ok, json);
        var plan = JsonNode.Parse(json)!;
        var inventory = plan["inventory"]!.AsArray().ToDictionary(s => s!["name"]!["value"]!.GetValue<string>());
        inventory.Keys.ShouldBe(["Example 365", "Example laptops", "Example backup service"], ignoreOrder: true, json);
        inventory["Example 365"]!["owner"]!["value"]!.GetValue<string>().ShouldBe("it@example.com", json);
        inventory["Example 365"]!["owner"]!["source"]!["kind"]!.GetValue<string>().ShouldBe("ciso_assistant", json);
        plan["controls"]!.AsArray().Select(c => c!["ref_id"]!["value"]!.GetValue<string>()).ShouldContain("AC-MFA", json);
        var row = plan["requirements"]!.AsArray().Single(r => r!["ref"]!["value"]!.GetValue<string>() == "ISM-1486")!;
        row["result"]!["value"]!.GetValue<string>().ShouldBe("compliant", json);
        plan["documents"]!.AsArray().ShouldHaveSingleItem()!["value"]!.GetValue<string>().ShouldBe("Identity and access policy", json);
    }

    private static string CopyOfExampleScope()
    {
        var source = Directory.GetParent(CisoAssistantFixture.ExampleScope)!.FullName;
        var target = Path.Combine(Path.GetTempPath(), "hound-ssp-" + Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        return Path.Combine(target, Path.GetFileName(CisoAssistantFixture.ExampleScope));
    }

    private async Task<(int ExitCode, string Output)> RunAsync(string[] args, CancellationToken ct)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            args,
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            ct);
        return (exitCode, exitCode == ExitCodes.Ok ? output.ToString() : output.ToString() + error.ToString());
    }
}
