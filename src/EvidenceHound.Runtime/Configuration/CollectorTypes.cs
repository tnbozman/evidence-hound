namespace EvidenceHound.Runtime.Configuration;

/// <summary>The collector types this build knows.</summary>
public static class CollectorTypes
{
    /// <summary>Fixed, fictional results for tests, demos and dry runs.</summary>
    public const string Fake = "fake";

    public static IReadOnlySet<string> BuiltIn { get; } = new HashSet<string>(StringComparer.Ordinal) { Fake };
}
