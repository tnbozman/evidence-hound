using EvidenceHound.Core.Results;

namespace EvidenceHound.CisoAssistant.Tests;

internal static class ResultExtensions
{
    public static T Value<T>(this Result<T> result)
        where T : notnull =>
        result.Match(value => value, problems => throw new InvalidOperationException(string.Join("; ", problems.Select(p => p.Message))));

    public static IReadOnlyList<Problem> Problems<T>(this Result<T> result)
        where T : notnull =>
        result.Match(_ => [], problems => problems);
}
