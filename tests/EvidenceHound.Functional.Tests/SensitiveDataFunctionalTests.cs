using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s10-06: a fictional supplier whose solution stores CUI and personal information is applied to the local CISO Assistant. The kinds
/// are filtering labels on the solution, its authority is in the description, a re-plan lists no changes, and <c>hound supplier list</c>
/// prints the prime's list. Outputs land in <c>test-results/sensitive-data/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class SensitiveDataFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private static readonly string Supplier = """
        ref_id: SUP-FT-DATA
        name: Example Secure Files Pty Ltd
        country: AU
        currency: AUD
        domain: Example Pty Ltd
        solutions:
          - ref_id: SOL-FT-DATA
            name: Secure file storage
            description: Engineering drawings shared with the prime.
            data:
              - kind: cui
                detail: CUI//SP-CTI
                role: stores
                authority:
                  name: FedRAMP Moderate equivalent
                  evidence: documents/suppliers/example-secure-files-fedramp.pdf
                  expires: "2099-06-30"
              - kind: pii
                role: stores
        contracts:
          - ref_id: CTR-FT-DATA
            name: Secure file storage subscription
            status: draft
            solutions: [SOL-FT-DATA]
        """;

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-sensitive-data-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "sensitive-data")).FullName;

    [Fact]
    public async Task Data_kinds_are_labels_on_the_solution_a_replan_lists_no_changes_and_the_supplier_list_names_the_authority()
    {
        var ct = TestContext.Current.CancellationToken;
        TestRepo.CopyExample(_repo);

        var thirdParties = Directory.CreateDirectory(Path.Combine(_repo, "scope", "third-parties")).FullName;
        await File.WriteAllTextAsync(Path.Combine(thirdParties, "sup-ft-data.yaml"), Supplier, ct);
        var config = Path.Combine(_repo, "hound.yaml");
        var scope = Path.Combine(_repo, "scope");

        var (applyExit, applied) = await StepAsync("1-apply", ["scope", "apply", scope, "--config", config], ct);
        applyExit.ShouldBe(ExitCodes.Ok, applied);
        JsonNode.Parse(applied)!["failed"]!.GetValue<int>().ShouldBe(0, applied);

        var (_, again) = await StepAsync("2-plan-after-apply", ["scope", "plan", scope, "--config", config], ct);
        again.ShouldContain("0 to create, 0 to update, 0 to delete.");

        // In CISO Assistant: filter solutions by the data-cui label, and read the authority in the description.
        var labels = (await GetAsync("filtering-labels/?label=data-cui", ct))["results"]!.AsArray();
        var cui = labels.Single(l => l!["label"]!.GetValue<string>() == "data-cui")!["id"]!.GetValue<string>();
        var solutions = (await GetAsync($"solutions/?filtering_labels={cui}", ct))["results"]!.AsArray();
        var solution = solutions.Single(s => s!["ref_id"]!.GetValue<string>() == "SOL-FT-DATA")!;
        await File.WriteAllTextAsync(Path.Combine(_results, "3-solution.json"), solution.ToJsonString(), ct);
        solution["description"]!.GetValue<string>().ShouldContain("FedRAMP Moderate equivalent, evidence documents/suppliers/example-secure-files-fedramp.pdf, expires 2099-06-30");

        var (listExit, listed) = await StepAsync("4-supplier-list", ["supplier", "list", "--data", "cui", "--repo", _repo], ct);
        listExit.ShouldBe(ExitCodes.Ok, listed);
        listed.ShouldContain("| SUP-FT-DATA | SOL-FT-DATA | stores | CUI//SP-CTI | FedRAMP Moderate equivalent |");
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private async Task<JsonNode> GetAsync(string path, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ciso.ApiUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);
        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.IsSuccessStatusCode.ShouldBeTrue(text);
        return JsonNode.Parse(text)!;
    }

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
