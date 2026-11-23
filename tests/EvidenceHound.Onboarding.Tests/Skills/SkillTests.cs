using EvidenceHound.Onboarding.Skills;

namespace EvidenceHound.Onboarding.Tests.Skills;

internal static class SkillText
{
    public static string Of(string name, string appliesTo = "[applicability]", string extra = "", string body = "Give a reason for every decision.") =>
        $"---\nname: {name}\ndescription: Example guidance for tests.\nversion: 1.0.0\napplies_to: {appliesTo}\n{extra}---\n{body}\n";

    public static Skill Parse(string text, SkillSource source = SkillSource.Organisation, string path = "skills/example.md") =>
        SkillParser.Parse(text, path, source).Value();
}

public class SkillParserTests
{
    [Fact]
    public void Reads_frontmatter_and_body()
    {
        var skill = SkillText.Parse(SkillText.Of("example-check", "[applicability, mapping]", "frameworks: [essential-eight-2023]\noverrides: applicability\n", "# Heading\n\nGive a reason."));

        skill.Name.ShouldBe("example-check");
        skill.Description.ShouldBe("Example guidance for tests.");
        skill.Version.ShouldBe("1.0.0");
        skill.AppliesTo.ShouldBe([SkillStep.Applicability, SkillStep.Mapping]);
        skill.Frameworks.ShouldBe(["essential-eight-2023"]);
        skill.Overrides.ShouldBe("applicability");
        skill.Body.ShouldBe("# Heading\n\nGive a reason.");
        skill.Source.ShouldBe(SkillSource.Organisation);
        skill.Sha256.Length.ShouldBe(64);
    }
}

public class SkillValidationTests
{
    [Fact]
    public void Unknown_step_and_missing_fields_are_reported()
    {
        var problems = SkillParser.Parse("---\nname: Example Check\napplies_to: [deploy]\n---\nBody.\n", "skills/bad.md", SkillSource.Organisation).Problems();

        problems.Select(p => p.Code).ShouldBe(["skill.name_invalid", "skill.field_missing", "skill.field_missing", "skill.step_unknown"], ignoreOrder: true);
        problems.ShouldAllBe(p => p.Message.StartsWith("skills/bad.md", StringComparison.Ordinal));
        SkillParser.Parse("No frontmatter.", "skills/none.md", SkillSource.Organisation).Problems().ShouldHaveSingleItem().Code.ShouldBe("skill.frontmatter_missing");
        SkillParser.Parse(SkillText.Of("big", body: new string('x', SkillParser.MaxBodyCharacters + 1)), "skills/big.md", SkillSource.Organisation).Problems()
            .ShouldHaveSingleItem().Code.ShouldBe("skill.too_large");
    }
}

public class SkillCatalogueTests
{
    private static readonly Skill Default = SkillText.Parse(SkillText.Of("applicability"), SkillSource.Default, "default:applicability.md");

    [Fact]
    public void Same_name_without_overrides_is_an_error()
    {
        var problem = SkillCatalogue.Build([Default], [SkillText.Parse(SkillText.Of("applicability"))]).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("skill.name_taken");
        problem.Message.ShouldContain("overrides: applicability");
    }

    [Fact]
    public void Override_replaces_default_and_is_listed()
    {
        var mine = SkillText.Parse(SkillText.Of("our-applicability", extra: "overrides: applicability\n"));

        var catalogue = SkillCatalogue.Build([Default], [mine]).Value();

        catalogue.Skills.ShouldHaveSingleItem().ShouldBe(mine);
        catalogue.Overridden.ShouldBe(new Dictionary<string, string> { ["our-applicability"] = "applicability" });
        SkillCatalogue.Build([Default], [SkillText.Parse(SkillText.Of("x", extra: "overrides: nothing-here\n"))]).Problems().ShouldHaveSingleItem().Code.ShouldBe("skill.override_unknown");
    }

    [Fact]
    public void Defaults_ship_with_hound_and_are_valid()
    {
        var defaults = SkillCatalogue.Defaults().Value();

        defaults.Select(s => s.Name).ShouldBe(
            ["supplier-assessment", "supplier-assurance-documents", "applicability", "essential-eight-ml2-mapping", "profile-authoring", "reading-an-api-specification", "reviewing-a-config-change", "environment-capabilities"],
            ignoreOrder: true);
        defaults.ShouldAllBe(s => s.Source == SkillSource.Default);
    }
}

public class SkillSelectionTests
{
    [Fact]
    public void Selects_by_step_and_framework_within_budget()
    {
        var applicability = SkillText.Parse(SkillText.Of("applicability"), SkillSource.Default);
        var e8 = SkillText.Parse(SkillText.Of("e8", "[applicability, mapping]", "frameworks: [essential-eight-2023]\n"));
        var nist = SkillText.Parse(SkillText.Of("nist", "[applicability]", "frameworks: [nist-800-171]\n"));
        var profile = SkillText.Parse(SkillText.Of("profile", "[profile]"));
        var catalogue = SkillCatalogue.Build([applicability], [e8, nist, profile]).Value();

        SkillSelection.Select(catalogue, SkillStep.Applicability, "essential-eight-2023", 10_000).Value().Select(s => s.Name).ShouldBe(["applicability", "e8"]);

        var over = SkillSelection.Select(catalogue, SkillStep.Applicability, "essential-eight-2023", 40).Problems().ShouldHaveSingleItem();
        over.Code.ShouldBe("skill.budget_exceeded");
        over.Message.ShouldContain("applicability");
        over.Message.ShouldContain("e8");
    }
}
