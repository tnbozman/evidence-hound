namespace EvidenceHound.Core.Scope;

/// <summary>
/// Checks <c>scope/settings.yaml</c> against the settings snapshot: every flag and setting it names must exist in the pinned CISO Assistant
/// release, and no flag it turns off may hide something the scope declares. Pure.
/// </summary>
internal static class SettingsValidator
{
    /// <summary>What each flag hides, as the names of what the scope declares under it; flags not listed hide nothing the scope writes yet.</summary>
    private static readonly Dictionary<string, (string What, Func<ScopeDocument, IEnumerable<string>> Declared)> Hides = new(StringComparer.Ordinal)
    {
        ["compliance"] = ("the audits", scope => scope.Audits.Select(a => a.RefId)),
        ["tprm"] = ("the third parties", scope => scope.ThirdParties.Select(e => e.RefId)),
        ["contracts"] = ("the contracts", scope => scope.ThirdParties.SelectMany(e => e.Contracts).Select(c => c.RefId)),
    };

    public static IEnumerable<ScopeProblem> Validate(ScopeDocument scope, CisoAssistantSettings known)
    {
        var settings = scope.Settings;
        return
        [
            .. settings.FeatureFlags.Where(f => known.GatedFeatureFlags.ContainsKey(f.Name)).Select(f => new ScopeProblem(
                "settings.flag_gated",
                f.At,
                $"CISO Assistant {known.Version} only offers feature flag '{f.Name}' when its backend sets {known.GatedFeatureFlags[f.Name]}, which the stack leaves off.",
                $"Remove '{f.Name}' from settings.yaml.")),
            .. settings.FeatureFlags.Where(f => !known.FeatureFlags.ContainsKey(f.Name) && !known.GatedFeatureFlags.ContainsKey(f.Name)).Select(f => new ScopeProblem(
                "settings.flag_unknown",
                f.At,
                $"CISO Assistant {known.Version} has no feature flag '{f.Name}'.",
                $"Use one of: {string.Join(", ", known.FeatureFlags.Keys.Order(StringComparer.Ordinal))}.")),
            .. settings.General.Where(g => !known.General.Contains(g.Name)).Select(g => new ScopeProblem(
                "settings.setting_unknown",
                g.At,
                $"CISO Assistant {known.Version} has no general setting '{g.Name}'.",
                $"Use one of: {string.Join(", ", known.General.Order(StringComparer.Ordinal))}.")),
            .. settings.FeatureFlags.Where(f => !f.Enabled && Hides.ContainsKey(f.Name)).SelectMany(f => InUse(f, Hides[f.Name], scope)),
        ];
    }

    private static IEnumerable<ScopeProblem> InUse(ScopeFlag flag, (string What, Func<ScopeDocument, IEnumerable<string>> Declared) hides, ScopeDocument scope)
    {
        var declared = hides.Declared(scope).ToArray();
        if (declared.Length > 0)
        {
            yield return new ScopeProblem(
                "settings.feature_disabled_in_use",
                flag.At,
                $"feature flag '{flag.Name}' is off, which hides {hides.What} this scope declares ({string.Join(", ", declared)}).",
                $"Turn '{flag.Name}' on, or remove {hides.What} from the scope first.");
        }
    }
}
