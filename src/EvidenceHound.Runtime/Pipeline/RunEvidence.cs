using EvidenceHound.Core.Checks;
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
    public static string IdFor(string collector, string fileName) => throw new NotImplementedException();

    /// <summary>Evidence from the run's manifest, and checks whose file-name references become evidence IDs.</summary>
    public static Result<CollectorResults> From(CollectedRun run) => throw new NotImplementedException();

    /// <summary>Reads evidence content from the run's collected files, held in memory.</summary>
    public static IEvidenceFiles Files(CollectedRun run) => throw new NotImplementedException();
}
