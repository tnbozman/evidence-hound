using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>
/// s11-12: an AI system is a system in <c>scope/setup.yaml</c> with an <c>ai</c> block, its entry in the AI register. The block is reviewed by
/// pull request; CISO Assistant gets it as filtering labels and in the asset's description (custom fields are an Enterprise feature).
/// </summary>
public class AiRegisterScopeTests
{
    // A system's keys sit at eight spaces in ScopeFixture.Setup.
    private static readonly string Block =
        "        owner: it@example.com\n" +
        "        ai:\n" +
        "          screening: elevated\n" +
        "          screening_yes: [1, 5]\n" +
        "          screened_on: \"2026-03-02\"\n" +
        "          registered_on: \"2026-03-04\"\n" +
        "          version: \"2.1\"\n" +
        "          use: customised\n" +
        "          purpose: Point the sales team at the leads most likely to buy.\n" +
        "          intended_uses: [Rank new leads, Suggest a follow-up date]\n" +
        "          limitations: Not for pricing or credit decisions.\n" +
        "          misuse: Using scores to judge staff performance.\n" +
        "          data_sources: [CRM lead records (structured)]\n" +
        "          stakeholders: [Sales team, Prospective customers]\n" +
        "          oversight: human_on_the_loop\n" +
        "          oversight_note: The sales lead reviews a monthly sample and can switch scoring off.\n" +
        "          impact_assessment: approved\n" +
        "          impact_assessed_on: \"2026-03-20\"\n" +
        "          in_use_from: \"2026-04-01\"\n" +
        "          last_tested_on: \"2026-03-25\"\n" +
        "        tags:\n";

    [Fact]
    public void Ai_block_parses()
    {
        var parsed = Parse(Setup());

        parsed.Problems.ShouldBeEmpty();
        var ai = parsed.Scope.Setup.Domains.Single().Systems[0].Ai.ShouldNotBeNull();
        (ai.Screening, ai.ScreenedOn, ai.Use, ai.Oversight, ai.OversightNote).ShouldBe(
            (AiScreening.Elevated, new DateOnly(2026, 3, 2), AiUse.Customised, AiOversight.HumanOnTheLoop, "The sales lead reviews a monthly sample and can switch scoring off."));
        ai.ScreeningYes.ShouldBe([1, 5]);
        (ai.RegisteredOn, ai.Version, ai.Purpose, ai.Limitations, ai.Misuse).ShouldBe(
            (new DateOnly(2026, 3, 4), "2.1", "Point the sales team at the leads most likely to buy.", "Not for pricing or credit decisions.", "Using scores to judge staff performance."));
        ai.IntendedUses.ShouldBe(["Rank new leads", "Suggest a follow-up date"]);
        ai.DataSources.ShouldBe(["CRM lead records (structured)"]);
        ai.Stakeholders.ShouldBe(["Sales team", "Prospective customers"]);
        (ai.ImpactAssessment, ai.ImpactAssessedOn, ai.InUseFrom, ai.LastTestedOn, ai.RetiredOn).ShouldBe(
            (AiImpactAssessment.Approved, new DateOnly(2026, 3, 20), new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 25), (DateOnly?)null));
        parsed.Scope.Setup.Domains.Single().Systems[1].Ai.ShouldBeNull();
    }

    [Theory]
    [InlineData("          screening: elevated\n", "", "scope.key_missing")]
    [InlineData("          screening_yes: [1, 5]\n", "", "scope.key_missing")]
    [InlineData("          screened_on: \"2026-03-02\"\n", "", "scope.key_missing")]
    [InlineData("          oversight_note: The sales lead reviews a monthly sample and can switch scoring off.\n", "", "scope.key_missing")]
    [InlineData("screening: elevated", "screening: medium", "scope.value_invalid")]
    [InlineData("screening_yes: [1, 5]", "screening_yes: [1, 8]", "scope.value_invalid")]
    [InlineData("use: customised", "use: borrowed", "scope.value_invalid")]
    [InlineData("oversight: human_on_the_loop", "oversight: nobody", "scope.value_invalid")]
    [InlineData("impact_assessment: approved", "impact_assessment: done", "scope.value_invalid")]
    [InlineData("in_use_from: \"2026-04-01\"", "in_use_from: April", "scope.value_invalid")]
    [InlineData("          screening: elevated\n", "          screening: elevated\n          colour: blue\n", "scope.key_unknown")]
    public void Required_keys_and_values_are_checked(string from, string to, string code)
    {
        Parse(Setup().Replace(from, to, StringComparison.Ordinal)).Problems.Select(p => p.Code).ShouldContain(code);
    }

    [Theory]
    [InlineData("screening: normal", "screening_yes: [1, 5]")]
    [InlineData("screening: elevated", "screening_yes: []")]
    public void Screening_outcome_must_agree_with_the_answers(string screening, string yes)
    {
        // s11-11: the National AI Centre's screening questions: any yes needs more governance (elevated), all no is normal.
        var setup = Setup().Replace("screening: elevated", screening, StringComparison.Ordinal).Replace("screening_yes: [1, 5]", yes, StringComparison.Ordinal);

        var problem = Parse(setup).Problems.ShouldHaveSingleItem();

        problem.Code.ShouldBe("ai.screening_disagrees");
        problem.Message.ShouldContain("Example 365");
        problem.Hint.ShouldContain("runbooks/ai-screening.md");
    }

    [Fact]
    public void Unacceptable_can_follow_any_answers()
    {
        var setup = Setup().Replace("screening: elevated", "screening: unacceptable", StringComparison.Ordinal).Replace("screening_yes: [1, 5]", "screening_yes: []", StringComparison.Ordinal);

        Parse(setup).Problems.ShouldBeEmpty();
    }

    [Fact]
    public void Impact_date_needs_an_approved_assessment()
    {
        var problem = Parse(Setup().Replace("impact_assessment: approved", "impact_assessment: in_progress", StringComparison.Ordinal)).Problems.ShouldHaveSingleItem();

        (problem.Code, problem.At.File).ShouldBe(("ai.impact_date_without_approval", "scope/setup.yaml"));
        problem.Message.ShouldContain("Example 365");
    }

    [Fact]
    public void Retired_before_use_is_reported()
    {
        var setup = Setup().Replace("          last_tested_on:", "          retired_on: \"2026-03-01\"\n          last_tested_on:", StringComparison.Ordinal);

        var problem = Parse(setup).Problems.ShouldHaveSingleItem();

        problem.Code.ShouldBe("ai.retired_before_use");
        problem.Message.ShouldContain("2026-03-01");
    }

    [Fact]
    public void Third_party_ai_needs_a_solution()
    {
        var problem = Parse(Setup().Replace("use: customised", "use: third_party", StringComparison.Ordinal)).Problems.ShouldHaveSingleItem();

        problem.Code.ShouldBe("ai.solution_missing");
        problem.Hint.ShouldContain("solution");
    }

    internal static string Setup() => ScopeFixture.Setup.Replace("        owner: it@example.com\n        tags:\n", Block, StringComparison.Ordinal);

    internal static ScopeParse Parse(string setup) =>
        ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)]);
}
