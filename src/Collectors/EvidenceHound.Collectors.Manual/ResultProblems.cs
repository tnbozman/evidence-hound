using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Manual;

internal static class ResultProblems
{
    /// <summary>The result's problems, or none when it succeeded. For collecting every problem before combining results.</summary>
    public static IReadOnlyList<Problem> ProblemsOf<T>(Result<T> result)
        where T : notnull => result.Match(_ => [], problems => problems);
}
