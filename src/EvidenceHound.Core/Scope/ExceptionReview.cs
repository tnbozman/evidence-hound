namespace EvidenceHound.Core.Scope;

/// <summary>A note on a security exception or risk acceptance CISO Assistant holds: its status, or that it expires soon or has expired while approved.</summary>
public sealed record ExceptionNote(string Code, string Kind, string RefId, string Name, string Status, string? Expires, string Detail);

/// <summary>Reviews the security exceptions and risk acceptances CISO Assistant holds (s15-03). Pure.</summary>
public static class ExceptionReview
{
    public static IReadOnlyList<ExceptionNote> Notes(IReadOnlyList<ActualObject> held, DateOnly today) => throw new NotImplementedException();
}
