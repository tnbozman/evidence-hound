using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Scope;

/// <summary>A requirement an audit leaves open because another system meets it: the system, its audit if it has one, and the result there.</summary>
public sealed record OpenDependency(string Audit, string Requirement, string System, string? SystemAudit, string ResultThere);

/// <summary>The open dependencies of a scope: each row with <c>provided_by</c>, per system it names. Pure.</summary>
public static class OpenDependencies
{
    public static IReadOnlyList<OpenDependency> Of(ScopeDocument scope, IReadOnlyList<ScopeTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(targets);
        return
        [
            .. scope.Audits.SelectMany(audit => audit.Assessments
                .Where(a => a.Result != ComplianceResult.NotApplicable)
                .SelectMany(a => a.ProvidedBy.Select(system => Dependency(scope, targets, audit, a, system)))),
        ];
    }

    private static OpenDependency Dependency(ScopeDocument scope, IReadOnlyList<ScopeTarget> targets, ScopeAudit audit, HandAssessment row, string system)
    {
        var requirement = row.Requirement.Key.Value;
        var there = scope.Audits.FirstOrDefault(a => a.RefId != audit.RefId && a.Framework.Value == audit.Framework.Value && a.Systems.Any(s => s.Value == system));
        return there is null
            ? new(audit.RefId, requirement, system, null, "no audit yet")
            : new(audit.RefId, requirement, system, there.RefId, ResultIn(there, targets, requirement));
    }

    private static string ResultIn(ScopeAudit audit, IReadOnlyList<ScopeTarget> targets, string requirement) =>
        audit.Assessments.FirstOrDefault(a => a.Requirement.Key.Value == requirement) is { } row
            ? Names.Of(row.Result)
            : targets.Any(t => t.AuditRefId == audit.RefId && t.Computed.Any(k => k.Value == requirement))
                ? "from collectors"
                : Names.Of(audit.Unlisted?.Result ?? ComplianceResult.NotAssessed);
}
