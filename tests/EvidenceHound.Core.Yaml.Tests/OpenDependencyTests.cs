using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s09-36: a requirement met by another system stays open, and the plan lists what it waits on.</summary>
public class OpenDependencyTests
{
    private static readonly string Waiting = "    status: to_do\n    result: not_assessed\n    provided_by: [Example laptops]\n    observation: Met by Example laptops.";

    private static readonly string Audit = ScopeFixture.Audit.Replace("    status: done\n    result: compliant\n    observation: Reviewed by hand.", Waiting, StringComparison.Ordinal);

    private static readonly string LaptopsAudit = """
        ref_id: LAPTOPS-2026
        name: Example laptops 2026
        framework: urn:example:risk:framework:example-baseline
        domain: Example Pty Ltd
        perimeter: Example baseline
        systems: [Example laptops]
        assessments:
          - ref: EX-1.2
            status: done
            result: compliant
            observation: Reviewed by hand.
        """;

    [Fact]
    public void Open_dependencies_are_listed()
    {
        var scope = Parse(Audit).Scope;

        var dependency = OpenDependencies.Of(scope, []).ShouldHaveSingleItem();

        dependency.ShouldBe(new OpenDependency("EXAMPLE-BASELINE-2026", "EX-1.2", "Example laptops", null, "no audit yet"));
    }

    [Fact]
    public void The_providing_systems_audit_and_its_result_are_named()
    {
        var scope = Parse(Audit, LaptopsAudit).Scope;

        OpenDependencies.Of(scope, []).ShouldHaveSingleItem().ShouldBe(new OpenDependency("EXAMPLE-BASELINE-2026", "EX-1.2", "Example laptops", "LAPTOPS-2026", "compliant"));
    }

    [Fact]
    public void A_result_the_providing_audits_mapping_computes_is_named_as_such()
    {
        var scope = Parse(Audit, LaptopsAudit.Replace("assessments:\n  - ref: EX-1.2\n    status: done\n    result: compliant\n    observation: Reviewed by hand.\n", "", StringComparison.Ordinal)).Scope;
        var target = new ScopeTarget("LAPTOPS-2026", [RequirementKey.Ref("EX-1.2")], [], new SourceLine("hound.yaml", 1));

        OpenDependencies.Of(scope, [target]).ShouldHaveSingleItem().ResultThere.ShouldBe("from collectors");
    }

    private static ScopeParse Parse(params string[] audits) => ScopeLoader.Parse(
        new ScopeSource("scope/setup.yaml", ScopeFixture.Setup),
        new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
        [.. audits.Select((a, i) => new ScopeSource($"scope/audits/audit-{i}.yaml", a))]);
}
