using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s15-01: a fictional 4x4 risk matrix and two threats in a repo library file are uploaded to CISO Assistant and appear in its catalogue;
/// planning again with no change writes nothing; a changed file at the same version is refused; a raised version with a third threat is
/// uploaded and the loaded library updated. Each step's output lands in <c>test-results/risk-library/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class RiskLibraryFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private static readonly string LibraryUrn = "urn:example:risk:library:example-versioned";

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-risk-library-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "risk-library")).FullName;

    private string Scope => Path.Combine(_repo, "scope");

    private string Library => Path.Combine(_repo, "libraries", "example-risk.yaml");

    [Fact]
    public async Task Custom_matrix_and_threats_are_uploaded_once_and_updated_on_a_version_bump()
    {
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(Scope);
        Directory.CreateDirectory(Path.GetDirectoryName(Library)!);
        // Under its own URN: raising its version must not leave the example library, which other tests load, behind CISO Assistant's copy.
        await File.WriteAllTextAsync(Library, await ExampleRiskLibrary.CopyAsync("versioned", ct), ct);
        await File.WriteAllTextAsync(Path.Combine(Scope, "setup.yaml"), """
            organisation:
              root_folder: Global
            libraries:
              - urn: urn:example:risk:library:example-versioned
                file: ../libraries/example-risk.yaml
            """, ct);

        (await StepAsync("1-apply", ["scope", "apply", Scope], ct)).Exit.ShouldBe(ExitCodes.Ok);
        (await GetAsync("risk-matrices/?limit=500", ct))["results"]!.AsArray().ShouldContain(m => m!["urn"]!.GetValue<string>() == "urn:example:risk:matrix:example-versioned-4x4");
        (await ThreatsAsync(ct)).ShouldBe(["VERSIONED-THR-01", "VERSIONED-THR-02"]);

        var replan = await StepAsync("2-plan-again", ["scope", "plan", Scope, "--format", "json"], ct);
        replan.Exit.ShouldBe(ExitCodes.Ok);
        JsonNode.Parse(replan.Output)!["changes"]!.AsArray().ShouldNotContain(c => c!["key"]!.GetValue<string>() == LibraryUrn);

        var threat = """
                - urn: urn:example:risk:threat:example-versioned:thr-03
                  ref_id: VERSIONED-THR-03
                  name: Shared administrator account used after a role change
                  description: An administrator who moved role still signs in with a shared account.

            """;
        await File.AppendAllTextAsync(Library, threat, ct);
        var refused = await StepAsync("3-plan-changed-same-version", ["scope", "plan", Scope], ct);
        refused.Exit.ShouldBe(ExitCodes.Configuration);
        refused.Error.ShouldContain("library.version_not_bumped");

        await File.WriteAllTextAsync(Library, (await File.ReadAllTextAsync(Library, ct)).Replace("\nversion: 1\n", "\nversion: 2\n", StringComparison.Ordinal), ct);
        (await StepAsync("4-apply-version-2", ["scope", "apply", Scope], ct)).Exit.ShouldBe(ExitCodes.Ok);
        (await ThreatsAsync(ct)).ShouldBe(["VERSIONED-THR-01", "VERSIONED-THR-02", "VERSIONED-THR-03"]);
        (await StepAsync("5-apply-again", ["scope", "apply", Scope], ct)).Output.ShouldContain("\"written\": 0");
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private async Task<string[]> ThreatsAsync(CancellationToken ct) =>
        [.. (await GetAsync("threats/?limit=1000", ct))["results"]!.AsArray()
            .Where(t => t!["urn"]?.GetValue<string>()?.StartsWith("urn:example:risk:threat:example-versioned:", StringComparison.Ordinal) == true)
            .Select(t => t!["ref_id"]!.GetValue<string>()).Order(StringComparer.Ordinal)];

    private async Task<(int Exit, string Output, string Error)> StepAsync(string name, string[] args, CancellationToken ct)
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
        return (exitCode, output.ToString(), error.ToString());
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
