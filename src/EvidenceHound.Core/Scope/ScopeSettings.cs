namespace EvidenceHound.Core.Scope;

/// <summary>
/// <c>scope/settings.yaml</c>: the CISO Assistant feature flags and general settings the repo manages, by name. Settings it does not name are
/// CISO Assistant's own and are never changed.
/// </summary>
public sealed record ScopeSettings(IReadOnlyList<ScopeFlag> FeatureFlags, IReadOnlyList<ScopeGeneralSetting> General)
{
    public static ScopeSettings None { get; } = new([], []);
}

/// <summary>A feature flag, on or off.</summary>
public sealed record ScopeFlag(string Name, bool Enabled, SourceLine At);

/// <summary>A general setting, with its value as compact JSON (<c>true</c>, <c>500</c>, <c>"€"</c>, <c>["low","high"]</c>).</summary>
public sealed record ScopeGeneralSetting(string Name, string Json, SourceLine At);
