using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>One instance's parameter values (defaults filled in), its secrets, and whether identities are kept in evidence.</summary>
public sealed record InstanceValues(IReadOnlyDictionary<string, string> Parameters, IReadOnlyDictionary<string, Secret> Secrets, bool IncludeIdentities)
{
    /// <summary>The instance's <c>allow_writes</c>: whether queries the profile marks <c>write: true</c> may run. Off by default.</summary>
    public bool AllowWrites { get; init; }
}
