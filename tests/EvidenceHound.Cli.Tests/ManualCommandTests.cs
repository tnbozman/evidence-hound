using EvidenceHound.Collectors.Manual;
using EvidenceHound.Core.Checks;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

public sealed class ManualCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-manual-tests-").FullName;

    private string Drop => Path.Combine(_workDir, "manual", "firewall");

    [Fact]
    public async Task Upload_with_attestation_copies_the_file_and_writes_its_sidecar()
    {
        var export = await ExportAsync("firewall-export.txt", "rule 1: allow https from any to web-01.example.com");

        var exitCode = await Run("y\n\npass\nfail\nReviewed with the network team.\n", "manual", "upload", "--config", await ConfigAsync(), "--source", "example-firewall-config", export);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var copied = Path.Combine(Drop, "firewall-export.txt");
        (await File.ReadAllTextAsync(copied, TestContext.Current.CancellationToken)).ShouldBe("rule 1: allow https from any to web-01.example.com");
        var attestation = Attestation.Parse(await File.ReadAllTextAsync(copied + Attestation.Suffix, TestContext.Current.CancellationToken), "sidecar").Match(a => a, p => throw new InvalidOperationException(p[0].Message));
        attestation.Who.ShouldBe("Alex Example");
        attestation.Outcomes.ShouldBe(new Dictionary<string, CheckOutcome> { ["network.firewall.rules_reviewed"] = CheckOutcome.Pass, ["network.firewall.config_exported"] = CheckOutcome.Fail }, ignoreOrder: true);
        attestation.Note.ShouldBe("Reviewed with the network team.");
        _output.ToString().ShouldContain(copied);
        _error.ToString().ShouldContain("network.firewall.rules_reviewed");
    }

    [Fact]
    public async Task Upload_without_attesting_copies_only_the_file()
    {
        var export = await ExportAsync("firewall-export.txt", "rule 1: allow https");

        var exitCode = await Run("n\n", "manual", "upload", "--config", await ConfigAsync(), "--source", "example-firewall-config", export);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        Directory.GetFiles(Drop).Select(Path.GetFileName).ShouldBe(["firewall-export.txt"]);
    }

    [Fact]
    public async Task Same_file_uploaded_twice_is_copied_once()
    {
        var config = await ConfigAsync();
        var first = await ExportAsync("firewall-export.txt", "rule 1: allow https");
        var again = await ExportAsync("firewall-export-copy.txt", "rule 1: allow https");

        (await Run("n\n", "manual", "upload", "--config", config, "--source", "example-firewall-config", first)).ShouldBe(ExitCodes.Ok);
        var exitCode = await Run("", "manual", "upload", "--config", config, "--source", "example-firewall-config", again);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        Directory.GetFiles(Drop).Select(Path.GetFileName).ShouldBe(["firewall-export.txt"]);
        _logs.Entries.ShouldContain(e => e.Message.Contains("already uploaded", StringComparison.Ordinal) && e.Message.Contains("firewall-export.txt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_or_non_manual_source_exits_three()
    {
        var config = await ConfigAsync();
        var export = await ExportAsync("firewall-export.txt", "rule 1: allow https");

        (await Run("", "manual", "upload", "--config", config, "--source", "no-such-source", export)).ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("example-firewall-config");
        (await Run("", "manual", "upload", "--config", config, "--source", "example-fake", export)).ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("not a manual source");
        Directory.Exists(Drop).ShouldBeFalse();
    }

    [Fact]
    public async Task File_not_matching_the_patterns_exits_three()
    {
        var export = await ExportAsync("notes.md", "not an export");

        var exitCode = await Run("", "manual", "upload", "--config", await ConfigAsync(), "--source", "example-firewall-config", export);

        exitCode.ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("*.txt");
        Directory.Exists(Drop).ShouldBeFalse();
    }

    [Fact]
    public async Task Invalid_outcome_answer_exits_two_and_copies_nothing()
    {
        var export = await ExportAsync("firewall-export.txt", "rule 1: allow https");

        var exitCode = await Run("y\n\nmaybe\n", "manual", "upload", "--config", await ConfigAsync(), "--source", "example-firewall-config", export);

        exitCode.ShouldBe(ExitCodes.Usage);
        _error.ToString().ShouldContain("maybe");
        Directory.Exists(Drop).ShouldBeFalse();
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }

    private async Task<string> ExportAsync(string name, string content)
    {
        var path = Path.Combine(_workDir, name);
        await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
        return path;
    }

    private async Task<string> ConfigAsync()
    {
        var path = Path.Combine(_workDir, "hound.yaml");
        await File.WriteAllTextAsync(path, """
            ciso_assistant: {url: "http://localhost:8000/api/", token: "env:HOUND_CA_TOKEN"}
            evidence: {root: ./evidence}
            collectors:
              - {name: example-fake, type: fake, schedule: "0 2 * * *"}
              - name: example-firewall-config
                type: manual
                schedule: "0 6 * * *"
                settings:
                  drop_folder: ./manual/firewall
                  patterns: "*.txt"
                  checks: network.firewall.rules_reviewed, network.firewall.config_exported
                  due_days: 30
                  owner: Alex Example
            target: {mapping: ./mapping.yaml, compliance_assessment: EXAMPLE-1}
            """, TestContext.Current.CancellationToken);
        return path;
    }

    private Task<int> Run(string input, params string[] args) =>
        HoundCli.RunAsync(args, new StringReader(input), _output, _error, logging => logging.AddProvider(_logs), TestContext.Current.CancellationToken);
}
