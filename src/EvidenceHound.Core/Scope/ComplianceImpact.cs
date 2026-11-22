namespace EvidenceHound.Core.Scope;

/// <summary>One implementation group of an audit: requirements compliant before and after the plan, out of the group's total.</summary>
public sealed record ImpactGroup(string Group, int Before, int After, int Total);

/// <summary>A row whose result the plan changes: its requirement, title and parent (the strategy or domain), old and new result, and why.</summary>
public sealed record ImpactRow(string Requirement, string Title, string? Parent, string Old, string New, string Why, bool Regresses);

/// <summary>What a plan does to one audit's compliance.</summary>
public sealed record AuditImpact(string Audit, IReadOnlyList<ImpactGroup> Groups, IReadOnlyList<ImpactRow> Rows)
{
    public int Regressions => Rows.Count(r => r.Regresses);
}

/// <summary>A scope plan's effect on compliance. Pure.</summary>
public static class ComplianceImpact
{
    public static IReadOnlyList<AuditImpact> Of(ScopePlan plan, IReadOnlyList<ScopeObject> declared, IReadOnlyList<ActualObject> actual, ScopeInputs inputs) =>
        throw new NotImplementedException($"{plan}{declared}{actual}{inputs}");
}
