using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>One instance's parameter values (defaults filled in), its secrets, and whether identities are kept in evidence.</summary>
public sealed record InstanceValues(IReadOnlyDictionary<string, string> Parameters, IReadOnlyDictionary<string, Secret> Secrets, bool IncludeIdentities);
