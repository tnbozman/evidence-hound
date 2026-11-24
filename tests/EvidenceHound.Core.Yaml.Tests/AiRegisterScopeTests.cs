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
        "          tier: medium\n" +
        "          screened_on: \"2026-03-02\"\n" +
        "          use: customised\n" +
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
        (ai.Tier, ai.ScreenedOn, ai.Use, ai.Oversight, ai.OversightNote).ShouldBe(
            (AiRiskTier.Medium, new DateOnly(2026, 3, 2), AiUse.Customised, AiOversight.HumanOnTheLoop, "The sales lead reviews a monthly sample and can switch scoring off."));
        (ai.ImpactAssessment, ai.ImpactAssessedOn, ai.InUseFrom, ai.LastTestedOn, ai.RetiredOn).ShouldBe(
            (AiImpactAssessment.Approved, new DateOnly(2026, 3, 20), new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 25), (DateOnly?)null));
        parsed.Scope.Setup.Domains.Single().Systems[1].Ai.ShouldBeNull();
    }

    [Theory]
    [InlineData("          tier: medium\n", "", "scope.key_missing")]
    [InlineData("          screened_on: \"2026-03-02\"\n", "", "scope.key_missing")]
    [InlineData("          oversight_note: The sales lead reviews a monthly sample and can switch scoring off.\n", "", "scope.key_missing")]
    [InlineData("tier: medium", "tier: extreme", "scope.value_invalid")]
    [InlineData("use: customised", "use: borrowed", "scope.value_invalid")]
    [InlineData("oversight: human_on_the_loop", "oversight: nobody", "scope.value_invalid")]
    [InlineData("impact_assessment: approved", "impact_assessment: done", "scope.value_invalid")]
    [InlineData("in_use_from: \"2026-04-01\"", "in_use_from: April", "scope.value_invalid")]
    [InlineData("          tier: medium\n", "          tier: medium\n          colour: blue\n", "scope.key_unknown")]
    public void Required_keys_and_values_are_checked(string from, string to, string code)
    {
        Parse(Setup().Replace(from, to, StringComparison.Ordinal)).Problems.Select(p => p.Code).ShouldContain(code);
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
