namespace EvidenceHound.Collectors;

/// <summary>A copy of one evidence file from an earlier complete run of the same collector instance.</summary>
public sealed record EarlierFile(DateTimeOffset CollectedAt, byte[] Content);

/// <summary>
/// The collector instance's earlier evidence, as the evidence store still holds it (retention applies). Used by checks that need to
/// know when something was first seen, such as how long a firmware upgrade has been offered; read only, never written.
/// </summary>
public interface IEvidenceHistory
{
    /// <summary>Every earlier complete run's copy of <paramref name="fileName"/>, oldest first; runs without the file are left out.</summary>
    Task<IReadOnlyList<EarlierFile>> EarlierAsync(string fileName, CancellationToken cancellationToken);
}

/// <summary>No earlier evidence: what a collector sees outside a store, such as in profile tests without history.</summary>
public sealed class NoEvidenceHistory : IEvidenceHistory
{
    public static NoEvidenceHistory Instance { get; } = new();

    public Task<IReadOnlyList<EarlierFile>> EarlierAsync(string fileName, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EarlierFile>>([]);
}
