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

    /// <summary>The systems the control covers, as CISO Assistant holds them (s11-16).</summary>
    public IReadOnlyList<string> Systems { get; init; } = [];

    /// <summary>Who implements the control when <c>controls.yaml</c> says so (s11-16); null to infer it.</summary>
    public SspValue? ResponsibleEntity { get; init; }
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

/// <summary>A named value, such as a hand-written section and its file.</summary>
public sealed record SspNamed(string Name, SspValue Value);

/// <summary>
/// One control in the annex: the control (an ISM control for the Essential Eight), the parent nodes it is listed under (strategies), and its
/// applicability, status, statement, responsible party and evidence, with the POA&amp;M item that carries it when it is not yet met.
/// </summary>
public sealed record SspAnnexRow(
    string Control,
    string Title,
    IReadOnlyList<string> Parents,
    SspValue Applicability,
    SspValue Status,
    SspValue Statement,
    SspValue Responsible,
    SspValue Evidence,
    string? Poam)
{
    /// <summary>The control's full text (s11-16).</summary>
    public string Description { get; init; } = "";

    /// <summary>The implementation groups the control is in: for the Essential Eight, its maturity levels.</summary>
    public IReadOnlyList<string> MaturityLevels { get; init; } = [];

    /// <summary>Policy, Procedure, Technical or Documentation, from the framework's reference controls.</summary>
    public IReadOnlyList<string> ControlTypes { get; init; } = [];

    /// <summary>Who implements the control, in the ASD SSP annex template's terms: Consumer, Shared, Outsourced service provider, Cloud service provider or Not applicable.</summary>
    public SspValue ResponsibleEntity { get; init; } = new(null, Status.Source);

    /// <summary>The implementation status in the ASD SSP annex template's terms, such as Effective or Not assessed.</summary>
    public SspValue AsdStatus { get; init; } = new(null, Status.Source);
}

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
    IReadOnlyList<SspNamed> Sections)
{
    /// <summary>The system's categorisation: for the ISM, its classification (SP 800-18 section 2).</summary>
    public SspValue Classification { get; init; } = new(null, SystemName.Source);

    /// <summary>Operational, under development or undergoing a major modification (SP 800-18 section 7).</summary>
    public SspValue OperationalStatus { get; init; } = new(null, SystemName.Source);

    /// <summary>Major application or general support system (SP 800-18 section 8).</summary>
    public SspValue SystemType { get; init; } = new(null, SystemName.Source);

    /// <summary>The controls annex (s11-04): one row per control in the programme's groups; empty when built without the framework snapshot.</summary>
    public IReadOnlyList<SspAnnexRow> Annex { get; init; } = [];

    /// <summary>Every gap, by requirement in the document's order: listed at the front of the draft, and fatal with <c>--strict</c>.</summary>
    public IReadOnlyList<SspGap> Gaps => [.. Requirements.SelectMany(r => r.Gaps.Select(g => new SspGap(r.Ref.Value ?? "", g)))];
}
