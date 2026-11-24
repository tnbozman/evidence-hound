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

    [Fact]
    public async Task A_hand_written_row_conflicts_only_once_a_configured_collector_reports_its_checks()
    {
        // s10-04: manual checks are mapped, but a run publishes a requirement only when every one of its checks is reported, so a team
        // keeps hand-writing a row until it configures the manual source for it.
        var scope = Path.Combine(_root, "scope");
        var config = Path.Combine(_root, "hound.yaml");
        (await Hound("scope", "validate", scope, "--config", config)).ShouldBe(ExitCodes.Ok, _error.ToString());

        var text = await File.ReadAllTextAsync(config, _ct);
        await File.WriteAllTextAsync(config, text.Replace("\ntarget:", """

              - name: user-application-hardening
                type: manual
                schedule: "0 6 * * *"
                settings:
                  drop_folder: /var/lib/evidence-hound/manual/user-application-hardening
                  checks: browser.java_blocked
                  due_days: 183
                  owner: Example endpoint owner

            target:
            """, StringComparison.Ordinal), _ct);

        (await Hound("scope", "validate", scope, "--config", config)).ShouldNotBe(ExitCodes.Ok);
        _error.ToString().ShouldContain("scope.result_conflict");
        _error.ToString().ShouldContain("ISM-1486");
    }

    [Fact]
    public async Task Each_runbook_is_read_from_the_repo_for_its_task_description()
    {
        // The registry is only read with --config.
        var checkedScope = await ScopeCommands.ValidateAsync(new DirectoryInfo(Path.Combine(_root, "scope")), null, null!, new DateOnly(2026, 11, 24), _ct);

        var runbook = await File.ReadAllTextAsync(Path.Combine(_root, "manual-sources", "backup-restore-test.md"), _ct);
        checkedScope.Inputs.Runbooks["manual-sources/backup-restore-test.md"].ShouldBe(runbook);
        checkedScope.Inputs.Runbooks.Keys.ShouldBe(["manual-sources/backup-restore-test.md", "manual-sources/mfa-exceptions-review.md", "manual-sources/weekly-compliance-review.md"], ignoreOrder: true);
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
