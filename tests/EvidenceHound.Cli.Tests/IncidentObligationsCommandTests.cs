using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Cli.Tests;

/// <summary>s15-07: <c>hound incident obligations</c> reads an incident from CISO Assistant and lists the reports it owes, due from discovery.</summary>
public sealed class IncidentObligationsCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly WireMockServer _ca = WireMockServer.Start();
    private readonly string _dir = Directory.CreateTempSubdirectory("hound-incident-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public IncidentObligationsCommandTests()
    {
        TestFiles.CopyExampleRepo(_dir);
        var setup = Path.Combine(_dir, "scope", "setup.yaml");
        File.WriteAllText(setup, File.ReadAllText(setup).Replace(
            "        parents: [Example 365]\n",
            "        parents: [Example 365]\n        data:\n          - kind: cui\n            detail: CUI//SP-CTI\n            role: processes\n",
            StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(_dir, "scope", "reporting.yaml"), """
            obligations:
              - id: dfars-7012
                name: DFARS 252.204-7012 cyber incident report
                report_to: DoD, through the DIBNet portal
                within: 72h
                when: {data: [cui]}
              - id: reportcyber
                name: Report to ASD's ACSC through ReportCyber
                report_to: ASD's ACSC
                within: 1d
                when: {always: true}
            """);

        _ca.Given(Request.Create().WithPath("/api/incidents/").WithParam("ref_id", "INC-2026-01").UsingGet())
            .RespondWith(Response.Create().WithBody(List(new JsonObject
            {
                ["id"] = "inc-1",
                ["ref_id"] = "INC-2026-01",
                ["name"] = "Malware on a laptop",
                ["reported_at"] = "2026-11-20T12:00:00Z",
                ["assets"] = new JsonArray(new JsonObject { ["id"] = "as-2", ["str"] = "Example laptops" }),
            }).ToJsonString()));
        _ca.Given(Request.Create().WithPath("/api/timeline-entries/").WithParam("incident", "inc-1").UsingGet())
            .RespondWith(Response.Create().WithBody(List(
                Entry("2026-11-20T09:30:00Z", "detection", "The EDR flagged ransomware on a laptop."),
                Entry("2026-11-20T15:00:00Z", "observation", "Reported: reportcyber, reference 12345")).ToJsonString()));
    }

    [Fact]
    public async Task Lists_deadlines_from_discovery_time()
    {
        var exitCode = await Run("incident", "obligations", "INC-2026-01", "--format", "json");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString() + string.Join('\n', _logs.Messages));
        var result = JsonNode.Parse(_output.ToString())!;
        result["incident"]!.GetValue<string>().ShouldBe("INC-2026-01");
        DateTimeOffset.Parse(result["discovered"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe(new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero), "Discovery is the first detection entry, not when it was recorded.");
        var reports = result["reports"]!.AsArray();
        reports.Select(r => r!["id"]!.GetValue<string>()).ShouldBe(["reportcyber", "dfars-7012"]);
        reports[0]!["status"]!.GetValue<string>().ShouldBe("made");
        var dfars = reports[1]!;
        DateTimeOffset.Parse(dfars["due"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe(new DateTimeOffset(2026, 11, 23, 9, 30, 0, TimeSpan.Zero));
        dfars["status"]!.GetValue<string>().ShouldBe("overdue");
        dfars["why"]!.GetValue<string>().ShouldContain("Example laptops handles cui");
        _ca.LogEntries.ShouldNotContain(e => e.RequestMessage!.Method != "GET");
    }

    [Fact]
    public async Task Text_output_names_each_report_its_deadline_and_where_it_goes()
    {
        var exitCode = await Run("incident", "obligations", "INC-2026-01");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var text = _output.ToString();
        text.ShouldContain("dfars-7012");
        text.ShouldContain("2026-11-23 09:30 UTC");
        text.ShouldContain("OVERDUE");
        text.ShouldContain("DoD, through the DIBNet portal");
    }

    [Fact]
    public async Task Report_pack_dfars_gathers_what_the_dod_report_asks_for()
    {
        // s17-07: the laptops handle CUI in this test's scope, so the incident owes the 72-hour DFARS report; the pack is for the person who files it.
        var exitCode = await Run("incident", "report-pack", "INC-2026-01", "--dfars");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString() + string.Join('\n', _logs.Messages));
        var pack = _output.ToString();
        pack.ShouldStartWith("# DFARS 252.204-7012 report pack: INC-2026-01 Malware on a laptop\n");
        pack.ShouldContain("- Report due: 2026-11-23 09:30 UTC (72 hours");
        pack.ShouldContain("| Example laptops | cui (processes): CUI//SP-CTI | CUI asset |");
        pack.ShouldContain("| 2026-11-20 09:30 UTC | The EDR flagged ransomware on a laptop. |");
        _ca.LogEntries.ShouldNotContain(e => e.RequestMessage!.Method != "GET");
    }

    [Fact]
    public async Task Report_pack_needs_a_kind_of_report()
    {
        var exitCode = await Run("incident", "report-pack", "INC-2026-01");

        exitCode.ShouldNotBe(ExitCodes.Ok);
        _logs.Messages.ShouldContain(m => m.Contains("--dfars", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unknown_incident_is_a_problem()
    {
        _ca.Given(Request.Create().WithPath("/api/incidents/").WithParam("ref_id", "INC-NONE").UsingGet()).RespondWith(Response.Create().WithBody(List().ToJsonString()));

        var exitCode = await Run("incident", "obligations", "INC-NONE");

        exitCode.ShouldNotBe(ExitCodes.Ok);
        _logs.Messages.ShouldContain(m => m.Contains("INC-NONE", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        _ca.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private Task<int> Run(params string[] args) => HoundCli.RunAsync(
        [.. args, "--scope", Path.Combine(_dir, "scope"), "--config", Path.Combine(_dir, "hound.yaml")],
        _output,
        _error,
        logging => logging.AddProvider(_logs),
        new Dictionary<string, string?> { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = "example-token-for-cli-tests" },
        _ct);

    private static JsonObject Entry(string at, string type, string entry) => new() { ["timestamp"] = at, ["entry_type"] = type, ["entry"] = entry };

    private static JsonObject List(params JsonObject[] items) => new() { ["count"] = items.Length, ["results"] = new JsonArray([.. items]) };
}
