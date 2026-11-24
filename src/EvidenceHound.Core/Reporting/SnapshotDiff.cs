using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Reporting;

/// <summary>A requirement row whose result changed between two periods; <see cref="From"/> is null for a row new in the later period.</summary>
public sealed record RequirementChange(string Programme, string Urn, string? RefId, string? ParentUrn, ComplianceResult? From, ComplianceResult To);

/// <summary>An applied control whose status changed; <see cref="From"/> is null for a control new in the later period.</summary>
public sealed record ControlChange(string RefId, string Name, string? From, string To);

/// <summary>s12-04: what changed between two snapshots. Every report is a function of snapshots and their differences.</summary>
public sealed record SnapshotDiff(
    IReadOnlyList<RequirementChange> Requirements,
    IReadOnlyList<ControlChange> Controls,
    IReadOnlyList<AcceptanceItem> NewAcceptances,
    IReadOnlyList<AcceptanceItem> ExpiredAcceptances,
    IReadOnlyList<AcceptanceItem> ClosedAcceptances)
{
    public bool IsEmpty => Requirements.Count + Controls.Count + NewAcceptances.Count + ExpiredAcceptances.Count + ClosedAcceptances.Count == 0;
}

public static class Snapshots
{
    /// <summary>The changes from <paramref name="before"/> to <paramref name="after"/>. Pure: <c>Diff(a, a)</c> is empty.</summary>
    public static SnapshotDiff Diff(PostureSnapshot before, PostureSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var earlier = before.Acceptances.GroupBy(Key).ToDictionary(g => g.Key, g => g.First());
        var later = after.Acceptances.GroupBy(Key).ToDictionary(g => g.Key, g => g.First());
        var expired = after.Acceptances
            .Where(a => ExpiresBy(a, after.Period.End) && !(earlier.TryGetValue(Key(a), out var was) && ExpiresBy(was, before.Period.End)))
            .ToList();
        return new SnapshotDiff(
            RequirementChanges(before, after),
            ControlChanges(before.Controls, after.Controls),
            [.. after.Acceptances.Where(a => a.Active && !(earlier.TryGetValue(Key(a), out var was) && was.Active))],
            expired,
            [.. before.Acceptances.Where(a => a.Active && !(later.TryGetValue(Key(a), out var now) && (now.Active || expired.Contains(now))))]);
    }

    private static List<RequirementChange> RequirementChanges(PostureSnapshot before, PostureSnapshot after)
    {
        var earlier = before.Programmes
            .SelectMany(p => p.Requirements.Select(r => (Key: (p.Id, r.Urn), r.Result)))
            .GroupBy(x => x.Key)
            .ToDictionary(g => g.Key, g => g.First().Result);
        return
        [
            .. after.Programmes.SelectMany(p => p.Requirements
                .Where(r => !earlier.TryGetValue((p.Id, r.Urn), out var was) || was != r.Result)
                .Select(r => new RequirementChange(p.Id, r.Urn, r.RefId, r.ParentUrn, earlier.TryGetValue((p.Id, r.Urn), out var was) ? was : null, r.Result))),
        ];
    }

    private static List<ControlChange> ControlChanges(IReadOnlyList<ControlItem> before, IReadOnlyList<ControlItem> after)
    {
        var earlier = before.GroupBy(c => c.RefId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Status, StringComparer.Ordinal);
        return
        [
            .. after
                .Where(c => !earlier.TryGetValue(c.RefId, out var was) || was != c.Status)
                .Select(c => new ControlChange(c.RefId, c.Name, earlier.GetValueOrDefault(c.RefId), c.Status)),
        ];
    }

    private static (AcceptanceKind, string) Key(AcceptanceItem item) => (item.Kind, item.RefId);

    private static bool ExpiresBy(AcceptanceItem item, DateOnly date) => item.Expires is { } expires && expires <= date;
}
