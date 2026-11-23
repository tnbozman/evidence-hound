using EvidenceHound.Onboarding.Explain;
using EvidenceHound.Onboarding.Skills;

namespace EvidenceHound.Onboarding.Tests.Explain;

/// <summary>Explain uses the skills for what the change touches, from the same catalogue as onboarding; <c>--skill</c> chooses instead.</summary>
public sealed class ExplainSkillTests
{
    private static readonly SkillCatalogue Catalogue = SkillCatalogue.Build(SkillCatalogue.Defaults().Value(), []).Value();

    [Fact]
    public void Skills_follow_what_the_change_touches()
    {
        Names([ChangeTopic.ThirdParty]).ShouldBe(["reviewing-a-config-change", "supplier-assessment", "supplier-assurance-documents"], ignoreOrder: true);
        Names([ChangeTopic.NotApplicable]).ShouldContain("applicability");
        Names([ChangeTopic.NotApplicable]).ShouldNotContain("supplier-assessment");
        Names([ChangeTopic.Profile]).ShouldContain("profile-authoring");
        Names([ChangeTopic.Mapping]).ShouldContain("essential-eight-ml2-mapping");
        Names([ChangeTopic.Audit]).ShouldContain("essential-eight-ml2-mapping");
        Names([]).ShouldBe(["reviewing-a-config-change"]);

        // A mapping skill for another framework is left out.
        ExplainSkills.Choose(Catalogue, [ChangeTopic.Mapping], "other-framework", []).Value().Select(s => s.Name).ShouldNotContain("essential-eight-ml2-mapping");
    }

    [Fact]
    public void Skill_option_overrides_the_choice_and_is_recorded()
    {
        var chosen = ExplainSkills.Choose(Catalogue, [ChangeTopic.ThirdParty], "essential-eight-2023", ["profile-authoring"]).Value();

        chosen.Select(s => s.Name).ShouldBe(["profile-authoring"]);
        var facts = ChangeReader.Read(ExplainTests.Plan, "plan.md").Value();
        Explanation.Render(facts, null, chosen).ShouldContain($"| profile-authoring | default | 1.0.0 | `{chosen[0].Sha256}` |");

        ExplainSkills.Choose(Catalogue, [], null, ["no-such-skill"]).Problems().ShouldHaveSingleItem().Code.ShouldBe("skill.unknown");
    }

    private static IReadOnlyList<string> Names(IReadOnlyList<ChangeTopic> topics) =>
        [.. ExplainSkills.Choose(Catalogue, topics, "essential-eight-2023", []).Value().Select(s => s.Name)];
}
