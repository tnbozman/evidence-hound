namespace EvidenceHound.Core.Packaging;

/// <summary>Which stored runs to delete. Pure.</summary>
public static class Retention
{
    /// <summary>Runs started more than <paramref name="days"/> days before <paramref name="now"/>, except the latest complete run, which is always kept.</summary>
    public static IReadOnlyList<StoredRun> RunsToDelete(IReadOnlyList<StoredRun> runs, DateTimeOffset now, int days)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var cutoff = now.AddDays(-days);
        var latestComplete = runs.Where(r => r.Complete).MaxBy(r => r.StartedAt);
        return [.. runs.Where(r => r.StartedAt < cutoff && r != latestComplete).OrderBy(r => r.StartedAt)];
    }

    /// <summary>
    /// As above, but each ended audit period's final run (the latest complete run started by the end of its last day, UTC) is kept for
    /// <paramref name="periodRetentionYears"/> years after the period ended, however old, because it is the evidence behind a closed record.
    /// </summary>
    public static IReadOnlyList<StoredRun> RunsToDelete(IReadOnlyList<StoredRun> runs, DateTimeOffset now, int days, IReadOnlyList<DateOnly> periodEnds, int periodRetentionYears)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(periodEnds);

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var finals = periodEnds
            .Where(end => end < today && end >= today.AddYears(-periodRetentionYears))
            .Select(end => runs.Where(r => r.Complete && DateOnly.FromDateTime(r.StartedAt.UtcDateTime) <= end).MaxBy(r => r.StartedAt))
            .OfType<StoredRun>()
            .ToHashSet();
        return [.. RunsToDelete(runs, now, days).Where(r => !finals.Contains(r))];
    }
}
