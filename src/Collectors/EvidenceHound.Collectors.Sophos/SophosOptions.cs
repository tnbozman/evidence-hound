using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>A Sophos collector instance's settings, validated once.</summary>
internal sealed record SophosOptions(
    string ClientId,
    string? TenantId,
    Uri IdUrl,
    Uri ApiUrl,
    bool IncludeIdentities,
    double HealthyPassPercent,
    double HealthyPartialPercent,
    int StaleAgentDays,
    IReadOnlyList<string> SupportedFirmware)
{
    public static Result<SophosOptions> FromSettings(CollectorSettings settings) => throw new NotImplementedException();
}
