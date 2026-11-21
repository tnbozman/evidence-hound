namespace EvidenceHound.Core.Mapping;

/// <summary>How a mapping names a requirement: by its <c>ref_id</c> or by its URN.</summary>
public enum RequirementKeyKind
{
    Ref,
    Urn,
}

/// <summary>
/// The name of a framework requirement in a mapping. A URN names exactly one requirement row. A ref can match several rows
/// when a framework repeats it, which is an error unless <see cref="AllRows"/> says to write the same result to every one.
/// </summary>
public sealed record RequirementKey
{
    private RequirementKey(RequirementKeyKind kind, string value, bool allRows)
    {
        Kind = kind;
        Value = value;
        AllRows = allRows;
    }

    public RequirementKeyKind Kind { get; }

    /// <summary>The ref_id or the URN.</summary>
    public string Value { get; }

    /// <summary>For a ref: every row with this ref gets the result (<c>rows: all</c>).</summary>
    public bool AllRows { get; }

    public static RequirementKey Ref(string refId, bool allRows = false) => new(RequirementKeyKind.Ref, refId, allRows);

    public static RequirementKey Urn(string urn) => new(RequirementKeyKind.Urn, urn, false);

    public override string ToString() => Value;
}
