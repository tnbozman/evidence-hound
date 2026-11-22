using System.Text.RegularExpressions;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Sophos;

/// <summary>The config keys of the Sophos Central collector's settings and secrets.</summary>
public static class SophosSettings
{
    public const string ClientId = "client_id";
    public const string TenantId = "tenant_id";
    public const string IdUrl = "id_url";
    public const string ApiUrl = "api_url";
    public const string IncludeIdentities = "include_identities";
    public const string HealthyPassPercent = "healthy_pass_percent";
    public const string HealthyPartialPercent = "healthy_partial_percent";
    public const string StaleAgentDays = "stale_agent_days";
    public const string SupportedFirewallFirmware = "supported_firewall_firmware";
    public const string ClientSecret = "client_secret";
}

/// <summary>A Sophos collector instance's settings, validated once.</summary>
internal sealed partial record SophosOptions(
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
    public static readonly Uri DefaultIdUrl = new("https://id.sophos.com/");

    public static readonly Uri DefaultApiUrl = new("https://api.central.sophos.com/");

    public static Result<SophosOptions> FromSettings(CollectorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var read = new SettingsReader(settings, SophosCollector.TypeName);
        var clientId = read.Required(SophosSettings.ClientId);
        var pass = read.Percent(SophosSettings.HealthyPassPercent, 95);
        var partial = read.Percent(SophosSettings.HealthyPartialPercent, 80);
        read.Require(partial <= pass, SophosSettings.HealthyPartialPercent, $"{partial} is more than {SophosSettings.HealthyPassPercent} ({pass})");
        read.Required(SophosSettings.SupportedFirewallFirmware);
        var firmware = read.List(SophosSettings.SupportedFirewallFirmware);
        var badFirmware = firmware.FirstOrDefault(f => !Version().IsMatch(f));
        read.Require(badFirmware is null, SophosSettings.SupportedFirewallFirmware, $"'{badFirmware}' is not a firmware version such as 21.0 or 20.0.3");
        return read.Finish(new SophosOptions(
            clientId,
            settings.Find(SophosSettings.TenantId) is { Length: > 0 } tenant ? tenant : null,
            read.Url(SophosSettings.IdUrl, DefaultIdUrl),
            read.Url(SophosSettings.ApiUrl, DefaultApiUrl),
            read.Flag(SophosSettings.IncludeIdentities),
            pass,
            partial,
            read.Count(SophosSettings.StaleAgentDays, 14),
            firmware));
    }

    [GeneratedRegex(@"^[0-9]+(\.[0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Version();
}
