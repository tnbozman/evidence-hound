using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>A check's result and its observation.</summary>
public sealed record Finding(CheckOutcome Outcome, string Observation);

public static class CheckEvaluator
{
    public static Finding Evaluate(Check check, RowSource source, Renderer renderer, DateTimeOffset now, string? evidenceFile = null) =>
        throw new NotImplementedException();
}
