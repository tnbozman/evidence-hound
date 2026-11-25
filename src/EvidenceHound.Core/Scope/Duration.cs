namespace EvidenceHound.Core.Scope;

/// <summary>A whole number of seconds written as <c>30m</c>, <c>4h</c>, <c>2d</c> or <c>1w</c> (s15-04): recovery objectives and escalation points.</summary>
public readonly record struct Duration(int Seconds)
{
    public static bool TryParse(string? text, out Duration duration) => throw new NotImplementedException();

    public override string ToString() => throw new NotImplementedException();
}
