using System.Text.Json.Nodes;
using EvidenceHound.Core.Planning;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EvidenceHound.Cli.Tests;

public sealed class PublishCommandTests : IDisposable
{
    private static readonly string Token = "example-token-for-cli-tests";
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly WireMockServer _ca = WireMockServer.Start();

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        _ca.Dispose();
    }

    [Fact]
    public async Task Dry_run_prints_the_report_without_calling_ciso_assistant()
    {
        var exitCode = await Publish(settings: [], "--dry-run");

        exitCode.ShouldBe(ExitCodes.Ok);
        _ca.LogEntries.ShouldBeEmpty();
        var report = Report();
        report["planned"]!.GetValue<int>().ShouldBe(10);
        report["sent"]!.GetValue<int>().ShouldBe(0);
        report["items"]![0]!["update"]!.GetValue<string>().ShouldBe("upload evidence ev-0001 (mfa-admins.json)");
        report["items"]![0]!["outcome"]!.GetValue<string>().ShouldBe("planned");
    }

    [Fact]
    public async Task A_target_naming_an_audit_with_a_cycle_publishes_to_the_period_open_that_day()
    {
        var dir = Directory.CreateTempSubdirectory("hound-periods-").FullName;
        try
        {
            var files = Path.Combine(AppContext.BaseDirectory, "files");
            Directory.CreateDirectory(Path.Combine(dir, "scope", "audits"));
            await File.WriteAllTextAsync(Path.Combine(dir, "scope", "setup.yaml"), "domains: []\n", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(dir, "scope", "audits", "example-baseline.yaml"), """
                ref_id: EXAMPLE-BASELINE
                name: Example baseline
                framework: urn:example:risk:framework:example-baseline
                domain: Example Pty Ltd
                perimeter: Corporate IT
                cycle: {period: quarter, starts: "2026-07-01"}
                unlisted: {result: not_assessed}
                """, TestContext.Current.CancellationToken);
            var config = (await File.ReadAllTextAsync(Path.Combine(files, "examples", "example-pty-ltd.yaml"), TestContext.Current.CancellationToken))
                .Replace("../mappings/example.yaml", Path.Combine(files, "mappings", "example.yaml"), StringComparison.Ordinal)
                .Replace("compliance_assessment: EXAMPLE-BASELINE-2026", "compliance_assessment: EXAMPLE-BASELINE", StringComparison.Ordinal)
                + "\nscope: scope\n";
            await File.WriteAllTextAsync(Path.Combine(dir, "hound.yaml"), config, TestContext.Current.CancellationToken);

            var exitCode = await HoundCli.RunAsync(
                [
                    "publish", "--dry-run", "--config", Path.Combine(dir, "hound.yaml"),
                    "--checks", Path.Combine(files, "fixtures", "publish", "checks.json"),
                    "--evidence-dir", Path.Combine(files, "fixtures", "publish", "evidence"),
                ],
                _output,
                _error,
                logging => logging.AddProvider(_logs),
                new Dictionary<string, string?> { ["HOUND_TODAY"] = "2026-11-21" },
                TestContext.Current.CancellationToken);

            exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
            _output.ToString().ShouldContain("EXAMPLE-BASELINE-Q2-2027FY");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task A_plan_that_cannot_be_made_exits_three()
    {
        var exitCode = await HoundCli.RunAsync(
            ["publish", "--config", "missing.yaml", "--checks", "missing.json", "--evidence-dir", "."],
            _output,
            _error,
            logging => logging.AddProvider(_logs),
            Settings(),
            TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCodes.Configuration);
        _ca.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_token_exits_three()
    {
        var exitCode = await Publish(new Dictionary<string, string?> { ["HOUND_CA_URL"] = ApiUrl });

        exitCode.ShouldBe(ExitCodes.Configuration);
        _logs.Messages.ShouldContain(m => m.Contains("HOUND_CA_TOKEN", StringComparison.Ordinal));
        _ca.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_url_exits_three()
    {
        var exitCode = await Publish(new Dictionary<string, string?> { ["HOUND_CA_TOKEN"] = Token });

        exitCode.ShouldBe(ExitCodes.Configuration);
        _logs.Messages.ShouldContain(m => m.Contains("HOUND_CA_URL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sends_the_plan_and_prints_the_report()
    {
        FakeEverything();

        var exitCode = await Publish(Settings());

        exitCode.ShouldBe(ExitCodes.Ok);
        var report = Report();
        (report["sent"]!.GetValue<int>(), report["failed"]!.GetValue<int>()).ShouldBe((10, 0));
        _ca.LogEntries.ShouldAllBe(e => e.RequestMessage!.Headers!["Authorization"][0] == $"Token {Token}");
        _logs.Messages.ShouldContain("Published to CISO Assistant: 10 sent, 0 skipped, 0 failed");
    }

    [Fact]
    public async Task Each_result_it_writes_carries_the_run_marker_so_scope_apply_and_export_leave_it_to_the_runs()
    {
        FakeEverything();

        (await Publish(Settings())).ShouldBe(ExitCodes.Ok);

        var patches = _ca.LogEntries.Where(e => e.RequestMessage!.Method == "PATCH" && e.RequestMessage.Path.StartsWith("/api/requirement-assessments/", StringComparison.Ordinal)).ToArray();
        patches.ShouldNotBeEmpty();
        patches.ShouldAllBe(e => RowState.ReadMark(JsonNode.Parse(e.RequestMessage!.Body!)!["observation"]!.GetValue<string>()) != null);
    }

    [Fact]
    public async Task A_failed_item_exits_four_and_logs_its_problem()
    {
        FakeEverything();
        _ca.Given(Request.Create().WithPath("/api/applied-controls/").UsingGet().WithParam("ref_id", "AC-MFA"))
            .AtPriority(1)
            .RespondWith(Json(200, """{"count": 0, "results": []}"""));

        var exitCode = await Publish(Settings());

        exitCode.ShouldBe(ExitCodes.PublishFailed);
        Report()["failed"]!.GetValue<int>().ShouldBe(1);
        _logs.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("AC-MFA", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Kafka_transport_is_removed_and_says_so()
    {
        // s19-02: the Kafka transport was removed in release 3.
        var exitCode = await Publish(new Dictionary<string, string?> { ["HOUND_TRANSPORT"] = "kafka" });

        exitCode.ShouldBe(ExitCodes.Configuration);
        _logs.Messages.ShouldContain(m => m.Contains("removed", StringComparison.Ordinal) && m.Contains("REST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_transport_exits_three()
    {
        var exitCode = await Publish(new Dictionary<string, string?> { ["HOUND_TRANSPORT"] = "carrier-pigeon" });

        exitCode.ShouldBe(ExitCodes.Configuration);
        _logs.Messages.ShouldContain(m => m.Contains("carrier-pigeon", StringComparison.Ordinal));
    }

    private string ApiUrl => $"{_ca.Url}/api/";

    private Dictionary<string, string?> Settings() => new() { ["HOUND_CA_URL"] = ApiUrl, ["HOUND_CA_TOKEN"] = Token };

    private Task<int> Publish(Dictionary<string, string?> settings, params string[] extra) =>
        HoundCli.RunAsync(
            [
                "publish",
                "--config", Path.Combine(AppContext.BaseDirectory, "files", "examples", "example-pty-ltd.yaml"),
                "--checks", Path.Combine(AppContext.BaseDirectory, "files", "fixtures", "publish", "checks.json"),
                "--evidence-dir", Path.Combine(AppContext.BaseDirectory, "files", "fixtures", "publish", "evidence"),
                .. extra,
            ],
            _output,
            _error,
            logging => logging.AddProvider(_logs),
            settings,
            TestContext.Current.CancellationToken);

    private JsonNode Report() => JsonNode.Parse(_output.ToString())!;

    private void FakeEverything()
    {
        _ca.Given(Request.Create().WithPath("/api/evidences/").UsingGet()).RespondWith(Json(200, """{"count": 0, "results": []}"""));
        _ca.Given(Request.Create().WithPath("/api/evidences/").UsingPost()).RespondWith(Json(201, """{"id": "ev-uuid"}"""));
        _ca.Given(Request.Create().WithPath("/api/evidence-revisions/").UsingPost()).RespondWith(Json(201, """{"id": "rev-uuid"}"""));
        _ca.Given(Request.Create().WithPath("/api/requirement-assessments/").UsingGet())
            .RespondWith(Json(200, """{"count": 1, "results": [{"id": "ra-uuid", "requirement": {"urn": "urn:example:risk:req_node:example-baseline:ex-1", "ref_id": "EX-1", "implementation_groups": null}}]}"""));
        _ca.Given(Request.Create().WithPath("/api/compliance-assessments/").UsingGet()).RespondWith(Json(200, """{"count": 1, "results": [{"id": "ca-uuid"}]}"""));
        _ca.Given(Request.Create().WithPath("/api/compliance-assessments/ca-uuid/object/").UsingGet()).RespondWith(Json(200, """{"id": "ca-uuid", "selected_implementation_groups": []}"""));
        _ca.Given(Request.Create().WithPath("/api/requirement-assessments/ra-uuid/").UsingPatch()).RespondWith(Json(200, """{"id": "ra-uuid"}"""));
        _ca.Given(Request.Create().WithPath("/api/applied-controls/").UsingGet()).AtPriority(5).RespondWith(Json(200, """{"count": 1, "results": [{"id": "ac-uuid"}]}"""));
        _ca.Given(Request.Create().WithPath("/api/applied-controls/ac-uuid/").UsingPatch()).RespondWith(Json(200, """{"id": "ac-uuid"}"""));
    }

    private static IResponseBuilder Json(int status, string body) =>
        Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body);
}
