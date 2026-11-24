using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s11-12: the register entry reaches CISO Assistant through its own Community Edition features: filtering labels and the description.</summary>
public class AiRegisterPlanTests
{
    [Fact]
    public void Register_entry_becomes_labels_and_description()
    {
        var system = ScopeDesired.From(AiRegisterScopeTests.Parse(AiRegisterScopeTests.Setup()).Scope).Single(o => o.Kind == ScopeKind.System && o.Key == "Example 365");

        system.Fields["labels"].ShouldBe("ai-system, ai-tier-medium, ai-use-customised, ai-oversight-on-the-loop");
        system.Fields["description"].ShouldStartWith("Mail, files and identity.");
        system.Fields["description"].ShouldContain("AI register: medium tier, customised, human on the loop.");
        system.Fields["description"].ShouldContain("Oversight: The sales lead reviews a monthly sample and can switch scoring off.");
        system.Fields["description"].ShouldContain("Screened 2026-03-02; impact assessment approved 2026-03-20; in use from 2026-04-01; last tested 2026-03-25.");
    }

    [Fact]
    public void A_retired_system_is_labelled_retired()
    {
        var setup = AiRegisterScopeTests.Setup().Replace("          last_tested_on:", "          retired_on: \"2026-10-31\"\n          last_tested_on:", StringComparison.Ordinal);

        var system = ScopeDesired.From(AiRegisterScopeTests.Parse(setup).Scope).Single(o => o.Kind == ScopeKind.System && o.Key == "Example 365");

        system.Fields["labels"].ShouldEndWith(", ai-retired");
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
