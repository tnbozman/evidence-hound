using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Cli.Tests;

/// <summary>s12-08: <c>hound mapping crosswalk</c>: which NIST SP 800-171 requirements the Essential Eight evidence answers today. Reads only.</summary>
public sealed class MappingCrosswalkCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly WireMockServer _ca = WireMockServer.Start();
    private readonly string _dir = Directory.CreateTempSubdirectory("hound-crosswalk-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public MappingCrosswalkCommandTests()
    {
        TestFiles.CopyExampleRepo(_dir);
        var rows = new JsonObject
        {
            ["count"] = 2,
            ["results"] = new JsonArray(
                Row("ra-1", "urn:intuitem:risk:req_node:essential-eight-2023:rap:ism-0445", "ISM-0445", "compliant"),
                Row("ra-2", "urn:intuitem:risk:req_node:essential-eight-2023:mfa:ism-1504", "ISM-1504", "non_compliant")),
        };
        _ca.Given(Request.Create().WithPath("/api/requirement-assessments/").WithParam("compliance_assessment__ref_id", "EX-E8-2026").UsingGet())
            .RespondWith(Response.Create().WithBody(rows.ToJsonString()));
    }

    [Fact]
    public async Task Prints_each_800_171_requirement_with_the_essential_eight_evidence_behind_it_and_todays_result()
    {
        var exitCode = await Run(Online, "mapping", "crosswalk", "--from", "essential-eight");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString() + string.Join("\n", _logs.Messages));
        var text = _output.ToString();
        text.ShouldContain("urn:intuitem:risk:framework:nist-sp-800-171-rev2");
        text.ShouldContain("Advisory");
        text.ShouldContain("of 110 requirements");
        text.ShouldContain("| 3.1.6 Use non-privileged accounts or roles for non-security functions | answered | ISM-0445 (equal, 1 check): compliant");
        text.ShouldContain("ISM-1504 (intersect, 3 checks): non compliant");
        _ca.LogEntries.ShouldNotBeEmpty();
        _ca.LogEntries.ShouldAllBe(e => e.RequestMessage!.Method == "GET");
    }

    [Fact]
    public async Task Offline_it_reads_no_ciso_assistant_and_prints_json_for_an_agent()
    {
        var exitCode = await Run([], "mapping", "crosswalk", "--from", "essential-eight", "--offline", "--json");

        exitCode.ShouldBe(ExitCodes.Ok, string.Join("\n", _logs.Messages));
        _ca.LogEntries.ShouldBeEmpty();
        var report = JsonNode.Parse(_output.ToString())!;
        report["to"]!.GetValue<string>().ShouldBe("urn:intuitem:risk:framework:nist-sp-800-171-rev2");
        report["advisory"]!.GetValue<bool>().ShouldBeTrue();
        report["reviewed"].ShouldBeNull();
        report["audit"].ShouldBeNull();
        var rows = report["rows"]!.AsArray();
        rows.Count.ShouldBe(110);
        (report["answered"]!.GetValue<int>() + report["partly_answered"]!.GetValue<int>() + report["open"]!.GetValue<int>()).ShouldBe(110);
        var row = rows.Single(r => r!["ref"]!.GetValue<string>() == "3.1.6")!;
        row["status"]!.GetValue<string>().ShouldBe("answered");
        row["sources"]![0]!["ref"]!.GetValue<string>().ShouldBe("ISM-0445");
        row["sources"]![0]!["checks"]![0]!.GetValue<string>().ShouldBe("identity.privileged.admins_cloud_only");
        row["sources"]![0]!["result"].ShouldBeNull();
    }

    [Fact]
    public async Task A_crosswalk_in_the_repo_is_used_before_the_one_hound_ships()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "mappings", "crosswalks"));
        await File.WriteAllTextAsync(Path.Combine(_dir, "mappings", "crosswalks", "ours.yaml"), """
            from: urn:intuitem:risk:framework:essential-eight-2023
            to: urn:intuitem:risk:framework:nist-sp-800-171-rev2
            reviewed: {by: Security lead, on: 2026-11-20}
            requirements:
              - {ref: "3.5.3", title: "Use multifactor authentication", answered_by: [{ref: ISM-1504, relationship: superset}]}
            """, _ct);

        var exitCode = await Run([], "mapping", "crosswalk", "--from", "essential-eight", "--offline");

        exitCode.ShouldBe(ExitCodes.Ok, string.Join("\n", _logs.Messages));
        _output.ToString().ShouldContain("Reviewed by Security lead on 2026-11-20");
        _output.ToString().ShouldContain("of 1 requirements");
    }

    [Fact]
    public async Task A_crosswalk_from_another_framework_is_refused()
    {
        var file = Path.Combine(_dir, "other.yaml");
        await File.WriteAllTextAsync(file, """
            from: urn:example:risk:framework:other
            to: urn:intuitem:risk:framework:nist-sp-800-171-rev2
            requirements: []
            """, _ct);

        var exitCode = await Run([], "mapping", "crosswalk", "--from", "essential-eight", "--offline", "--file", file);

        exitCode.ShouldBe(ExitCodes.Configuration);
        _logs.Messages.ShouldContain(m => m.Contains("urn:example:risk:framework:other", StringComparison.Ordinal) && m.Contains("essential-eight", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_a_ciso_assistant_it_fails_and_suggests_offline()
    {
        var exitCode = await Run(new() { ["HOUND_TODAY"] = "2026-12-02" }, "mapping", "crosswalk", "--from", "essential-eight");

        exitCode.ShouldNotBe(ExitCodes.Ok);
        _logs.Messages.ShouldContain(m => m.Contains("--offline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unknown_programme_is_refused_with_the_ones_there_are()
    {
        var exitCode = await Run([], "mapping", "crosswalk", "--from", "cmmc", "--offline");

        exitCode.ShouldBe(ExitCodes.Configuration);
        _logs.Messages.ShouldContain(m => m.Contains("essential-eight", StringComparison.Ordinal) && m.Contains("ai6", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        _ca.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private Dictionary<string, string?> Online => new() { ["HOUND_CA_URL"] = $"{_ca.Url}/api/", ["HOUND_CA_TOKEN"] = "example-token-for-cli-tests", ["HOUND_TODAY"] = "2026-12-02" };

    private Task<int> Run(Dictionary<string, string?> settings, params string[] args) => HoundCli.RunAsync(
        [.. args, "--scope", Path.Combine(_dir, "scope"), "--config", Path.Combine(_dir, "hound.yaml")],
        _output,
        _error,
        logging => logging.AddProvider(_logs),
        settings,
        _ct);

    private static JsonObject Row(string id, string urn, string refId, string result) => new()
    {
        ["id"] = id,
        ["result"] = result,
        ["requirement"] = new JsonObject { ["urn"] = urn, ["ref_id"] = refId, ["implementation_groups"] = new JsonArray("1", "2", "3") },
    };
}
