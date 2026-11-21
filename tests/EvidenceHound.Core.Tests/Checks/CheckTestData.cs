using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Tests.Checks;

internal static class CheckTestData
{
    public static readonly DateTimeOffset CollectedAt = new(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);

    public const string Sha256 = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    public static T Value<T>(this Result<T> result)
        where T : notnull =>
        result.Match(value => value, problems => throw new InvalidOperationException(string.Join("; ", problems.Select(p => p.Message))));

    public static IReadOnlyList<Problem> Problems<T>(this Result<T> result)
        where T : notnull =>
        result.Match(_ => [], problems => problems);

    public static CheckId Id(string value) => CheckId.Create(value).Value();
}
