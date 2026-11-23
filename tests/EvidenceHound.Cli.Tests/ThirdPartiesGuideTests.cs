namespace EvidenceHound.Cli.Tests;

/// <summary>
/// s10-03: the third-party guide walks a small team, with its agent, from the spreadsheet to the supplier register in the repo, and the
/// example compliance repo carries suppliers and a review that validate.
/// </summary>
public sealed class ThirdPartiesGuideTests
{
    private static string Guide => File.ReadAllText(Path.Combine(ExampleRepoTests.RepoRoot, "docs", "third-parties.md"));

    [Fact]
    public void The_guide_covers_the_formats_validation_plan_apply_privacy_and_what_stays_in_ciso_assistant()
    {
        var guide = Guide;

        guide.ShouldContain("scope.md#third-partiesentityyaml");
        guide.ShouldContain("hound supplier draft");
        guide.ShouldContain("hound approve");
        guide.ShouldContain("hound scope validate");
        guide.ShouldContain("hound scope plan");
        guide.ShouldContain("hound scope apply");
        guide.ShouldContain("## Representatives are personal data");
        guide.ShouldContain("## What stays in CISO Assistant");
        foreach (var kept in new[] { "questionnaire answers", "tier", "campaigns" })
        {
            guide.ShouldContain(kept, Case.Insensitive);
        }
    }

    [Fact]
    public void The_guide_says_what_a_small_team_does_with_its_agent_and_what_only_a_person_does()
    {
        var guide = Guide;

        guide.ShouldContain("## Moving the supplier spreadsheet with your agent");
        guide.ShouldContain("trust centre", Case.Insensitive);
        guide.ShouldContain("documents/suppliers/");
        guide.ShouldContain("assess-a-supplier");
    }

    [Fact]
    public void The_guide_points_at_example_suppliers_with_reviews_and_the_template_turns_contracts_on()
    {
        // The template stays free of suppliers (s09-32): the onboarding tutorial's files, which validate on a copy of it, are the example.
        Guide.ShouldContain("examples/onboarding-tutorial/scope/third-parties/");
        var folder = Path.Combine(ExampleRepoTests.Tutorial, "scope", "third-parties");
        var files = Directory.GetFiles(folder, "*.yaml");

        files.Length.ShouldBeGreaterThanOrEqualTo(2);
        files.Count(f => File.ReadAllText(f).Contains("assessments:", StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(1);
        File.ReadAllText(Path.Combine(ExampleRepoTests.Repo, "scope", "settings.yaml")).ShouldContain("contracts: true");
    }
}
