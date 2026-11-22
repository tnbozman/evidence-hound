using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>One check of one test case: the result the case expects and the result the profile gave.</summary>
public sealed record CheckComparison(string CheckId, string Expected, string Actual, string Observation)
{
    public bool Passed => Expected == Actual;
}

/// <summary>One test case of a profile.</summary>
public sealed record CaseReport(string Name, string Description, IReadOnlyList<CheckComparison> Checks, IReadOnlyList<string> Unrecorded)
{
    public bool Passed => Unrecorded.Count == 0 && Checks.All(c => c.Passed);
}

public static class ProfileTestRunner
{
    public static Task<Result<IReadOnlyList<CaseReport>>> RunAsync(string profilesFolder, string name, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
