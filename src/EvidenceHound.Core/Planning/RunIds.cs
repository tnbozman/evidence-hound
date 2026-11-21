using System.Globalization;

namespace EvidenceHound.Core.Planning;

/// <summary>Run IDs: the run's start in UTC, such as <c>20261121T020000Z</c>. Pure.</summary>
public static class RunIds
{
    public static string From(DateTimeOffset startedAt) => startedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
}
