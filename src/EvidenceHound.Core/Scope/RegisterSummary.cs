namespace EvidenceHound.Core.Scope;

/// <summary>A programme's register (s11-14): which register in the scope it summarises, and the fields it counts by.</summary>
public sealed record ProgrammeRegister(string Of, IReadOnlyList<string> By, SourceLine At);

/// <summary>One item of a register: its name, its field values, and when it was added and retired.</summary>
public sealed record RegisterItem(string Name, IReadOnlyDictionary<string, string> Values, DateOnly? Added, DateOnly? Retired);

/// <summary>How many current items have a field's value.</summary>
public sealed record RegisterCount(string Field, string Value, int Count);

/// <summary>
/// A register summarised for a period, for a programme's report: the items still current at the period's end counted by field, those
/// added and retired in it, and those a failing check names. Nothing here knows which register it is.
/// </summary>
public sealed record RegisterSummary(
    IReadOnlyList<string> By, int Total, IReadOnlyList<RegisterCount> Counts, IReadOnlyList<string> Added, IReadOnlyList<string> Retired, IReadOnlyList<string> Failing)
{
    public static RegisterSummary Of(IEnumerable<RegisterItem> items, IReadOnlyList<string> by, DateOnly from, DateOnly to, IReadOnlySet<string> failing)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(by);
        ArgumentNullException.ThrowIfNull(failing);
        var all = items.ToArray();
        var current = all.Where(i => i.Retired is not { } retired || retired > to).ToArray();
        RegisterCount[] counts =
        [
            .. by.SelectMany(field => current
                .Select(i => i.Values.GetValueOrDefault(field))
                .OfType<string>()
                .GroupBy(value => value, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new RegisterCount(field, g.Key, g.Count()))),
        ];
        return new(
            by,
            current.Length,
            counts,
            [.. all.Where(i => Within(i.Added, from, to)).Select(i => i.Name)],
            [.. all.Where(i => Within(i.Retired, from, to)).Select(i => i.Name)],
            [.. current.Where(i => failing.Contains(i.Name)).Select(i => i.Name)]);
    }

    private static bool Within(DateOnly? date, DateOnly from, DateOnly to) => date is { } d && d >= from && d <= to;
}

/// <summary>The registers a scope keeps: the AI systems (systems with an <c>ai</c> block), and every system.</summary>
public static class RegisterItems
{
    public const string AiSystems = "ai_systems";
    public const string Systems = "systems";

    private static readonly string[] AiFields = ["tier", "use", "oversight"];
    private static readonly string[] SystemFields = ["type"];

    public static IReadOnlyList<string> Names { get; } = [AiSystems, Systems];

    /// <summary>The fields a register can be counted by; none for an unknown register.</summary>
    public static IReadOnlyList<string> Fields(string register) => register switch
    {
        AiSystems => AiFields,
        Systems => SystemFields,
        _ => [],
    };

    public static IReadOnlyList<RegisterItem> Of(ScopeSetup setup, string register)
    {
        ArgumentNullException.ThrowIfNull(setup);
        var systems = setup.Domains.SelectMany(d => d.Systems);
        return register switch
        {
            AiSystems => [.. systems.Where(s => s.Ai is not null).Select(s => AiItem(s.Name, s.Ai!))],
            Systems => [.. systems.Select(s => new RegisterItem(
                s.Name, new Dictionary<string, string>(StringComparer.Ordinal) { ["type"] = s.Type == SystemType.Primary ? "primary" : "supporting" }, null, null))],
            _ => [],
        };
    }

    private static RegisterItem AiItem(string name, AiRegistration ai) => new(
        name,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tier"] = Snake(ai.Tier),
            ["use"] = Snake(ai.Use),
            ["oversight"] = Snake(ai.Oversight),
        },
        ai.InUseFrom,
        ai.RetiredOn);

    private static string Snake<T>(T value)
        where T : struct, Enum =>
        AiRegister.Code(value).Replace('-', '_');
}
