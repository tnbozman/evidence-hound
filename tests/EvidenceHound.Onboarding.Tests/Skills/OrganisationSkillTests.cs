using EvidenceHound.Onboarding.Skills;

namespace EvidenceHound.Onboarding.Tests.Skills;

/// <summary><c>hound skill draft</c>: organisation skills written as templates to fill in, versioned each time they are written again.</summary>
public sealed class OrganisationSkillTests
{
    [Fact]
    public void Redraft_shows_a_diff_and_bumps_the_version()
    {
        var kind = OrganisationSkills.Kinds.Single(k => k.Name == "risk-appetite");

        var first = OrganisationSkills.Template(kind, null);
        var edited = first.Text.Replace("_Answer here._", "We accept no unsupported software.", StringComparison.Ordinal);
        var second = OrganisationSkills.Template(kind, edited);

        first.Version.ShouldBe("1");
        first.Diff.ShouldBeNull();
        first.Text.ShouldContain("\nversion: 1\n");
        second.Version.ShouldBe("2");
        second.Text.ShouldContain("\nversion: 2\n");
        second.Diff.ShouldNotBeNull().ShouldContain("-We accept no unsupported software.");
        second.Diff.ShouldContain("-version: 1");

        // The template asks the questions as headings.
        var template = first;
        foreach (var question in kind.Questions)
        {
            template.Text.ShouldContain($"## {question}");
        }

        SkillParser.Parse(template.Text, "risk-appetite.md", SkillSource.Organisation).Value().Name.ShouldBe("risk-appetite");
    }
}
