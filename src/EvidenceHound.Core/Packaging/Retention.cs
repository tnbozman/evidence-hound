namespace EvidenceHound.Core.Packaging;

/// <summary>Which stored runs to delete. Pure.</summary>
public static class Retention
{
    /// <summary>Runs started more than <paramref name="days"/> days before <paramref name="now"/>, except the latest complete run, which is always kept.</summary>
    public static IReadOnlyList<StoredRun> RunsToDelete(IReadOnlyList<StoredRun> runs, DateTimeOffset now, int days) =>
        throw new NotImplementedException();
}
