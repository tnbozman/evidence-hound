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

    public static IReadOnlyList<StoredRun> RunsToDelete(IReadOnlyList<StoredRun> runs, DateTimeOffset now, int days, IReadOnlyList<DateOnly> periodEnds, int periodRetentionYears) =>
        RunsToDelete(runs, now, days);
}
