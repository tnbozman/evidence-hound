using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s11-12: the register entry reaches CISO Assistant through its own Community Edition features: filtering labels and the description.</summary>
public class AiRegisterPlanTests
{
    [Fact]
    public void Register_entry_becomes_labels_and_description()
    {
        var system = ScopeDesired.From(AiRegisterScopeTests.Parse(AiRegisterScopeTests.Setup()).Scope).Single(o => o.Kind == ScopeKind.System && o.Key == "Example 365");

        system.Fields["labels"].ShouldBe("ai-oversight-on-the-loop, ai-screening-elevated, ai-system, ai-use-customised");
        system.Fields["description"].ShouldBe("""
            Mail, files and identity.

            AI register: elevated screening (yes to questions 1, 5), customised, human on the loop.
            Version: 2.1
            Purpose: Point the sales team at the leads most likely to buy.
            Intended uses: Rank new leads; Suggest a follow-up date
            Limitations and prohibited use: Not for pricing or credit decisions.
            Foreseeable misuse: Using scores to judge staff performance.
            Data sources and type: CRM lead records (structured)
            Stakeholders affected: Sales team; Prospective customers
            Oversight: The sales lead reviews a monthly sample and can switch scoring off.
            Registered 2026-03-04; screened 2026-03-02; impact assessment approved 2026-03-20; in use from 2026-04-01; last tested 2026-03-25.
            """);
    }

    [Fact]
    public void A_retired_system_is_labelled_retired()
    {
        var setup = AiRegisterScopeTests.Setup().Replace("          last_tested_on:", "          retired_on: \"2026-10-31\"\n          last_tested_on:", StringComparison.Ordinal);

        var system = ScopeDesired.From(AiRegisterScopeTests.Parse(setup).Scope).Single(o => o.Kind == ScopeKind.System && o.Key == "Example 365");

        system.Fields["labels"].Split(", ").ShouldContain("ai-retired");
        system.Fields["description"].ShouldContain("retired 2026-10-31");
    }

    [Fact]
    public void Systems_without_ai_carry_no_ai_labels()
    {
        var desired = ScopeDesired.From(AiRegisterScopeTests.Parse(ScopeFixture.Setup).Scope);

        desired.Where(o => o.Kind == ScopeKind.System).ShouldAllBe(o => !o.Fields.ContainsKey("labels"));
        desired.Single(o => o.Key == "Example 365").Fields["description"].ShouldBe("Mail, files and identity.");
    }
}
