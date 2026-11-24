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
public sealed record SspControl(SspValue RefId, SspValue Name, SspValue Owner, SspValue Status, SspValue Description);

/// <summary>A requirement row of the programme's audit.</summary>
public sealed record SspRequirement(SspValue Ref, SspValue Result, SspValue Observation, SspValue Evidence, SspValue Controls);

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
    IReadOnlyList<SspValue> Sections);
