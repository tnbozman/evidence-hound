using System.Text.Json.Nodes;
using EvidenceHound.Onboarding.Guard;
using EvidenceHound.Onboarding.Skills;
using EvidenceHound.Onboarding.Tests.Journey;

namespace EvidenceHound.Onboarding.Tests.Skills;

/// <summary>
/// <c>hound skill draft</c>: organisation skills drafted from the person's answers and what the config repo already holds, stripped of
/// anything that identifies the organisation's systems or people, and versioned on every re-draft.
/// </summary>
public sealed class OrganisationSkillTests
{
    internal const string Secret = "sk-test-not-a-real-key";
    private static readonly GuardTerms Terms = new(new Dictionary<string, string> { ["llm_api_key"] = Secret }, []);
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Draft_reads_the_capability_map_systems_and_suppliers()
    {
        var files = new Dictionary<string, string>
        {
            ["scope/setup.yaml"] = "systems:\n  - name: Example 365\n    owner: alex@corp.example.org\n",
            ["scope/third-parties/sup-example.yaml"] = "ref_id: SUP-EXAMPLE\nname: Example Assets Ltd\n",
            ["scope/capabilities.yaml"] = "capabilities:\n  - name: application control\n    provided_by: SYS-APPCTL\n",
        };
        var kind = OrganisationSkills.Kinds.Single(k => k.Name == "it-landscape");
        var model = new ApplicabilityTests.ScriptedModel(_ => new JsonObject { ["body"] = "# IT landscape\n\nExample 365 is the identity provider." });
        SkillAnswer[] answers = [new(kind.Questions[0], "Example 365 for identity and Intune for devices.")];

        var body = (await OrganisationSkills.DraftAsync(model, kind, answers, OrganisationSkills.Material(files, Terms), Terms, _ct)).Value();

        body.ShouldBe("# IT landscape\n\nExample 365 is the identity provider.");
        var request = model.Sent.ShouldHaveSingleItem();
        request.Step.ShouldBe(OrganisationSkills.Step);
        var sent = string.Join('\n', request.Inputs.Select(i => i.Content));
        sent.ShouldContain("Example 365");
        sent.ShouldContain("Example Assets Ltd");
        sent.ShouldContain("application control");
        sent.ShouldContain("Intune for devices");
        sent.ShouldNotContain("alex@corp.example.org");
        OutboundGuard.Check(request, Terms).ShouldBeEmpty();
    }

    [Fact]
    public void Sensitive_content_is_stripped_and_refused()
    {
        var kind = OrganisationSkills.Kinds.Single(k => k.Name == "organisation-profile");
        const string body = "Admins sign in at 10.20.30.40, tenant 3f2b8c1e-9a7d-4e21-8b55-0c6f1d2a9e44, ask alex@corp.example.org. Key " + Secret + ".";

        var draft = OrganisationSkills.Draft(kind, body, null, Terms);

        draft.Text.ShouldNotContain("10.20.30.40");
        draft.Text.ShouldNotContain("3f2b8c1e-9a7d-4e21-8b55-0c6f1d2a9e44");
        draft.Text.ShouldNotContain("alex@corp.example.org");
        draft.Text.ShouldNotContain(Secret);
        var skill = SkillParser.Parse(draft.Text, "organisation-profile.md", SkillSource.Organisation).Value();
        skill.AppliesTo.ShouldBe([SkillStep.Supplier, SkillStep.System, SkillStep.Applicability, SkillStep.Explain]);
        SkillCatalogue.Warnings(skill, []).ShouldBeEmpty();

        // Written by hand instead, the same content is refused.
        var handWritten = SkillParser.Parse(draft.Text.Replace("[ip]", "10.20.30.40", StringComparison.Ordinal), "organisation-profile.md", SkillSource.Organisation).Value();
        SkillCatalogue.Warnings(handWritten, []).ShouldHaveSingleItem().Code.ShouldBe("skill.sensitive_content");
    }

    [Fact]
    public void Redraft_shows_a_diff_and_bumps_the_version()
    {
        var kind = OrganisationSkills.Kinds.Single(k => k.Name == "risk-appetite");

        var first = OrganisationSkills.Draft(kind, "We accept no unsupported software.", null, Terms);
        var second = OrganisationSkills.Draft(kind, "We accept no unsupported software after 30 days.", first.Text, Terms);

        first.Version.ShouldBe("1");
        first.Diff.ShouldBeNull();
        first.Text.ShouldContain("\nversion: 1\n");
        second.Version.ShouldBe("2");
        second.Text.ShouldContain("\nversion: 2\n");
        second.Diff.ShouldNotBeNull().ShouldContain("-We accept no unsupported software.");
        second.Diff.ShouldContain("+We accept no unsupported software after 30 days.");
        second.Diff.ShouldContain("-version: 1");

        // The template for teams without a model asks the questions as headings.
        var template = OrganisationSkills.Template(kind, null);
        template.Version.ShouldBe("1");
        foreach (var question in kind.Questions)
        {
            template.Text.ShouldContain($"## {question}");
        }

        SkillParser.Parse(template.Text, "risk-appetite.md", SkillSource.Organisation).Value().Name.ShouldBe("risk-appetite");
    }
}
