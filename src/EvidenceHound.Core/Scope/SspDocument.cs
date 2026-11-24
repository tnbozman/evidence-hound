namespace EvidenceHound.Core.Scope;

/// <summary>Where a value in the plan came from: an object in CISO Assistant, or a file and line in the repo.</summary>
public sealed record SspSource(string Kind, string Reference)
{
    public static readonly string CisoAssistant = "ciso_assistant";
    public static readonly string Repo = "repo";
}

/// <summary>One value in the plan, with its source; null when the source holds none.</summary>
public sealed record SspValue(string? Value, SspSource Source);

/// <summary>A system in the plan's inventory.</summary>
public sealed record SspSystem(SspValue Name, SspValue Type, SspValue Owner, SspValue Description);

/// <summary>An external service: a supplier's solution that provides a system in the boundary.</summary>
public sealed record SspService(SspValue System, SspValue Solution, SspValue Supplier, SspValue Criticality);

/// <summary>An applied control linked to the programme's audit.</summary>
public sealed record SspControl(SspValue RefId, SspValue Name, SspValue Owner, SspValue Status, SspValue Description)
{
    /// <summary>When the control is due to be in place, for the POA&amp;M (s11-03).</summary>
    public SspValue Eta { get; init; } = new(null, RefId.Source);
}

/// <summary>A requirement row of the programme's audit.</summary>
public sealed record SspRequirement(SspValue Ref, SspValue Result, SspValue Observation, SspValue Evidence, SspValue Controls)
{
    /// <summary>The implementation status mapped from the result (s11-03): Implemented, Partially implemented, Planned, Not implemented or Not applicable.</summary>
    public SspValue Status { get; init; } = new(null, Result.Source);

    /// <summary>The linked controls' descriptions in ref order, then the observation; for a requirement not applicable, its justification.</summary>
    public SspValue Statement { get; init; } = new(null, Result.Source);

    /// <summary>The linked controls' owners.</summary>
    public SspValue Responsible { get; init; } = new(null, Result.Source);

    /// <summary>What is missing for an assessor to accept the row: no statement, no owner, or not applicable without a justification.</summary>
    public IReadOnlyList<string> Gaps { get; init; } = [];
}

/// <summary>A requirement the plan cannot yet state fully, and why.</summary>
public sealed record SspGap(string Requirement, string Reason);

/// <summary>A role and who holds it.</summary>
public sealed record SspRoleValue(string Role, SspValue Holder);

/// <summary>The system security plan as a model: every value with its source, for a profile to lay out (s11-04) and for review (s11-05).</summary>
public sealed record SspDocument(
    string Programme,
    string Profile,
    SspValue SystemName,
    SspValue Version,
    SspValue Framework,
    SspValue Audit,
    SspValue Perimeter,
    IReadOnlyList<SspRoleValue> Roles,
    IReadOnlyList<SspSystem> Inventory,
    IReadOnlyList<SspService> ExternalServices,
    IReadOnlyList<SspControl> Controls,
    IReadOnlyList<SspRequirement> Requirements,
    IReadOnlyList<SspValue> Documents,
    IReadOnlyList<SspValue> Sections)
{
    /// <summary>Every gap, by requirement in the document's order: listed at the front of the draft, and fatal with <c>--strict</c>.</summary>
    public IReadOnlyList<SspGap> Gaps => [.. Requirements.SelectMany(r => r.Gaps.Select(g => new SspGap(r.Ref.Value ?? "", g)))];
}
