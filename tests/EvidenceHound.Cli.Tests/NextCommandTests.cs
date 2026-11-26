using System.Security.Cryptography;
using System.Text.Json.Nodes;
using EvidenceHound.Runtime;
using EvidenceHound.Runtime.Status;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Cli.Tests;

/// <summary>s18-01: <c>hound next</c> tells the agent what needs doing, most urgent first, with the command for each, and writes nothing.</summary>
public sealed class NextCommandTests : IDisposable
{
    private static readonly DateTimeOffset Ran = new(2027, 2, 14, 16, 0, 0, TimeSpan.Zero);
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly WireMockServer _ca = WireMockServer.Start();
    private readonly string _dir = Directory.CreateTempSubdirectory("hound-next-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private string Repo => Path.Combine(_dir, "repo");

    private string Config => Path.Combine(Repo, "hound.yaml");

    public NextCommandTests()
    {
        TestFiles.CopyExampleRepo(Repo);
        File.WriteAllText(Config, File.ReadAllText(Config).Replace("/var/lib/evidence-hound", Path.Combine(_dir, "var"), StringComparison.Ordinal));
        var upload = Directory.CreateDirectory(Path.Combine(_dir, "var", "manual", "example-incident-response-plan")).FullName;
        File.WriteAllText(Path.Combine(upload, "irp.pdf"), "plan");
        File.SetLastWriteTimeUtc(Path.Combine(upload, "irp.pdf"), Ran.UtcDateTime.AddDays(-30));
        var store = new FileRunRecordStore(Path.Combine(_dir, "var", "evidence"));
        store.SaveAsync(
            new RunRecord("example-m365", Ran, Ran.AddSeconds(3), CollectorOutcome.Failed, 0, 0, 2, [new("collector.all_checks_errored", "All 2 checks errored.")], null),
            _ct).GetAwaiter().GetResult();
        _ca.Given(Request.Create().WithPath("/api/folders/").UsingGet())
            .RespondWith(Response.Create().WithBody(new JsonObject { ["count"] = 0, ["results"] = new JsonArray() }.ToJsonString()));
    }

    [Fact]
    public async Task Lists_work_most_urgent_first_with_the_command_for_each()
    {
        var exitCode = await Run(online: false, "next", "--repo", Repo, "--config", Config, "--json");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var json = JsonNode.Parse(_output.ToString())!;
        var items = json["items"]!.AsArray().Select(i => i!).ToList();
        var urgency = items.Select(i => i["urgency"]!.GetValue<string>()).ToList();
        urgency.ShouldBe([.. urgency.OrderBy(u => Array.IndexOf(Urgencies, u))], "most urgent first");
        items.ShouldContain(i => i["kind"]!.GetValue<string>() == "collector" && i["subject"]!.GetValue<string>() == "example-m365"
            && i["urgency"]!.GetValue<string>() == "broken" && i["command"]!.GetValue<string>() == "hound run --config hound.yaml --only example-m365");
        items.ShouldContain(i => i["kind"]!.GetValue<string>() == "manual_source" && i["subject"]!.GetValue<string>() == "example-incident-register"
            && i["urgency"]!.GetValue<string>() == "overdue");
        items.ShouldNotContain(i => i["kind"]!.GetValue<string>() == "manual_source" && i["subject"]!.GetValue<string>() == "example-incident-response-plan", "uploaded 30 days ago, due every 366");
        items.ShouldContain(i => i["kind"]!.GetValue<string>() == "exception" && i["subject"]!.GetValue<string>() == "EXC-01"
            && i["urgency"]!.GetValue<string>() == "due_soon");
        items.ShouldAllBe(i => i["command"]!.GetValue<string>().StartsWith("hound ", StringComparison.Ordinal) && i["minutes"]!.GetValue<int>() > 0);
        json["minutes"]!.GetValue<int>().ShouldBe(items.Sum(i => i["minutes"]!.GetValue<int>()));
    }

    [Fact]
    public async Task Writes_nothing()
    {
        var before = Tree(_dir);

        var exitCode = await Run(online: true, "next", "--repo", Repo, "--config", Config, "--json");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        Tree(_dir).ShouldBe(before);
        _ca.LogEntries.ShouldNotBeEmpty();
        _ca.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
        var connection = JsonNode.Parse(_output.ToString())!["connection"]!;
        connection["reachable"]!.GetValue<bool>().ShouldBeTrue();
        connection["token"]!.GetValue<string>().ShouldBe("accepted");
        _output.ToString().ShouldNotContain("example-token");
    }

    [Fact]
    public async Task Offline_lists_what_it_can_and_says_the_connection_is_unknown()
    {
        var exitCode = await Run(online: false, "next", "--repo", Repo, "--config", Config, "--json");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var json = JsonNode.Parse(_output.ToString())!;
        json["connection"]!["reachable"].ShouldBeNull();
        json["connection"]!["note"]!.GetValue<string>().ShouldContain("HOUND_CA_TOKEN");
        json["items"]!.AsArray().ShouldNotBeEmpty();
        json["items"]!.AsArray().ShouldNotContain(i => i!["kind"]!.GetValue<string>() == "scope");
    }

    [Fact]
    public async Task A_rejected_token_is_broken_work_for_the_person()
    {
        _ca.Reset();
        _ca.Given(Request.Create().WithPath("/api/folders/").UsingGet()).RespondWith(Response.Create().WithStatusCode(401).WithBody("""{"detail":"Invalid token."}"""));

        var exitCode = await Run(online: true, "next", "--repo", Repo, "--config", Config);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var text = _output.ToString();
        text.ShouldContain("CISO Assistant rejected the token");
        text.ShouldContain("hound config validate hound.yaml --check-secrets");
    }

    private static readonly string[] Urgencies = ["overdue", "broken", "due_soon", "waiting"];

    private static Dictionary<string, string> Tree(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(f => Path.GetRelativePath(root, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))) + File.GetLastWriteTimeUtc(f).Ticks, StringComparer.Ordinal);

    private Task<int> Run(bool online, params string[] args) => HoundCli.RunAsync(
        args,
        _output,
        _error,
        configureLogging: null,
        new Dictionary<string, string?>
        {
            ["HOUND_TODAY"] = "2027-02-15",
            ["HOUND_CA_URL"] = online ? $"{_ca.Url}/api/" : null,
            ["HOUND_CA_TOKEN"] = online ? "example-token" : null,
        },
        _ct);

    public void Dispose()
    {
        _ca.Stop();
        _ca.Dispose();
        Directory.Delete(_dir, recursive: true);
    }
}
