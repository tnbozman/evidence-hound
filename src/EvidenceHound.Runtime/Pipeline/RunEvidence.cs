using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Packaging;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Runtime.Pipeline;

/// <summary>A collector run in the planner's terms: checks citing evidence IDs, the evidence and the control statuses.</summary>
public sealed record CollectorResults(IReadOnlyList<CheckResult> Checks, IReadOnlyList<Evidence> Evidence, IReadOnlyList<ControlStatus> ControlStatuses);

/// <summary>Turns a stored collector run into planner input. Pure.</summary>
public static class RunEvidence
{
    /// <summary>The evidence ID (CISO Assistant ref_id) of one of a collector's files: stable across runs, so each run adds a revision.</summary>
    public static string IdFor(string collector, string fileName) => $"{collector}/{fileName}";

    /// <summary>Evidence from the run's manifest, and checks whose file-name references become evidence IDs.</summary>
    public static Result<CollectorResults> From(CollectedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var collector = run.Instance.Name;
        var evidence = Result.Combine(run.Manifest.Files.Select(f =>
            Evidence.Create(IdFor(collector, f.FileName), f.FileName, f.MediaType, f.Sha256, f.SizeBytes, f.CollectedAt)));
        var checks = Result.Combine(run.Output.Checks.Select(c =>
            CheckResult.Create(c.CheckId, c.Outcome, c.Observation, c.EvidenceRefs.Select(name => IdFor(collector, name)), c.CollectedAt, c.Source)));
        return evidence.Bind(e => checks.Map(c => new CollectorResults(c, e, run.Output.ControlStatuses)));
    }

    /// <summary>Reads evidence content from the run's collected files, held in memory.</summary>
    public static IEvidenceFiles Files(CollectedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return Files(run.Output.Files);
    }

    /// <summary>Reads evidence content from files held in memory, by file name.</summary>
    public static IEvidenceFiles Files(IReadOnlyList<CollectedFile> files) =>
        new InMemoryFiles(files.ToDictionary(f => f.FileName, f => f.Content, StringComparer.Ordinal));

    private sealed class InMemoryFiles(Dictionary<string, ReadOnlyMemory<byte>> files) : IEvidenceFiles
    {
        public Task<Result<ReadOnlyMemory<byte>>> ReadAsync(Evidence evidence, CancellationToken cancellationToken) =>
            Task.FromResult(files.TryGetValue(evidence.FileName, out var content)
                ? Result.Ok(content)
                : Result.Fail<ReadOnlyMemory<byte>>(new Problem("evidence.file_not_found", $"Evidence {evidence.Id} is not in this run.")));
    }
}
