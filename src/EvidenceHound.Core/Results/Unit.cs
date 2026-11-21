namespace EvidenceHound.Core.Results;

/// <summary>The value of a successful result that carries nothing, like <c>void</c>.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value;
}
