using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>
/// s11-10: every AI6 sub-practice is filled by a run: from the AI register checks where CISO Assistant holds the facts, and from checks a
/// person attests with a manual source (examples/manual-sources/ai6/) where it does not.
/// </summary>
public class Ai6MappingTests
{
    /// <summary>The register checks the ciso-assistant profile runs (s11-13).</summary>
    private static readonly string[] RegisterChecks =
    [
        "ai.register_complete", "ai.owner_assigned", "ai.screening_recorded", "ai.impact_assessment_before_use", "ai.risks_in_register",
        "ai.human_oversight_documented", "ai.tested_before_use", "ai.test_results_current", "ai.suppliers_assessed", "ai.supplier_contracts_current",
    ];

    private static string Files => Path.Combine(AppContext.BaseDirectory, "files");

    private static LoadedMapping Mapping => MappingLoader.Parse(File.ReadAllText(Path.Combine(Files, "mappings", "ai6.yaml")), "mappings/ai6.yaml").Value();

    private static FrameworkSnapshot Snapshot => FrameworkSnapshotJson.Read(File.ReadAllText(Path.Combine(Files, "frameworks", "ai6.json")), "ai6.json").Value();

    [Fact]
    public void All_requirement_refs_exist_in_framework_snapshot()
    {
        MappingValidator.Validate(Mapping, Snapshot).ShouldBeEmpty();
        Mapping.Mapping.FrameworkUrn.ShouldBe(Snapshot.FrameworkUrn);
    }

    [Fact]
    public void Every_sub_practice_is_mapped_or_manual()
    {
        string[] covered = [.. Mapping.Mapping.Requirements.Select(r => r.RequirementRef), .. Mapping.ManualOnly.Select(m => m.Key.Value)];

        covered.ShouldBe(Snapshot.Nodes.Select(n => n.RefId!), ignoreOrder: true);
    }

    [Fact]
    public void Every_ai_check_is_mapped()
    {
        RegisterChecks.Except(Checks()).ShouldBeEmpty();
    }

    [Fact]
    public void Every_check_is_a_register_check_or_attested_through_a_manual_source()
    {
        var attested = ManualSourceChecks();

        Checks().ShouldBe([.. RegisterChecks, .. attested], ignoreOrder: true);
        attested.ShouldNotBeEmpty();
        attested.ShouldAllBe(c => c.StartsWith("ai.", StringComparison.Ordinal));
    }

    [Fact]
    public void Ai6_rows_from_fixture_check_results()
    {
        // One example AI system is missing its test date: the register is complete, testing before use is not.
        var results = Checks().Select(c => Result(c, c == "ai.tested_before_use" ? CheckOutcome.Fail : CheckOutcome.Pass)).ToList();

        var rows = MappingEngine.ApplyMapping(Mapping.Mapping, results).ToDictionary(r => r.RequirementRef, r => r.Result);

        rows["AI6-4.1"].ShouldBe(ComplianceResult.Compliant);
        rows["AI6-5.1"].ShouldBe(ComplianceResult.NonCompliant);
        rows.Count.ShouldBe(21);
    }

    private static string[] Checks() => [.. Mapping.Mapping.Requirements.SelectMany(r => r.Checks).Select(c => c.Value).Distinct()];

    private static string[] ManualSourceChecks() =>
        [.. Directory.GetFiles(Path.Combine(Files, "manual-sources", "ai6"), "source.yaml", SearchOption.AllDirectories)
            .SelectMany(File.ReadAllLines)
            .Where(l => l.TrimStart().StartsWith("checks:", StringComparison.Ordinal))
            .SelectMany(l => l.Split(':', 2)[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct()];

    private static CheckResult Result(string check, CheckOutcome outcome) =>
        CheckResult.Create(CheckId.Create(check).Value(), outcome, "fixture", [], DateTimeOffset.UnixEpoch, "fixture").Value();
}
