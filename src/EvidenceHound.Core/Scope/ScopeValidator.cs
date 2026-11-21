using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Scope;

/// <summary>A deployment target as scope validation sees it: its audit, the requirements its mapping computes and its controls.</summary>
public sealed record ScopeTarget(string AuditRefId, IReadOnlyList<RequirementKey> Computed, IReadOnlyList<string> ControlRefIds, SourceLine At);

/// <summary>Checks a parsed scope against itself, the framework snapshots and the deployment's targets. Pure.</summary>
public static class ScopeValidator
{
    public static IReadOnlyList<ScopeProblem> Validate(
        ScopeDocument scope, IReadOnlyDictionary<string, FrameworkSnapshot> snapshots, IReadOnlyList<ScopeTarget> targets) =>
        throw new NotImplementedException($"{scope}{snapshots}{targets}");
}
