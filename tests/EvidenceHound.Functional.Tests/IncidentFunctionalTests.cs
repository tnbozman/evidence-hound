using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Cli;

namespace EvidenceHound.Functional.Tests;

/// <summary>
/// s15-07: a fictional incident on a system that handles CUI is recorded in CISO Assistant, with its detection on the timeline;
/// <c>hound incident obligations</c> lists the DFARS report due 72 hours after discovery. Each step's output lands in
/// <c>test-results/incident/</c>.
/// </summary>
[Trait("Category", "Functional")]
[Collection(SharedCisoAssistant.Name)]
public sealed class IncidentFunctionalTests(CisoAssistantFixture ciso) : IDisposable
{
    private static readonly DateTimeOffset Detected = new(2026, 11, 20, 9, 30, 0, TimeSpan.Zero);

    private readonly string _repo = Directory.CreateTempSubdirectory("hound-incident-").FullName;
    private readonly string _results = Directory.CreateDirectory(Path.Combine(CisoAssistantFixture.RepoRoot, "test-results", "incident")).FullName;

    private string Scope => Path.Combine(_repo, "scope");

    [Fact]
    public async Task A_cui_incident_owes_the_dfars_report_72_hours_after_its_detection()
    {
        var ct = TestContext.Current.CancellationToken;
        CopyExampleRepo();
        var setup = Path.Combine(Scope, "setup.yaml");
        await File.WriteAllTextAsync(setup, (await File.ReadAllTextAsync(setup, ct)).Replace(
            "        parents: [Example 365]\n",
            "        parents: [Example 365]\n        data:\n          - kind: cui\n            detail: CUI//SP-CTI\n            role: processes\n",
            StringComparison.Ordinal), ct);
        (await StepAsync("1-apply", ["scope", "apply", Scope], ct)).Exit.ShouldBe(ExitCodes.Ok);

        var laptops = (await SendAsync(HttpMethod.Get, "assets/?limit=1000", null, ct))["results"]!.AsArray().First(a => a!["name"]?.GetValue<string>() == "Example laptops")!;
        var incident = await SendAsync(HttpMethod.Post, "incidents/", new JsonObject
        {
            ["ref_id"] = "INC-FUNC-01",
            ["name"] = "Malware on a laptop (functional test)",
            ["folder"] = laptops["folder"]!["id"]!.GetValue<string>(),
            ["assets"] = new JsonArray(laptops["id"]!.GetValue<string>()),
        }, ct);
        await SendAsync(HttpMethod.Post, "timeline-entries/", new JsonObject
        {
            ["incident"] = incident["id"]!.GetValue<string>(),
            ["entry"] = "The EDR flagged malware on a laptop.",
            ["entry_type"] = "detection",
            ["timestamp"] = Detected.ToString("O", CultureInfo.InvariantCulture),
        }, ct);

        var listed = await StepAsync("2-obligations", ["incident", "obligations", "INC-FUNC-01", "--scope", Scope, "--config", Path.Combine(_repo, "hound.yaml"), "--format", "json"], ct);

        listed.Exit.ShouldBe(ExitCodes.Ok, listed.Output + listed.Error);
        var result = JsonNode.Parse(listed.Output)!;
        DateTimeOffset.Parse(result["discovered"]!.GetValue<string>(), CultureInfo.InvariantCulture).ShouldBe(Detected);
        var dfars = result["reports"]!.AsArray().Single(r => r!["id"]!.GetValue<string>() == "dfars-7012")!;
        DateTimeOffset.Parse(dfars["due"]!.GetValue<string>(), CultureInfo.InvariantCulture).ShouldBe(Detected.AddHours(72));
        dfars["why"]!.GetValue<string>().ShouldContain("Example laptops handles cui");
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private void CopyExampleRepo()
    {
        var example = Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "compliance-repo");
        foreach (var file in Directory.GetFiles(example, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(_repo, Path.GetRelativePath(example, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
    }

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

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(method, new Uri(ciso.ApiUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", ciso.Token);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"{method} {path}: {(int)response.StatusCode} {text}");
        return JsonNode.Parse(text)!;
    }
}
