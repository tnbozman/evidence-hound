using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s15-09: a judgement section or role the plan lacks is a gap, like the SSP's.</summary>
public class IrpValidatorTests
{
    [Fact]
    public void Missing_judgement_section_is_a_gap()
    {
        IrpFixture.Build().Gaps.ShouldBeEmpty();

        var withoutLessons = IrpFixture.Irp.Replace("  lessons_learned: irp/lessons-learned.md\n", "", StringComparison.Ordinal);
        IrpFixture.Build(withoutLessons).Gaps.ShouldContain(g => g.Requirement == "section lessons_learned");

        var emptyFile = new Dictionary<string, string>(IrpFixture.Texts, StringComparer.Ordinal) { ["irp/containment.md"] = "  \n" };
        IrpFixture.Build(texts: emptyFile).Gaps.ShouldContain(g => g.Requirement == "section containment" && g.Reason.Contains("irp/containment.md", StringComparison.Ordinal));

        var withoutDeputy = IrpFixture.Irp.Replace("  deputy: alex@example.com\n", "", StringComparison.Ordinal);
        IrpFixture.Build(withoutDeputy).Gaps.ShouldContain(g => g.Requirement == "role deputy");
    }

    [Fact]
    public void Unknown_role_or_section_is_refused()
    {
        var parsed = ScopeLoader.ParseIrp(new ScopeSource("scope/irp.yaml", IrpFixture.Irp.Replace("  deputy:", "  understudy:", StringComparison.Ordinal)));

        parsed.Problems.ShouldContain(p => p.Code == "scope.key_unknown" && p.Message.Contains("understudy", StringComparison.Ordinal));
    }
}
