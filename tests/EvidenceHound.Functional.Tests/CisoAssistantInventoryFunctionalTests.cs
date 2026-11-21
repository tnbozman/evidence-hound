using System.Diagnostics;
using System.Text.Json.Nodes;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s06-05: runs <c>scripts/ca-inventory.sh</c> against the seeded CISO Assistant. Its answers and the OpenAPI schema land in
/// <c>test-results/ca-inventory/</c>, which CI uploads, so the inventory document is checked against a real instance.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class CisoAssistantInventoryFunctionalTests(CisoAssistantFixture ciso)
{
    private static readonly string[] Questions =
        ["schema", "setup", "lock_freezes_controls", "audit_fields", "read_only_write", "evidence_revision_hash", "api_configuration", "ref_ids"];

    [Fact]
    public async Task Inventory_script_answers_every_question_and_saves_the_schema()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = RepoRoot();
        var output = Path.Combine(root, "test-results", "ca-inventory");
        var start = new ProcessStartInfo("bash", [Path.Combine(root, "scripts", "ca-inventory.sh"), ciso.ApiUrl.ToString(), output])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Environment = { ["HOUND_CA_TOKEN"] = ciso.Token },
        };

        using var script = Process.Start(start)!;
        var stdout = await script.StandardOutput.ReadToEndAsync(ct);
        var stderr = await script.StandardError.ReadToEndAsync(ct);
        await script.WaitForExitAsync(ct);

        script.ExitCode.ShouldBe(0, stderr);
        var answers = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(output, "answers.json"), ct))!.AsObject();
        answers.Select(a => a.Key).ShouldBe(Questions, ignoreOrder: true, stdout);
        answers["setup"]!.ToJsonString().ShouldBe("""{"perimeter":true,"folder":true,"framework":true}""", stdout);
        answers["schema"]!["paths"]!.GetValue<int>().ShouldBeGreaterThan(100, stdout);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("No global.json above the test folder.");
    }
}
