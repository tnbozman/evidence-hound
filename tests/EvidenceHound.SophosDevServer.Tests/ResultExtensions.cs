using EvidenceHound.Core.Results;

namespace EvidenceHound.SophosDevServer.Tests;

internal static class ResultExtensions
{
    public static T Value<T>(this Result<T> result)
        where T : notnull =>
        result.Match(value => value, problems => throw new ShouldAssertException($"Expected success, got: {string.Join("; ", problems.Select(p => $"{p.Code}: {p.Message}"))}"));
}
