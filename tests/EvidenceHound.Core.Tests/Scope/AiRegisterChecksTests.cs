using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Tests.Scope;

/// <summary>s11-13: the AI register entries in the scope files, checked on every run by the <c>ai-register</c> collector.</summary>
public class AiRegisterChecksTests
{
    private static readonly DateOnly Today = new(2026, 11, 24);
    private static readonly SourceLine At = new("scope/setup.yaml", 1);

    private static readonly AiRegisterEntry Chat = new(
        "Example Chat",
        "it@example.com",
        new AiRegistration(AiRiskTier.Low, new(2026, 2, 1), AiUse.ThirdParty, AiOversight.HumanInTheLoop, "Staff read every draft before sending.", At)
        {
            ImpactAssessment = AiImpactAssessment.NotRequired,
            InUseFrom = new(2026, 2, 10),
            LastTestedOn = new(2026, 2, 5),
        });

    private static readonly AiRegisterEntry Scoring = new(
        "Example lead scoring",
        "sales@example.com",
        new AiRegistration(AiRiskTier.Medium, new(2026, 3, 2), AiUse.ThirdParty, AiOversight.HumanOnTheLoop, "The sales lead reviews a monthly sample.", At)
        {
            ImpactAssessment = AiImpactAssessment.Approved,
            ImpactAssessedOn = new(2026, 3, 20),
            InUseFrom = new(2026, 4, 1),
            LastTestedOn = new(2026, 3, 25),
        });

    [Fact]
    public void A_well_kept_register_passes_every_check()
    {
        var findings = AiRegisterChecks.Assess([Chat, Scoring], Today);

        findings.Select(f => f.CheckId).ShouldBe(AiRegisterChecks.CheckIds);
        findings.ShouldAllBe(f => f.Outcome == CheckOutcome.Pass);
    }

    [Fact]
    public void Ai_system_without_owner_fails_owner_assigned()
    {
        var finding = Find(AiRegisterChecks.Assess([Chat, Scoring with { Owner = null }], Today), "ai.owner_assigned");

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain("Example lead scoring");
        finding.Observation.ShouldNotContain("Example Chat");
    }

    [Fact]
    public void Missing_impact_assessment_status_fails_register_complete()
    {
        var finding = Find(AiRegisterChecks.Assess([Chat with { Ai = Chat.Ai with { ImpactAssessment = null } }], Today), "ai.register_complete");

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain("Example Chat");
    }

    [Fact]
    public void Impact_assessment_after_go_live_fails()
    {
        var late = Scoring with { Ai = Scoring.Ai with { ImpactAssessedOn = new(2026, 4, 15) } };

        var finding = Find(AiRegisterChecks.Assess([Chat, late], Today), "ai.impact_assessment_before_use");

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain("Example lead scoring");
        finding.Observation.ShouldContain("2026-04-15");
    }

    [Fact]
    public void Medium_tier_without_approved_assessment_fails()
    {
        var pending = Scoring with { Ai = Scoring.Ai with { ImpactAssessment = AiImpactAssessment.InProgress, ImpactAssessedOn = null } };

        Find(AiRegisterChecks.Assess([pending], Today), "ai.impact_assessment_before_use").Outcome.ShouldBe(CheckOutcome.Fail);
    }

    [Theory]
    [InlineData(AiRiskTier.High, 2026, 5, 23, CheckOutcome.Fail)]
    [InlineData(AiRiskTier.High, 2026, 5, 25, CheckOutcome.Pass)]
    [InlineData(AiRiskTier.Medium, 2025, 11, 23, CheckOutcome.Fail)]
    [InlineData(AiRiskTier.Medium, 2025, 11, 25, CheckOutcome.Pass)]
    [InlineData(AiRiskTier.Low, 2025, 11, 25, CheckOutcome.Pass)]
    public void Stale_test_uses_tier_interval(AiRiskTier tier, int year, int month, int day, CheckOutcome expected)
    {
        var system = Scoring with { Ai = Scoring.Ai with { Tier = tier, LastTestedOn = new(year, month, day) } };

        Find(AiRegisterChecks.Assess([system], Today), "ai.test_results_current").Outcome.ShouldBe(expected);
    }

    [Fact]
    public void Untested_system_fails_tested_before_use_and_test_results_current()
    {
        var untested = Scoring with { Ai = Scoring.Ai with { LastTestedOn = null } };

        var findings = AiRegisterChecks.Assess([Chat, untested], Today);

        Find(findings, "ai.tested_before_use").Outcome.ShouldBe(CheckOutcome.Fail);
        Find(findings, "ai.test_results_current").Outcome.ShouldBe(CheckOutcome.Fail);
        Find(findings, "ai.tested_before_use").Observation.ShouldContain("Example lead scoring");
    }

    [Fact]
    public void Prohibited_system_in_use_fails_screening()
    {
        var prohibited = Chat with { Ai = Chat.Ai with { Tier = AiRiskTier.Prohibited } };

        var finding = Find(AiRegisterChecks.Assess([prohibited, Scoring], Today), "ai.screening_recorded");

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain("prohibited");
    }

    [Fact]
    public void Screened_after_going_into_use_fails_screening()
    {
        var late = Chat with { Ai = Chat.Ai with { ScreenedOn = new(2026, 3, 1) } };

        Find(AiRegisterChecks.Assess([late], Today), "ai.screening_recorded").Outcome.ShouldBe(CheckOutcome.Fail);
    }

    [Fact]
    public void High_tier_out_of_loop_fails_oversight()
    {
        var unwatched = Scoring with { Ai = Scoring.Ai with { Tier = AiRiskTier.High, Oversight = AiOversight.HumanOutOfLoop } };

        var finding = Find(AiRegisterChecks.Assess([Chat, unwatched], Today), "ai.human_oversight_documented");

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldContain("Example lead scoring");
    }

    [Fact]
    public void No_ai_systems_in_use_is_not_applicable()
    {
        var considered = Scoring with { Ai = Scoring.Ai with { InUseFrom = null } };
        var retired = Chat with { Ai = Chat.Ai with { RetiredOn = new(2026, 10, 31) } };

        var findings = AiRegisterChecks.Assess([considered, retired], Today);

        findings.Where(f => f.CheckId is not ("ai.register_complete" or "ai.owner_assigned")).ShouldAllBe(f => f.Outcome == CheckOutcome.NotApplicable);
        AiRegisterChecks.Assess([], Today).ShouldAllBe(f => f.Outcome == CheckOutcome.NotApplicable);
    }

    [Fact]
    public void Systems_not_in_use_still_need_an_owner_and_a_complete_entry()
    {
        var considered = Scoring with { Owner = null, Ai = Scoring.Ai with { InUseFrom = null } };

        Find(AiRegisterChecks.Assess([considered], Today), "ai.owner_assigned").Outcome.ShouldBe(CheckOutcome.Fail);
    }

    [Fact]
    public void Observations_name_the_practice_in_words_and_say_what_to_do()
    {
        var failing = Scoring with { Owner = null, Ai = Scoring.Ai with { LastTestedOn = null } };

        var finding = Find(AiRegisterChecks.Assess([failing], Today), "ai.owner_assigned");

        finding.Observation.ShouldContain("AI6-1.1 Accountable people");
        finding.Observation.ShouldContain("scope/setup.yaml");
        AiRegisterChecks.CheckIds.ShouldAllBe(id => AiRegisterChecks.Practice(id).StartsWith("AI6-", StringComparison.Ordinal));
    }

    [Fact]
    public void Entries_are_the_systems_with_an_ai_block()
    {
        var plain = new ScopeSystem("Example laptops", null, SystemType.Supporting, null, "it@example.com", [], new Dictionary<string, string>(), At);
        var chat = plain with { Name = "Example Chat", Ai = Chat.Ai };
        var setup = new ScopeSetup("Global", [], [new ScopeDomain("Example Pty Ltd", null, [], [plain, chat], At)]);

        AiRegisterChecks.Entries(setup).ShouldBe([new AiRegisterEntry("Example Chat", "it@example.com", Chat.Ai)]);
    }

    [Fact]
    public void Findings_name_the_failing_systems()
    {
        var findings = AiRegisterChecks.Assess([Chat, Scoring with { Owner = null }], Today);

        Find(findings, "ai.owner_assigned").Failing.ShouldBe(["Example lead scoring"]);
        Find(findings, "ai.tested_before_use").Failing.ShouldBeEmpty();
    }

    private static AiRegisterFinding Find(IEnumerable<AiRegisterFinding> findings, string checkId) => findings.Single(f => f.CheckId == checkId);
}
