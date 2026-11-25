using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s15-04: business impact analyses in <c>scope/bia/&lt;bia&gt;.yaml</c>, and recovery objectives on systems.</summary>
public class BiaScopeTests
{
    public const string Path = "scope/bia/ex-bia-2026.yaml";

    public const string Bia = """
        ref_id: EX-BIA-2026
        name: Example Pty Ltd business impact analysis 2026
        domain: Example Pty Ltd
        perimeter: Example baseline
        matrix: urn:example:risk:matrix:example-4x4
        version: "1.0"
        status: in_progress
        authors: [it@example.com]
        due_date: "2026-12-31"
        assessments:
          - system: Example 365
            dependencies: [Example laptops]
            controls: [AC-BACKUP]
            recovery: {documented: true, tested: false, targets_met: false}
            observation: Mail and files stop when Example 365 is down.
            thresholds:
              - {after: 4h, impact: Minor, justification: Staff use phones.}
              - {after: 1d, impact: Major, qualifications: [Reputation], justification: Customers notice.}
              - {after: 3d, impact: Severe}
          - system: Example laptops
            thresholds:
              - {after: 1d, impact: Moderate}
        """;

    private static readonly string Setup = ScopeFixture.Setup.Replace(
        "        description: Mail, files and identity.\n",
        "        description: Mail, files and identity.\n        objectives: {confidentiality: 3, availability: 2}\n        recovery: {rto: 4h, rpo: 1d, mtd: 3d}\n",
        StringComparison.Ordinal);

    public static ScopeParse Parse(string bia = Bia, string? setup = null) =>
        ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", setup ?? Setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)],
            bias: [new ScopeSource(Path, bia)]);

    private static IReadOnlyList<ScopeProblem> Problems(string bia = Bia)
    {
        var parsed = Parse(bia);
        return [.. parsed.Problems, .. BiaValidator.Validate(parsed.Scope, RiskFixture.Libraries)];
    }

    [Fact]
    public void Bia_and_recovery_objectives_are_read()
    {
        var scope = Parse().Scope;

        var bia = scope.Bias.ShouldHaveSingleItem();
        (bia.RefId, bia.Matrix.Value, bia.Assessments.Count).ShouldBe(("EX-BIA-2026", "urn:example:risk:matrix:example-4x4", 2));
        var first = bia.Assessments[0];
        (first.Documented, first.Tested, first.TargetsMet).ShouldBe((true, false, false));
        first.Thresholds.Select(t => (t.After.Seconds, t.Impact.Value)).ShouldBe([(14400, "Minor"), (86400, "Major"), (259200, "Severe")]);
        var system = scope.Setup.Domains[0].Systems.Single(s => s.Name == "Example 365");
        system.Objectives.ShouldBe(new Dictionary<string, int> { ["confidentiality"] = 3, ["availability"] = 2 });
        system.Recovery.ToDictionary(r => r.Key, r => r.Value.Seconds).ShouldBe(new Dictionary<string, int> { ["rto"] = 14400, ["rpo"] = 86400, ["mtd"] = 259200 });
        Problems().ShouldBeEmpty();
    }

    [Fact]
    public void Duplicate_system_is_reported()
    {
        var problem = Problems(Bia + "\n  - system: Example 365\n").ShouldHaveSingleItem();

        problem.Code.ShouldBe("bia.system_duplicate");
    }

    [Fact]
    public void Thresholds_must_ascend_and_be_unique()
    {
        Problems(Bia.Replace("{after: 1d, impact: Major", "{after: 4h, impact: Major", StringComparison.Ordinal))
            .Select(p => p.Code).ShouldContain("bia.threshold_order");
        Problems(Bia.Replace("{after: 3d, impact: Severe}", "{after: 2h, impact: Severe}", StringComparison.Ordinal))
            .Select(p => p.Code).ShouldContain("bia.threshold_order");
    }

    [Fact]
    public void Falling_impact_is_reported()
    {
        var problem = Problems(Bia.Replace("{after: 3d, impact: Severe}", "{after: 3d, impact: Moderate}", StringComparison.Ordinal)).ShouldHaveSingleItem();

        problem.Code.ShouldBe("bia.impact_falls");
        problem.Warning.ShouldBeTrue("CISO Assistant carries the highest impact forward, so the file is misleading, not broken");
    }

    [Fact]
    public void Unknown_names_are_reported()
    {
        var problems = Problems(Bia
            .Replace("impact: Minor", "impact: Tiny", StringComparison.Ordinal)
            .Replace("dependencies: [Example laptops]", "dependencies: [Example fax]", StringComparison.Ordinal)
            .Replace("controls: [AC-BACKUP]", "controls: [AC-NONE]", StringComparison.Ordinal)
            .Replace("matrix: urn:example:risk:matrix:example-4x4", "matrix: urn:example:risk:matrix:example-4x4", StringComparison.Ordinal));

        problems.Select(p => p.Code).Order(StringComparer.Ordinal).ShouldBe(["bia.control_unknown", "bia.impact_unknown", "bia.system_unknown"]);
        Parse(setup: Setup.Replace("rto: 4h", "rto: soon", StringComparison.Ordinal)).Problems.ShouldHaveSingleItem().Code.ShouldBe("scope.duration_invalid");
    }

    [Fact]
    public void Bia_objects_are_keyed_by_system_and_point_in_time()
    {
        var declared = ScopeDesired.From(Parse().Scope, ScopeInputs.None with { Libraries = RiskFixture.Libraries });

        declared.Single(o => o.Kind == ScopeKind.Bia).Fields["matrix"].ShouldBe("urn:example:risk:matrix:example-4x4");
        var assessment = declared.Single(o => o.Kind == ScopeKind.AssetAssessment && o.Key == "EX-BIA-2026/Example 365");
        assessment.Fields["recovery_documented"].ShouldBe("true");
        assessment.Fields["dependencies"].ShouldBe("Example laptops");
        var threshold = declared.Single(o => o.Kind == ScopeKind.EscalationThreshold && o.Key == "EX-BIA-2026/Example 365/1d");
        threshold.Fields["impact"].ShouldBe("Major");
        threshold.Levels["quali_impact"].ShouldBe(2);
        threshold.Fields["point_in_time"].ShouldBe("86400");
        var system = declared.Single(o => o.Kind == ScopeKind.System && o.Key == "Example 365");
        system.Fields["objectives"].ShouldBe("availability=2, confidentiality=3");
        system.Fields["recovery"].ShouldBe("mtd=3d, rpo=1d, rto=4h");
    }

    [Fact]
    public void Assessments_wait_for_their_bia_and_systems_and_thresholds_for_their_assessment()
    {
        var declared = ScopeDesired.From(Parse().Scope, ScopeInputs.None with { Libraries = RiskFixture.Libraries });

        ScopeApplier.DependsOn(declared.Single(o => o.Key == "EX-BIA-2026/Example 365" && o.Kind == ScopeKind.AssetAssessment))
            .ShouldBe([(ScopeKind.System, "Example 365"), (ScopeKind.System, "Example laptops"), (ScopeKind.Control, "AC-BACKUP"), (ScopeKind.Bia, "EX-BIA-2026")], ignoreOrder: true);
        ScopeApplier.DependsOn(declared.Single(o => o.Kind == ScopeKind.EscalationThreshold && o.Key == "EX-BIA-2026/Example 365/4h"))
            .ShouldBe([(ScopeKind.AssetAssessment, "EX-BIA-2026/Example 365")]);
    }
}
