namespace EvidenceHound.Collectors.M365;

/// <summary>Small helpers for the sentences checks write.</summary>
internal static class Prose
{
    /// <summary>"A", "A and B", "A, B and C".</summary>
    public static string And(IEnumerable<string> items)
    {
        var list = items.ToArray();
        return list.Length <= 1 ? string.Concat(list) : $"{string.Join(", ", list[..^1])} and {list[^1]}";
    }
}
