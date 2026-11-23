using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Manual;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s09-42: <c>hound manual propose</c> records an agent's proposed result next to a manual evidence file. A proposal is pending: it is not
/// an attestation, so it is not evidence and no run publishes it.
/// </summary>
public sealed class ManualProposeTests : IDisposable
{
    internal const string Source = "example-firewall-config";
    internal const string Content = "rule 1: allow https from any to web-01.example.com\nrule 14: deny any any\n";

    internal const string Config = """
        ciso_assistant: {url: "http://localhost:8000/api/", token: "env:HOUND_CA_TOKEN"}
        evidence: {root: ./evidence}
        collectors:
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
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("hound-manual-propose-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public ManualProposeTests() => File.WriteAllText(Path.Combine(_root, "hound.yaml"), Config);

    [Fact]
    public async Task Proposal_is_pending_and_not_published()
    {
        var export = Path.Combine(_root, "firewall-export.txt");
        await File.WriteAllTextAsync(export, Content, _ct);

        var (exit, output, error) = await ProposeAsync(
            _root, export, "--result", "pass", "--check", "network.firewall.config_exported=partial", "--observation", "The export lists 14 rules; the last denies any to any.",
            "--supports", "rule 14: deny any any", "--supports", "line 1: rule 1 allows https only", "--json");

        exit.ShouldBe(ExitCodes.Ok, error);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Content)));
        var answer = JsonNode.Parse(output)!;
        (answer["source"]!.GetValue<string>(), answer["file"]!.GetValue<string>(), answer["sha256"]!.GetValue<string>(), answer["pending"]!.GetValue<bool>())
            .ShouldBe((Source, "firewall-export.txt", hash, true));

        // The file is kept in the drop folder with the proposal beside it, and no attestation: nothing a run would publish as attested.
        var drop = Path.Combine(_root, "manual", "firewall");
        Directory.GetFiles(drop).Select(Path.GetFileName).ShouldBe(["firewall-export.txt", "firewall-export.txt.proposal.yaml"], ignoreOrder: true);
        var proposal = await File.ReadAllTextAsync(Path.Combine(drop, "firewall-export.txt.proposal.yaml"), _ct);
        proposal.ShouldContain("proposed_by: agent");
        proposal.ShouldContain($"file_sha256: {hash}");
        proposal.ShouldContain("network.firewall.rules_reviewed: pass");
        proposal.ShouldContain("network.firewall.config_exported: partial");
        proposal.ShouldContain("rule 14: deny any any");
        var source = new ManualSource(drop, ["*"], [], 30, "Alex Example");
        source.Accepts("firewall-export.txt.proposal.yaml").ShouldBeFalse();

        // Proposing again for the same file replaces the pending proposal. A file a person already attested takes no proposal.
        (exit, _, error) = await ProposeAsync(_root, "firewall-export.txt", "--result", "fail", "--observation", "Rule 1 is too broad.", "--supports", "rule 1");
        exit.ShouldBe(ExitCodes.Ok, error);
        (await File.ReadAllTextAsync(Path.Combine(drop, "firewall-export.txt.proposal.yaml"), _ct)).ShouldContain("network.firewall.rules_reviewed: fail");
        await File.WriteAllTextAsync(Path.Combine(drop, "firewall-export.txt.attest.yaml"), "who: Sam Example\ndate: 2026-11-20\noutcomes: {}\n", _ct);
        (exit, _, error) = await ProposeAsync(_root, "firewall-export.txt", "--result", "pass", "--observation", "Fine.", "--supports", "rule 14");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("already attested");

        // What a proposal must have: something in the file that supports it, and a file the source accepts.
        (exit, _, error) = await ProposeAsync(_root, export, "--result", "pass", "--observation", "Looks fine.");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("--supports");
        (exit, _, error) = await ProposeAsync(_root, Path.Combine(_root, "hound.yaml"), "--result", "pass", "--observation", "Fine.", "--supports", "line 1");
        exit.ShouldBe(ExitCodes.Configuration);
        error.ShouldContain("*.txt");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    internal static Task<(int Exit, string Output, string Error)> ProposeAsync(string root, string file, params string[] more) => OnboardAgentCommandTests.RunAsync(
        ["manual", "propose", Source, file, "--config", Path.Combine(root, "hound.yaml"), .. more], "", terminal: false, Path.Combine(root, "home"), TestContext.Current.CancellationToken);
}
