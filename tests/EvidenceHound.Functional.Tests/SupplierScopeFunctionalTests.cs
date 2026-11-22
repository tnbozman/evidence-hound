using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s09-19: two fictional suppliers applied to the local CISO Assistant from a copy of the example compliance repo. Re-applying writes
/// nothing, and a renewed contract's end date is a one-field update. Outputs land in <c>test-results/suppliers/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class SupplierScopeFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private const string Cloud = """
        ref_id: SUP-FT-CLOUD
        name: Example Cloud Pty Ltd
        description: Hosts the customer portal.
        country: AU
        currency: AUD
        domain: Example Pty Ltd
        dependency: 3
        penetration: 2
        maturity: 3
        trust: 3
        identifiers:
          LEI: EXAMPLE0000000000000
        solutions:
          - ref_id: SOL-FT-HOST
            name: Managed hosting
            criticality: 3
        contracts:
          - ref_id: CTR-FT-HOST
            name: Hosting agreement
            status: active
            start: "2026-01-01"
            end: "2027-12-31"
            annual_expense: 12000
            currency: AUD
            solutions: [SOL-FT-HOST]
        """;

    private const string Backup = """
        ref_id: SUP-FT-BACKUP
        name: Example Backup Ltd
        country: NZ
        currency: NZD
        dependency: 2
        penetration: 1
        maturity: 2
        trust: 2
        solutions:
          - ref_id: SOL-FT-OFFSITE
            name: Offsite backup
            criticality: 2
        contracts:
          - ref_id: CTR-FT-OFFSITE
            name: Backup service
            status: draft
            solutions: [SOL-FT-OFFSITE]
        """;

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-suppliers-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "suppliers")).FullName;

    [Fact]
    public async Task Two_suppliers_apply_reapply_writes_nothing_and_a_new_end_date_is_one_update()
    {
        var ct = TestContext.Current.CancellationToken;
        var example = Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "compliance-repo");
        foreach (var file in Directory.GetFiles(example, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(_repo, Path.GetRelativePath(example, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        var thirdParties = Directory.CreateDirectory(Path.Combine(_repo, "scope", "third-parties")).FullName;
        await File.WriteAllTextAsync(Path.Combine(thirdParties, "example-cloud.yaml"), Cloud, ct);
        await File.WriteAllTextAsync(Path.Combine(thirdParties, "example-backup.yaml"), Backup, ct);
        var config = Path.Combine(_repo, "hound.yaml");
        var scope = Path.Combine(_repo, "scope");

        var (applyExit, applied) = await StepAsync("1-apply", ["scope", "apply", scope, "--config", config], ct);
        applyExit.ShouldBe(ExitCodes.Ok, applied);
        JsonNode.Parse(applied)!["failed"]!.GetValue<int>().ShouldBe(0, applied);

        var (_, again) = await StepAsync("2-plan-after-apply", ["scope", "plan", scope, "--config", config], ct);
        again.ShouldContain("0 to create, 0 to update, 0 to delete.");

        await File.WriteAllTextAsync(Path.Combine(thirdParties, "example-cloud.yaml"), Cloud.Replace("end: \"2027-12-31\"", "end: \"2028-06-30\"", StringComparison.Ordinal), ct);
        var (planExit, plan) = await StepAsync("3-plan-renewed", ["scope", "plan", scope, "--config", config], ct);
        planExit.ShouldBe(ExitCodes.Ok, plan);
        plan.ShouldContain("0 to create, 1 to update, 0 to delete.");
        plan.ShouldContain("| Update | contract | CTR-FT-HOST | end_date: 2027-12-31 → 2028-06-30 |");

        var (renewExit, renewed) = await StepAsync("4-apply-renewed", ["scope", "apply", scope, "--config", config], ct);
        renewExit.ShouldBe(ExitCodes.Ok, renewed);
        JsonNode.Parse(renewed)!["written"]!.GetValue<int>().ShouldBe(1, renewed);
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private async Task<(int ExitCode, string Output)> StepAsync(string name, string[] args, CancellationToken ct)
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
        await File.WriteAllTextAsync(Path.Combine(_results, $"{name}.txt"), $"$ hound {string.Join(' ', args.Select(Path.GetFileName))}\n{output}{error}", ct);
        return (exitCode, exitCode == ExitCodes.Ok ? output.ToString() : output.ToString() + error.ToString());
    }
}
