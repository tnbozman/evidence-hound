using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s06-10: an audit with a quarterly cycle, run across the Q1-2027FY to Q2-2027FY boundary with a fixed clock (<c>HOUND_TODAY</c>). The old period
/// goes to in review with its final results, the new one opens from it as baseline, the next publish writes only to the new one, and a sign-off
/// in the audit file locks the old one on the next <c>scope apply</c>. Each step's output lands in <c>test-results/audit-periods/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class AuditPeriodsFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private static readonly string Series = """

        ref_id: EX-PERIOD
        name: Example baseline review
        framework: urn:example:risk:framework:example-baseline
        domain: Example Pty Ltd
        perimeter: Corporate IT
        systems: [Example 365]
        cycle: {period: quarter, starts: "2026-07-01"}
        unlisted:
          result: not_assessed
        """;

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-periods-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "audit-periods")).FullName;

    private string Scope => Path.Combine(_repo, "scope");

    private string Config => Path.Combine(_repo, "hound.yaml");

    [Fact]
    public async Task A_quarter_boundary_closes_the_old_period_opens_the_new_one_and_a_sign_off_locks_the_old_one()
    {
        var ct = TestContext.Current.CancellationToken;
        await WriteRepoAsync(ct);

        await StepAsync("1-apply-2026-09-30", ["scope", "apply", Scope], "2026-09-30", ct);
        await StepAsync("2-publish-2026-09-30", Publish, "2026-09-30", ct);
        var q3Rows = await RowsAsync("EX-PERIOD-Q1-2027FY", ct);
        q3Rows["EX-1.1"].Result.ShouldBe("non_compliant");

        var plan = await StepAsync("3-plan-2026-10-01", ["scope", "plan", Scope], "2026-10-01", ct);
        plan.ShouldContain("| EX-PERIOD-Q1-2027FY | closes: in review, awaiting sign-off |");
        plan.ShouldContain("| EX-PERIOD-Q2-2027FY | opens, from EX-PERIOD-Q1-2027FY |");
        await StepAsync("4-apply-2026-10-01", ["scope", "apply", Scope], "2026-10-01", ct);
        (await AuditAsync("EX-PERIOD-Q1-2027FY", ct)).ShouldBe(("in_review", false));
        (await AuditAsync("EX-PERIOD-Q2-2027FY", ct)).ShouldBe(("in_progress", false));
        (await RowsAsync("EX-PERIOD-Q2-2027FY", ct))["EX-1.1"].Result.ShouldBe("non_compliant", "the new period carries the old one's results over");

        var published = JsonNode.Parse(await StepAsync("5-publish-2026-10-01", Publish, "2026-10-01", ct))!;
        published["audits"]!.AsArray().Select(a => a!["compliance_assessment"]!.GetValue<string>()).ShouldBe(["EX-PERIOD-Q2-2027FY"]);
        (await RowsAsync("EX-PERIOD-Q1-2027FY", ct)).ShouldBe(q3Rows, "the closed period is not written again");

        await File.AppendAllTextAsync(
            Path.Combine(Scope, "audits", "ex-period.yaml"), "sign_offs:\n  - {period: Q1-2027FY, by: ceo@example.com, date: \"2026-10-14\"}\n", ct);
        await StepAsync("6-apply-2026-10-15", ["scope", "apply", Scope], "2026-10-15", ct);
        (await AuditAsync("EX-PERIOD-Q1-2027FY", ct)).ShouldBe(("done", true));
        (await AuditAsync("EX-PERIOD-Q2-2027FY", ct)).ShouldBe(("in_progress", false));
        (await RowsAsync("EX-PERIOD-Q1-2027FY", ct)).ShouldBe(q3Rows);
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private string[] Publish
    {
        get
        {
            var files = Path.Combine(AppContext.BaseDirectory, "files");
            return
            [
                "publish", "--config", Config,
                "--checks", Path.Combine(_repo, "checks.json"),
                "--evidence-dir", Path.Combine(files, "fixtures", "publish", "evidence"),
            ];
        }
    }

    /// <summary>A copy of the example compliance repo with one more audit, a quarterly series, and a deployment config that targets it.</summary>
    private async Task WriteRepoAsync(CancellationToken ct)
    {
        var example = Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "compliance-repo");
        foreach (var file in Directory.GetFiles(example, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(_repo, Path.GetRelativePath(example, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        await File.WriteAllTextAsync(Path.Combine(Scope, "audits", "ex-period.yaml"), Series.TrimStart() + "\n", ct);
        var files = Path.Combine(AppContext.BaseDirectory, "files");
        var config = (await File.ReadAllTextAsync(Path.Combine(files, "examples", "example-pty-ltd.yaml"), ct))
            .Replace("../mappings/example.yaml", Path.Combine(files, "mappings", "example.yaml"), StringComparison.Ordinal)
            .Replace("../.dev/evidence", Path.Combine(_repo, "evidence"), StringComparison.Ordinal)
            .Replace("compliance_assessment: EXAMPLE-BASELINE-2026", "compliance_assessment: EX-PERIOD", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Config, "scope: scope\n" + config, ct);

        // Its own evidence ids, so the evidence other functional tests publish first is not already in CISO Assistant.
        var checks = await File.ReadAllTextAsync(Path.Combine(files, "fixtures", "publish", "checks.json"), ct);
        await File.WriteAllTextAsync(Path.Combine(_repo, "checks.json"), checks.Replace("\"ev-", "\"ev-period-", StringComparison.Ordinal), ct);
    }

    /// <summary>Runs one hound command on <paramref name="today"/>, records its output, and returns it; it must exit 0.</summary>
    private async Task<string> StepAsync(string name, string[] args, string today, CancellationToken ct)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await HoundCli.RunAsync(
            args,
            output,
            error,
            configureLogging: null,
            new Dictionary<string, string?> { ["HOUND_CA_URL"] = ciso.ApiUrl.ToString(), ["HOUND_CA_TOKEN"] = ciso.Token, ["HOUND_TODAY"] = today },
            ct);
        var text = output.ToString();
        await File.WriteAllTextAsync(Path.Combine(_results, $"{name}.txt"), $"$ hound {string.Join(' ', args.Select(a => Path.GetFileName(a)))}  (HOUND_TODAY={today})\n{text}{error}", ct);
        exitCode.ShouldBe(ExitCodes.Ok, text + error);
        return text;
    }

    private async Task<(string Status, bool Locked)> AuditAsync(string refId, CancellationToken ct)
    {
        var audit = (await GetAsync($"compliance-assessments/?ref_id={refId}", ct))["results"]!.AsArray().ShouldHaveSingleItem()!;
        return (audit["status"]!.GetValue<string>(), audit["is_locked"]?.GetValue<bool>() ?? false);
    }

    /// <summary>The audit's rows for the requirements the example mapping computes, by requirement ref_id.</summary>
    private async Task<Dictionary<string, Row>> RowsAsync(string audit, CancellationToken ct)
    {
        var rows = new Dictionary<string, Row>(StringComparer.Ordinal);
        foreach (var requirement in new[] { "EX-1.1", "EX-1.2", "EX-2.1", "EX-3.1" })
        {
            var row = (await GetAsync($"requirement-assessments/?compliance_assessment__ref_id={audit}&requirement__ref_id={requirement}", ct))["results"]!.AsArray().ShouldHaveSingleItem()!;
            rows[requirement] = new Row(row["result"]!.GetValue<string>(), row["observation"]?.GetValue<string>() ?? "", row["updated_at"]?.ToString() ?? "");
        }

        return rows;
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

    private sealed record Row(string Result, string Observation, string UpdatedAt);
}
