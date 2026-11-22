using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>Only the flags and settings <c>scope/settings.yaml</c> names are compared and written; the rest are CISO Assistant's.</summary>
public class SettingsPlanTests
{
    [Fact]
    public void Only_named_settings_are_changed()
    {
        var declared = Declared();
        ActualObject[] held =
        [
            Held(ScopeKind.FeatureFlag, "tprm", "true"),
            Held(ScopeKind.FeatureFlag, "metrology", "true"),
            Held(ScopeKind.FeatureFlag, "bia", "false"),
            Held(ScopeKind.Setting, "enforce_mfa", "false"),
            Held(ScopeKind.Setting, "currency", "\"€\""),
            Held(ScopeKind.Setting, "daily_rate", "500"),
            Held(ScopeKind.Setting, "risk_matrix_labels", "[\"low\",\"high\"]"),
            Held(ScopeKind.Setting, "show_get_started", "true"),
        ];

        var plan = ScopePlanner.Plan(declared, held, prune: true);

        plan.Changes.Select(c => (c.Kind, c.Key, c.Action, c.Fields.Single().New)).ShouldBe(
            [(ScopeKind.FeatureFlag, "tprm", ScopeAction.Update, "false"), (ScopeKind.Setting, "enforce_mfa", ScopeAction.Update, "true")]);
        plan.Changes.ShouldAllBe(c => c.State == null);
        plan.Drift.ShouldBeEmpty();
    }

    [Fact]
    public void Flags_come_before_every_other_kind_so_features_are_on_before_their_objects_are_written()
    {
        Enum.GetValues<ScopeKind>().Take(2).ShouldBe([ScopeKind.FeatureFlag, ScopeKind.Setting]);
    }

    [Fact]
    public void A_second_plan_after_applying_is_empty()
    {
        var declared = Declared();
        var held = declared.Where(d => d.Kind is ScopeKind.FeatureFlag or ScopeKind.Setting).Select(d => new ActualObject(d, null)).ToArray();

        ScopePlanner.Plan(declared.Where(d => d.Kind is ScopeKind.FeatureFlag or ScopeKind.Setting).ToArray(), held, prune: true).IsEmpty.ShouldBeTrue();
    }

    private static IReadOnlyList<ScopeObject> Declared()
    {
        var parsed = ScopeLoader.Parse(
            new ScopeSource("scope/setup.yaml", ScopeFixture.Setup),
            null,
            [],
            settings: new ScopeSource(SettingsScopeTests.SettingsPath, SettingsScopeTests.Settings));
        parsed.Problems.ShouldBeEmpty();
        return [.. ScopeDesired.From(parsed.Scope).Where(d => d.Kind is ScopeKind.FeatureFlag or ScopeKind.Setting)];
    }

    private static ActualObject Held(ScopeKind kind, string name, string value) =>
        new(new ScopeObject(kind, name, new Dictionary<string, string>(StringComparer.Ordinal) { ["value"] = value }), null);
}
