using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s15-02: risk studies and scenarios as the plan compares them, ratings by level name and never risk levels.</summary>
public class RiskPlanTests
{
    private static IReadOnlyList<ScopeObject> Declared() =>
        ScopeDesired.From(RiskFixture.Parse().Scope, ScopeInputs.None with { Libraries = RiskFixture.Libraries });

    [Fact]
    public void Study_and_scenarios_are_declared_with_level_names_and_positions()
    {
        var declared = Declared();

        var study = declared.Single(o => o.Kind == ScopeKind.RiskStudy);
        study.Key.ShouldBe("EX-RISK-2026");
        study.Fields["matrix"].ShouldBe("urn:example:risk:matrix:example-4x4");
        var scenario = declared.Single(o => o.Kind == ScopeKind.RiskScenario && o.Key == "EX-RISK-2026/R-01");
        scenario.Fields["current_probability"].ShouldBe("Possible");
        scenario.Fields["residual_impact"].ShouldBe("Major");
        scenario.Fields["threats"].ShouldBe("urn:example:risk:threat:example-risk:thr-02");
        scenario.Fields.Keys.ShouldNotContain(k => k.EndsWith("_level", StringComparison.Ordinal), "CISO Assistant computes risk levels");
        scenario.Levels.ShouldBe(new Dictionary<string, int> { ["current_proba"] = 2, ["current_impact"] = 2, ["residual_proba"] = 1, ["residual_impact"] = 2 });
        declared.Single(o => o.Key == "EX-RISK-2026/R-02").Fields.Keys.ShouldNotContain("residual_probability", "not rated is left out");
    }

    [Fact]
    public void Rating_change_is_shown_as_level_names()
    {
        var declared = Declared();
        var scenario = declared.Single(o => o.Key == "EX-RISK-2026/R-01");
        var heldFields = new Dictionary<string, string>(scenario.Fields, StringComparer.Ordinal) { ["current_probability"] = "Likely" };
        var held = new ActualObject(scenario with { Fields = heldFields }, ScopeMarker.State(scenario.Kind, heldFields));

        var plan = ScopePlanner.Plan([scenario], [held], prune: false);

        plan.Changes.ShouldHaveSingleItem().Fields.ShouldBe([new FieldChange("current_probability", "Likely", "Possible")]);
    }
}
