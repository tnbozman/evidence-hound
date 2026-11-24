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
              purpose: ssp/purpose.md
              environment: ssp/environment.md
              management: ssp/management.md
              laws_and_policies: ssp/laws-and-policies.md
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

    [Fact]
    public async Task Strict_lists_the_example_gaps_and_the_poam_lists_what_is_not_met()
    {
        // s11-03: ISM-1486 is compliant by hand with no control linked, so nobody is responsible for it: a gap until a control owns it.
        var ct = TestContext.Current.CancellationToken;
        var scope = CopyOfExampleScope();
        await File.WriteAllTextAsync(Path.Combine(scope, "ssp.yaml"), Ssp, ct);
        var output = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "ssp")).FullName;

        var (exit, markdown) = await RunAsync(["ssp", "build", "--programme", "essential-eight", "--scope", scope, "--strict"], ct);
        await File.WriteAllTextAsync(Path.Combine(output, "ssp-essential-eight-strict.md"), markdown, ct);
        var (poamExit, poam) = await RunAsync(["ssp", "poam", "--programme", "essential-eight", "--scope", scope], ct);
        await File.WriteAllTextAsync(Path.Combine(output, "poam-essential-eight.md"), poam, ct);

        exit.ShouldBe(ExitCodes.SspGaps, markdown);
        markdown.ShouldContain("- ISM-1486: no responsible party", Case.Sensitive, markdown);
        poamExit.ShouldBe(ExitCodes.Ok, poam);
        poam.ShouldContain("| Requirement | Status | Control | Owner | ETA | Control status |", Case.Sensitive, poam);
        poam.ShouldContain("| Not implemented |", Case.Sensitive, poam);
    }

    [Fact]
    public async Task Renders_the_sp_800_18_plan_with_the_ism_annex_by_strategy()
    {
        // s11-04: one layout, NIST SP 800-18 Rev 1 Appendix A; the annex lists each ISM control once with the strategies it serves.
        var ct = TestContext.Current.CancellationToken;
        var scope = CopyOfExampleScope();
        await File.WriteAllTextAsync(Path.Combine(scope, "ssp.yaml"), Ssp + "\n    classification: OFFICIAL\n", ct);
        var sections = Directory.CreateDirectory(Path.Combine(Directory.GetParent(scope)!.FullName, "ssp")).FullName;
        await File.WriteAllTextAsync(Path.Combine(sections, "purpose.md"), "Example corporate IT runs Example Pty Ltd's mail, files and laptops.\n", ct);
        var output = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "ssp")).FullName;

        var (exit, markdown) = await RunAsync(["ssp", "build", "--programme", "essential-eight", "--scope", scope], ct);
        await File.WriteAllTextAsync(Path.Combine(output, "ssp-essential-eight.md"), markdown, ct);

        exit.ShouldBe(ExitCodes.Ok, markdown);
        markdown.ShouldContain("## 9. General description and purpose\n\nExample corporate IT runs Example Pty Ltd's mail, files and laptops.", Case.Sensitive, markdown);
        markdown.ShouldContain("- Classification: OFFICIAL", Case.Sensitive, markdown);
        markdown.ShouldContain("## 13. Security controls (annex)", Case.Sensitive, markdown);
        markdown.Split('\n').Count(l => l.StartsWith("| ISM-1815 |", StringComparison.Ordinal)).ShouldBe(1, markdown);
        markdown.ShouldContain("| mfa, rap, ac, uah |", Case.Sensitive, markdown);
        markdown.ShouldContain("## Appendix: plan of action and milestones", Case.Sensitive, markdown);

        // s11-16: the ASD annex template's columns; ISM-1504's reference control in CISO Assistant's library is tech.mfa.
        markdown.ShouldContain("| Control | Description | Strategies | Maturity levels | Control type | Responsible entity | Implementation status |", Case.Sensitive, markdown);
        var mfa = markdown.Split('\n').Single(l => l.StartsWith("| ISM-1504 |", StringComparison.Ordinal));
        mfa.ShouldContain("| Technical |", Case.Sensitive, mfa);
        mfa.ShouldMatch(@"\| (Consumer|Shared|Outsourced service provider|Cloud service provider|Not applicable) \| (Effective|Ineffective|Not implemented|Not applicable|Not assessed) \|", mfa);
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
