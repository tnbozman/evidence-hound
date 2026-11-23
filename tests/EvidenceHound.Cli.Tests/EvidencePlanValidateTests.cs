using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

/// <summary>s10-04: <c>hound scope validate</c> reads <c>scope/evidence-plan.yaml</c>, finds each runbook in the repo and each collector in the config.</summary>
public sealed class EvidencePlanValidateTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hound-evidence-plan-").FullName;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public EvidencePlanValidateTests() => TestFiles.CopyExampleRepo(_root);

    [Fact]
    public async Task A_runbook_that_is_not_in_the_repo_and_a_collector_not_in_the_config_are_reported()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "scope", "evidence-plan.yaml"), """
            grc_team: IT team
            evidence:
              - ref_id: EV-BACKUP-RESTORE
                control: AC-BACKUP
                method: manual
                owner: IT team
                every: 6 months
                runbook: manual-sources/missing-runbook.md
              - ref_id: EV-MFA
                control: AC-MFA
                method: automated
                collector: example-sophos
            """, _ct);

        var exit = await Hound("scope", "validate", Path.Combine(_root, "scope"), "--config", Path.Combine(_root, "hound.yaml"), "--format", "json");

        exit.ShouldNotBe(ExitCodes.Ok);
        var problems = JsonNode.Parse(_output.ToString())!.AsArray().Select(p => (Code: p!["code"]!.GetValue<string>(), Line: p["line"]!.GetValue<int>())).ToArray();
        problems.ShouldBe([("evidence_plan.runbook_file_missing", 3), ("evidence_plan.collector_unknown", 12)], ignoreOrder: true);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private Task<int> Hound(params string[] args) =>
        HoundCli.RunAsync(args, _output, _error, logging => logging.AddProvider(new RecordingLoggerProvider()), new Dictionary<string, string?>(), _ct);
}
