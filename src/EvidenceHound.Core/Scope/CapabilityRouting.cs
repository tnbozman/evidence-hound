namespace EvidenceHound.Core.Scope;

/// <summary>Which capabilities of <c>scope/capabilities.yaml</c> a collector instance does not answer, and who does. Pure.</summary>
public static class CapabilityRouting
{
    /// <summary>
    /// Each mitigation the map gives to something else than this instance, with what provides it. A mitigation is this instance's when its
    /// route names the instance, or when it names the instance's system (<paramref name="system"/>, the instance's <c>system</c> setting). A
    /// system decision with no route is another product's only when the instance's system is known and not among its systems. Manual evidence
    /// and gaps are never a collector's.
    /// </summary>
    public static IReadOnlyDictionary<string, string> For(ScopeDocument? scope, string instance, string? system)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var routed = new Dictionary<string, string>(StringComparer.Ordinal);
        if (scope?.Capabilities is not { } map)
        {
            return routed;
        }

        foreach (var decision in map.Mitigations)
        {
            var systems = decision.Systems.Select(s => s.Value).ToArray();
            var here = decision.Route == instance || (system is not null && systems.Contains(system, StringComparer.Ordinal));
            var elsewhere = decision.ProvidedBy switch
            {
                CapabilityProvision.Manual => "manual evidence",
                CapabilityProvision.Gap => "no product yet: it is a capability gap",
                _ when here => null,
                _ when decision.Route is not null || system is not null => string.Join(" and ", systems.Select(s => Provider(scope, s))),
                _ => null,
            };
            if (elsewhere is { Length: > 0 })
            {
                routed[decision.Mitigation] = elsewhere;
            }
        }

        return routed;
    }

    private static string Provider(ScopeDocument scope, string system) =>
        scope.Audits.FirstOrDefault(a => a.Systems.Any(s => s.Value == system)) is { } audit ? $"{system} (audit {audit.RefId})" : $"{system} (no audit yet)";
}
