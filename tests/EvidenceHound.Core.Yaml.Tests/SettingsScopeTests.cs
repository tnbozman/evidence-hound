using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary><c>scope/settings.yaml</c>: CISO Assistant's feature flags and general settings, by name, checked against the settings snapshot.</summary>
public class SettingsScopeTests
{
    public const string SettingsPath = "scope/settings.yaml";

    public const string Settings = """
        feature_flags:
          tprm: false
          metrology: true
        general:
          enforce_mfa: true
          currency: "€"
          daily_rate: 500
          risk_matrix_labels: [low, high]
        """;

    [Fact]
    public void Example_settings_parse_as_flags_and_json_values()
    {
        var parsed = Parse(Settings);

        Problems(Settings).ShouldBeEmpty();
        parsed.Scope.Settings.FeatureFlags.Select(f => (f.Name, f.Enabled)).ShouldBe([("tprm", false), ("metrology", true)]);
        parsed.Scope.Settings.General.Select(g => (g.Name, g.Json)).ShouldBe(
            [("enforce_mfa", "true"), ("currency", "\"€\""), ("daily_rate", "500"), ("risk_matrix_labels", "[\"low\",\"high\"]")]);
    }

    [Fact]
    public void Unknown_flag_is_reported()
    {
        var problem = Problems(Settings.Replace("metrology: true", "third_parties: true", StringComparison.Ordinal)).ShouldHaveSingleItem();

        (problem.Code, problem.At).ShouldBe(("settings.flag_unknown", new SourceLine(SettingsPath, 3)));
        problem.Message.ShouldContain("third_parties");
        problem.Hint.ShouldContain("tprm");
    }

    [Fact]
    public void Unknown_general_setting_is_reported()
    {
        var problem = Problems(Settings.Replace("daily_rate: 500", "hourly_rate: 500", StringComparison.Ordinal)).ShouldHaveSingleItem();

        (problem.Code, problem.At.Line).ShouldBe(("settings.setting_unknown", 7));
    }

    [Fact]
    public void A_flag_must_be_true_or_false()
    {
        var problem = Problems(Settings.Replace("tprm: false", "tprm: maybe", StringComparison.Ordinal)).ShouldHaveSingleItem();

        (problem.Code, problem.At.Line).ShouldBe(("scope.value_invalid", 2));
    }

    [Fact]
    public void A_secret_setting_is_rejected_without_showing_its_value()
    {
        var problem = Problems(Settings + "\n  openai_api_key: not-a-real-secret").ShouldHaveSingleItem();

        problem.Code.ShouldBe("scope.secret_in_file");
        problem.Message.ShouldNotContain("not-a-real-secret");
    }

    [Fact]
    public void Disabled_feature_in_use_is_reported()
    {
        var problems = Problems(Settings.Replace("metrology: true", "compliance: false", StringComparison.Ordinal));

        var problem = problems.ShouldHaveSingleItem();
        (problem.Code, problem.At.Line).ShouldBe(("settings.feature_disabled_in_use", 3));
        problem.Message.ShouldBe("feature flag 'compliance' is off, which hides the audits this scope declares (EXAMPLE-BASELINE-2026).");
    }

    private static ScopeParse Parse(string settings) =>
        ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", ScopeFixture.Setup),
            new ScopeSource("scope/controls.yaml", ScopeFixture.Controls),
            [new ScopeSource(ScopeFixture.AuditPath, ScopeFixture.Audit)],
            settings: new ScopeSource(SettingsPath, settings));

    private static IReadOnlyList<ScopeProblem> Problems(string settings)
    {
        var parsed = Parse(settings);
        return [.. parsed.Problems, .. ScopeValidator.Validate(parsed.Scope, ScopeFixture.Snapshots, [])];
    }
}
