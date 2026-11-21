using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>s06-04: a repeat run sends nothing, the history shows every row verified unchanged, and a row edited in the UI is restored as drift.</summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class ChangeOnlyFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-change-only-").FullName;

    [Fact]
    public async Task Second_run_sends_nothing_and_a_ui_edit_is_restored_as_drift()
    {
        var ct = TestContext.Current.CancellationToken;
        var (firstExit, first) = await RunAsync(ct);
        firstExit.ShouldBe(ExitCodes.Ok, first.ToJsonString());
        var rows = Changes(first).Changed + Changes(first).Unchanged;
        rows.ShouldBeGreaterThan(0);

        var (secondExit, second) = await RunAsync(ct);

        secondExit.ShouldBe(ExitCodes.Ok, second.ToJsonString());
        (second["collectors"]![0]!["publish"]!["sent"]!.GetValue<int>(), second["collectors"]![0]!["publish"]!["skipped"]!.GetValue<int>()).ShouldBe((0, 0), second.ToJsonString());
        Changes(second).ShouldBe((0, rows, 0));
        var history = HistoryLines();
        history.Count.ShouldBe(2);
        (history[1]["changed"]!.GetValue<int>(), history[1]["unchanged"]!.GetValue<int>()).ShouldBe((0, rows));
        history[1]["run_id"]!.GetValue<string>().ShouldBe(second["run_id"]!.GetValue<string>());

        var row = await RequirementAsync("EX-1.1", ct);
        var computed = row["result"]!.GetValue<string>();
        var edited = computed == "compliant" ? "non_compliant" : "compliant";
        await PatchAsync($"requirement-assessments/{row["id"]!.GetValue<string>()}/", new JsonObject { ["result"] = edited }, ct);

        var (thirdExit, third) = await RunAsync(ct);

        thirdExit.ShouldBe(ExitCodes.Ok, third.ToJsonString());
        Changes(third).ShouldBe((1, rows - 1, 1));
        var drift = third["collectors"]![0]!["drift"]!.AsArray().ShouldHaveSingleItem()!;
        drift["code"]!.GetValue<string>().ShouldBe("publish.drift_corrected");
        drift["message"]!.GetValue<string>().ShouldContain($"was {edited}, now {computed}");
        var restored = await RequirementAsync("EX-1.1", ct);
        restored["result"]!.GetValue<string>().ShouldBe(computed);
        restored["observation"]!.GetValue<string>().ShouldContain($"[hound run {third["run_id"]!.GetValue<string>()} state ");
        HistoryLines()[2]["drift"]!.GetValue<int>().ShouldBe(1);
    }

    private static (int Changed, int Unchanged, int Drift) Changes(JsonNode summary)
    {
        var audit = summary["collectors"]![0]!["changes"]!.AsArray().ShouldHaveSingleItem()!;
        return (audit["changed"]!.GetValue<int>(), audit["unchanged"]!.GetValue<int>(), audit["drift"]!.GetValue<int>());
    }

    private List<JsonNode> HistoryLines() =>
        [.. File.ReadAllLines(Path.Combine(_workDir, "evidence", "_history", "EXAMPLE-BASELINE-2026.jsonl")).Select(line => JsonNode.Parse(line)!)];

    private async Task<(int ExitCode, JsonNode Summary)> RunAsync(CancellationToken ct)
    {
        var files = Path.Combine(AppContext.BaseDirectory, "files");
        var config = Path.Combine(_workDir, "hound.yaml");
        var example = await File.ReadAllTextAsync(Path.Combine(files, "examples", "example-pty-ltd.yaml"), ct);
        await File.WriteAllTextAsync(config, example
            .Replace("root: ../.dev/evidence", $"root: {Path.Combine(_workDir, "evidence")}", StringComparison.Ordinal)
            .Replace("mapping: ../mappings/example.yaml", $"mapping: {Path.Combine(files, "mappings", "example.yaml")}", StringComparison.Ordinal), ct);

        // Each run needs its own second: evidence folders and run IDs are named by the start time.
        await Task.Delay(TimeSpan.FromSeconds(1.1), ct);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            ["run", "--config", config],
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token },
            ct);
        var text = output.ToString();
        return (exitCode, string.IsNullOrWhiteSpace(text) ? new JsonObject { ["stderr"] = error.ToString() } : JsonNode.Parse(text)!);
    }

    private async Task<JsonNode> RequirementAsync(string requirement, CancellationToken ct) =>
        (await GetAsync($"requirement-assessments/?compliance_assessment__ref_id=EXAMPLE-BASELINE-2026&requirement__ref_id={requirement}", ct))["results"]![0]!;

    private async Task<JsonNode> GetAsync(string path, CancellationToken ct)
    {
        using var http = Http();
        using var response = await http.GetAsync(new Uri(ciso.ApiUrl, path), ct);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
    }

    private async Task PatchAsync(string path, JsonObject body, CancellationToken ct)
    {
        using var http = Http();
        using var response = await http.PatchAsJsonAsync(new Uri(ciso.ApiUrl, path), body, ct);
        response.EnsureSuccessStatusCode();
    }

    private HttpClient Http()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);
        return http;
    }

    public void Dispose() => Directory.Delete(_workDir, recursive: true);
}
