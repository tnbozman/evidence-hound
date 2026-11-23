namespace EvidenceHound.Core.Scope;

/// <summary>A requirement an audit leaves open because another system meets it: the system, its audit if it has one, and the result there.</summary>
public sealed record OpenDependency(string Audit, string Requirement, string System, string? SystemAudit, string ResultThere);

/// <summary>The open dependencies of a scope. Pure.</summary>
public static class OpenDependencies
{
    public static IReadOnlyList<OpenDependency> Of(ScopeDocument scope, IReadOnlyList<ScopeTarget> targets) => [];
}
