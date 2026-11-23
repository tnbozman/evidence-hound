using EvidenceHound.Collectors.Manual;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s09-42: <c>hound manual approve</c> walks a person through every pending proposal on a terminal: the file, the agent's result,
/// observation and what supports it. The person attests, changes or rejects each; only then is there an attestation for a run to publish.
/// </summary>
public sealed class ManualApproveTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hound-manual-approve-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public ManualApproveTests() => File.WriteAllText(Path.Combine(_root, "hound.yaml"), ManualProposeTests.Config);

    [Fact]
    public async Task Walks_through_pending_proposals_and_records_the_attester()
    {
        await ProposeAsync("a-export.txt", "pass", "The export lists 14 rules; the last denies any to any.");
        await ProposeAsync("b-export.txt", "pass", "All rules were reviewed.");
        await ProposeAsync("c-export.txt", "fail", "The export is empty.");

        // Sam attests the first, changes the second (one check partial, with their own observation) and rejects the third.
        var answers = "Sam Example\na\nc\npartial\n\nOnly half the rules carry a review date.\nr\nThis is last month's export.\n";
        var (exit, output, error) = await ApproveAsync(answers, terminal: true);

        exit.ShouldBe(ExitCodes.Ok, error);
        error.ShouldContain("[1/3] example-firewall-config a-export.txt");
        error.ShouldContain("Proposed by agent: pass");
        error.ShouldContain("The export lists 14 rules; the last denies any to any.");
        error.ShouldContain("Supports: rule 14: deny any any");
        output.ShouldContain("Attested 2 of 3 proposals; 1 rejected.");

        var first = await AttestationAsync("a-export.txt");
        (first.Who, first.ProposedBy, first.Note).ShouldBe(("Sam Example", "agent", "The export lists 14 rules; the last denies any to any."));
        first.Outcomes.Values.ShouldAllBe(o => o == CheckOutcome.Pass);
        first.FileSha256.ShouldBe(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(Drop, "a-export.txt"), _ct))));

        var second = await AttestationAsync("b-export.txt");
        second.Outcomes.ShouldBe(
            new Dictionary<string, CheckOutcome> { ["network.firewall.rules_reviewed"] = CheckOutcome.Partial, ["network.firewall.config_exported"] = CheckOutcome.Pass }, ignoreOrder: true);
        (second.Who, second.ProposedBy, second.Note).ShouldBe(("Sam Example", "agent", "Only half the rules carry a review date."));

        // The rejected proposal leaves no attestation; the proposal records who rejected it and why, and is not asked again.
        File.Exists(Path.Combine(Drop, "c-export.txt.attest.yaml")).ShouldBeFalse();
        var rejected = await File.ReadAllTextAsync(Path.Combine(Drop, "c-export.txt.proposal.yaml"), _ct);
        rejected.ShouldContain("rejected_by: Sam Example");
        rejected.ShouldContain("This is last month's export.");
        File.Exists(Path.Combine(Drop, "a-export.txt.proposal.yaml")).ShouldBeFalse();

        (exit, output, error) = await ApproveAsync("", terminal: true);
        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("No proposals are waiting.");
    }

    [Fact]
    public async Task Refuses_without_a_terminal()
    {
        await ProposeAsync("a-export.txt", "pass", "The export lists 14 rules.");

        var (exit, _, error) = await ApproveAsync("Sam Example\na\n", terminal: false);

        exit.ShouldBe(ExitCodes.Usage);
        error.ShouldContain("needs a person at a terminal");
        File.Exists(Path.Combine(Drop, "a-export.txt.attest.yaml")).ShouldBeFalse();
        File.Exists(Path.Combine(Drop, "a-export.txt.proposal.yaml")).ShouldBeTrue();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Drop => Path.Combine(_root, "manual", "firewall");

    private async Task<Attestation> AttestationAsync(string file) =>
        Attestation.Parse(await File.ReadAllTextAsync(Path.Combine(Drop, file + Attestation.Suffix), _ct), file).Match(a => a, p => throw new InvalidOperationException(p[0].Message));

    private async Task ProposeAsync(string name, string result, string observation)
    {
        var export = Path.Combine(_root, name);
        await File.WriteAllTextAsync(export, $"{name}\nrule 14: deny any any\n", _ct);
        var (exit, _, error) = await ManualProposeTests.ProposeAsync(_root, export, "--result", result, "--observation", observation, "--supports", "rule 14: deny any any");
        exit.ShouldBe(ExitCodes.Ok, error);
    }

    private Task<(int Exit, string Output, string Error)> ApproveAsync(string answers, bool terminal) => OnboardAgentCommandTests.RunAsync(
        ["manual", "approve", "--config", Path.Combine(_root, "hound.yaml")], answers, terminal, Path.Combine(_root, "home"), _ct);
}
