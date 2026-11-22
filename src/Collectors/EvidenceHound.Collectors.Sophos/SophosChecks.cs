using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>What one check found, citing the lists (by name) it read.</summary>
internal sealed record Finding(string CheckId, CheckOutcome Outcome, string Observation, IReadOnlyList<string> Sources);

/// <summary>One check the collector reports: its ID and the pure function that evaluates it.</summary>
internal sealed record SophosCheck(string Id, Func<SophosData, SophosOptions, DateTimeOffset, Finding> Evaluate);

/// <summary>Endpoint protection and firewall checks over a Sophos Central tenant.</summary>
internal static class SophosChecks
{
    public static IReadOnlyList<SophosCheck> All { get; } = [];

    public static Finding HealthyRatio(SophosData data, SophosOptions options, DateTimeOffset now) => throw new NotImplementedException();

    public static Finding TamperProtection(SophosData data, SophosOptions options, DateTimeOffset now) => throw new NotImplementedException();

    public static Finding StaleAgents(SophosData data, SophosOptions options, DateTimeOffset now) => throw new NotImplementedException();

    public static Finding FirmwareSupported(SophosData data, SophosOptions options, DateTimeOffset now) => throw new NotImplementedException();
}
