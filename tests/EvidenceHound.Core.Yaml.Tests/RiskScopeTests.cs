using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s15-02: risk studies in <c>scope/risks/&lt;study&gt;.yaml</c>, checked offline against the matrix and threat snapshots.</summary>
public class RiskScopeTests
{
    [Fact]
    public void Study_and_scenarios_are_read()
    {
        var parsed = RiskFixture.Parse();

        parsed.Problems.ShouldBeEmpty();
        var study = parsed.Scope.RiskStudies.ShouldHaveSingleItem();
        (study.RefId, study.Matrix.Value, study.Domain.Value, study.Perimeter!.Value, study.Status).ShouldBe(
            ("EX-RISK-2026", "urn:example:risk:matrix:example-4x4", "Example Pty Ltd", "Example baseline", "in_progress"));
        study.Scenarios.Select(s => s.RefId).ShouldBe(["R-01", "R-02"]);
        var first = study.Scenarios[0];
        (first.Current.Probability, first.Current.Impact, first.Residual.Probability, first.Treatment).ShouldBe(("Possible", "Major", "Unlikely", "mitigate"));
        first.ExistingControls.ShouldHaveSingleItem().Value.ShouldBe("AC-BACKUP");
        study.Scenarios[1].Residual.ShouldBe(RiskRating.NotRated);
        RiskFixture.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_level_name_is_reported_with_line()
    {
        var study = RiskFixture.Study.Replace("{probability: Unlikely, impact: Moderate}", "{probability: Unlikely, impact: Huge}", StringComparison.Ordinal);

        var problem = RiskFixture.Problems(study).ShouldHaveSingleItem();

        problem.Code.ShouldBe("risk.level_unknown");
        problem.At.ShouldBe(new SourceLine(RiskFixture.Path, RiskFixture.LineOf("impact: Huge", study)));
        problem.Message.ShouldContain("'Huge'");
        problem.Hint.ShouldContain("Minor, Moderate, Major, Severe");
    }

    [Fact]
    public void Residual_above_current_is_reported()
    {
        var study = RiskFixture.Study.Replace("residual: {probability: Unlikely, impact: Major}", "residual: {probability: Likely, impact: Major}", StringComparison.Ordinal);

        var problem = RiskFixture.Problems(study).ShouldHaveSingleItem();

        problem.Code.ShouldBe("risk.residual_above_current");
        problem.Message.ShouldContain("R-01");
        problem.At.Line.ShouldBe(RiskFixture.LineOf("residual: {probability: Likely", study));
    }

    [Fact]
    public void Planned_control_already_active_is_reported()
    {
        // AC-MFA is active in controls.yaml, so planning it is a mistake: it belongs under existing_controls.
        var study = RiskFixture.Study.Replace("planned_controls: []", "planned_controls: [AC-MFA]", StringComparison.Ordinal);

        var problem = RiskFixture.Problems(study).ShouldHaveSingleItem();

        (problem.Code, problem.Warning).ShouldBe(("risk.planned_control_active", true));
        problem.Message.ShouldContain("AC-MFA");
    }

    [Fact]
    public void Unknown_system_threat_control_and_matrix_are_reported()
    {
        var study = RiskFixture.Study
            .Replace("systems: [Example 365]", "systems: [Example mainframe]", StringComparison.Ordinal)
            .Replace("threats: [THR-02]", "threats: [THR-99]", StringComparison.Ordinal)
            .Replace("existing_controls: [AC-BACKUP]", "existing_controls: [AC-NONE]", StringComparison.Ordinal);

        RiskFixture.Problems(study).Select(p => p.Code).Order(StringComparer.Ordinal).ShouldBe(["risk.control_unknown", "risk.system_unknown", "risk.threat_unknown"]);
        RiskFixture.Problems(RiskFixture.Study.Replace("example-4x4", "example-9x9", StringComparison.Ordinal))
            .ShouldHaveSingleItem().Code.ShouldBe("risk.matrix_unknown");
    }

    [Fact]
    public void Not_rated_is_written_as_two_dashes_and_treatment_is_checked()
    {
        var study = RiskFixture.Study.Replace("current: {probability: Unlikely, impact: Moderate}", "current: {probability: \"--\", impact: \"--\"}", StringComparison.Ordinal);

        RiskFixture.Problems(study).ShouldBeEmpty();
        RiskFixture.Parse(RiskFixture.Study.Replace("treatment: accept", "treatment: ignore", StringComparison.Ordinal)).Problems
            .ShouldContain(p => p.Message.Contains("'treatment'", StringComparison.Ordinal));
    }
}
