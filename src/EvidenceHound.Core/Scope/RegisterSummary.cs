namespace EvidenceHound.Core.Scope;

/// <summary>A programme's register (s11-14): which register in the scope it summarises, and the fields it counts by.</summary>
public sealed record ProgrammeRegister(string Of, IReadOnlyList<string> By, SourceLine At);

/// <summary>One item of a register: its name, its field values, and when it was added and retired.</summary>
public sealed record RegisterItem(string Name, IReadOnlyDictionary<string, string> Values, DateOnly? Added, DateOnly? Retired);

/// <summary>How many current items have a field's value.</summary>
public sealed record RegisterCount(string Field, string Value, int Count);

/// <summary>A register summarised for a period.</summary>
public sealed record RegisterSummary(
    IReadOnlyList<string> By, int Total, IReadOnlyList<RegisterCount> Counts, IReadOnlyList<string> Added, IReadOnlyList<string> Retired, IReadOnlyList<string> Failing)
{
    public static RegisterSummary Of(IEnumerable<RegisterItem> items, IReadOnlyList<string> by, DateOnly from, DateOnly to, IReadOnlySet<string> failing) =>
        new(by, -1, [], [], [], []);
}

/// <summary>The registers a scope keeps.</summary>
public static class RegisterItems
{
    public const string AiSystems = "ai_systems";
    public const string Systems = "systems";

    public static IReadOnlyList<string> Names { get; } = [AiSystems, Systems];

    public static IReadOnlyList<string> Fields(string register) => [];

    public static IReadOnlyList<RegisterItem> Of(ScopeSetup setup, string register) => [];
}
