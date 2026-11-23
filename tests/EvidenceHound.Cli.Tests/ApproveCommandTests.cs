using EvidenceHound.Core.Yaml;

namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s09-37: <c>hound approve</c> walks a person through everything drafted in the config repo that needs their approval, in turn, and records
/// who approved it and when. Nothing is approved in CISO Assistant: the approved state reaches it through <c>hound scope apply</c>.
/// </summary>
public sealed class ApproveCommandTests : IDisposable
{
    internal const string Capabilities = """
        framework: "urn:example:framework"
        mitigations:
          - mitigation: "EX"
            name: "Example mitigation"
            provided_by: manual
            reason: "The owner reports on it each quarter."
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("hound-approve-").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public ApproveCommandTests()
    {
        TestFiles.CopyExampleRepo(Repo);
        File.WriteAllText(Path.Combine(Repo, "frameworks", "example-framework.json"), OnboardSystemCommandTests.Framework);
    }

    [Fact]
    public async Task Walks_through_every_pending_approval_in_turn()
    {
        await OnboardAgentCommandTests.DraftEverythingAsync(Repo, _ct);
        await File.WriteAllTextAsync(CapabilitiesFile, Capabilities, _ct);

        // Capability, supplier, its review, the not-applicable decision, then the system with its default domain and perimeter.
        var (exit, output, error) = await ApproveAsync("Sam Example\ny\ny\nok\ny\ny\n\n\n");

        exit.ShouldBe(ExitCodes.Ok, error);
        string[] asked = ["[capability] EX", "[supplier] SUP-EXAMPLE", "[review] EA-SUP-EXAMPLE-2026-12", "[not applicable] EXAMPLE-ASSETS EX-2", "[system] EXAMPLE-ASSETS"];
        asked.Select(a => error.IndexOf(a, StringComparison.Ordinal)).ShouldBeInOrder(SortDirection.Ascending);
        asked.ShouldAllBe(a => error.Contains(a, StringComparison.Ordinal));
        error.ShouldContain("Only the supplier administers it.");

        var capabilities = await File.ReadAllTextAsync(CapabilitiesFile, _ct);
        capabilities.ShouldContain("approved_by: Sam Example");
        capabilities.ShouldContain("approved_on: \"2026-11-23\"");

        var supplier = ScopeLoader.Parse(
            new ScopeSource("setup.yaml", "organisation:\n  root_folder: Global\n"), null, [], thirdParties: [new ScopeSource(SupplierFile, await File.ReadAllTextAsync(SupplierFile, _ct))]);
        supplier.Problems.ShouldBeEmpty();
        var entity = supplier.Scope.ThirdParties.ShouldHaveSingleItem();
        var contract = entity.Contracts.ShouldHaveSingleItem();
        (contract.Status, contract.ApprovedBy, contract.ApprovedOn).ShouldBe(("active", "Sam Example", "2026-11-23"));
        var conclusion = entity.Assessments.ShouldHaveSingleItem().Conclusions.ShouldHaveSingleItem();
        (conclusion.Review, conclusion.Result, conclusion.By, conclusion.On).ShouldBe(("EA-SUP-EXAMPLE-2026-12", "ok", "Sam Example", "2026-11-23"));

        File.Exists(Path.Combine(Repo, "scope", "audits", "example-assets-example-framework.yaml")).ShouldBeTrue();
        output.ShouldContain("Approved 5 of 5");
        output.ShouldContain("hound scope plan");

        // Nothing is left: a second run asks nothing.
        (exit, output, error) = await ApproveAsync("");
        exit.ShouldBe(ExitCodes.Ok, error);
        output.ShouldContain("Nothing is waiting for approval.");
    }

    [Fact]
    public async Task What_a_person_declines_stays_a_draft_and_holds_back_what_depends_on_it()
    {
        await OnboardAgentCommandTests.DraftEverythingAsync(Repo, _ct);

        // The supplier is declined: its review is not asked, and the system waits for its supplier. The not-applicable decision is declined too.
        var (exit, output, error) = await ApproveAsync("Sam Example\nn\nn\n");

        exit.ShouldBe(ExitCodes.Ok, error);
        (await File.ReadAllTextAsync(SupplierFile, _ct)).ShouldContain("status: draft");
        error.ShouldNotContain("[review]");
        error.ShouldNotContain("[system]");
        output.ShouldContain("EXAMPLE-ASSETS waits for SUP-EXAMPLE to be approved");
        output.ShouldContain("Approved 0 of 2");
        File.Exists(Path.Combine(Repo, "scope", "audits", "example-assets-example-framework.yaml")).ShouldBeFalse();

        // A review concluded as a blocker is recorded, and the supplier's system is then held back by the plan's gate, not by approve.
        (exit, _, error) = await ApproveAsync("Sam Example\ny\nblocker\ns\n");
        exit.ShouldBe(ExitCodes.Ok, error);
        var supplier = await File.ReadAllTextAsync(SupplierFile, _ct);
        supplier.ShouldContain("status: active");
        supplier.ShouldContain("result: blocker");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Repo => Path.Combine(_root, "repo");

    private string CapabilitiesFile => Path.Combine(Repo, "scope", "capabilities.yaml");

    private string SupplierFile => Path.Combine(Repo, "scope", "third-parties", "sup-example.yaml");

    private Task<(int Exit, string Output, string Error)> ApproveAsync(string answers) =>
        OnboardAgentCommandTests.RunAsync(["approve", "--repo", Repo], answers, terminal: true, Path.Combine(_root, "home"), _ct);
}
