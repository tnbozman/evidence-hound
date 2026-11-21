using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.M365;

/// <summary>The config keys of the Microsoft 365 collector's settings and secrets.</summary>
public static class M365Settings
{
    public const string TenantId = "tenant_id";
    public const string ClientId = "client_id";
    public const string GraphUrl = "graph_url";
    public const string IncludeIdentities = "include_identities";
    public const string BreakGlassAccounts = "break_glass_accounts";
    public const string MfaPartialPercent = "mfa_partial_percent";
    public const string PrivilegedRoles = "privileged_roles";
    public const string ClientCertificate = "client_certificate";
    public const string ClientSecret = "client_secret";
}

/// <summary>A Microsoft 365 collector instance's settings, validated once.</summary>
internal sealed record M365Options(
    string TenantId,
    string ClientId,
    Uri GraphUrl,
    bool IncludeIdentities,
    IReadOnlySet<string> BreakGlass,
    double MfaPartialPercent,
    IReadOnlyList<string> PrivilegedRoles)
{
    public static readonly Uri DefaultGraphUrl = new("https://graph.microsoft.com/");

    public static Result<M365Options> FromSettings(CollectorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var read = new SettingsReader(settings);
        return read.Finish(new M365Options(
            read.Required(M365Settings.TenantId),
            read.Required(M365Settings.ClientId),
            read.Url(M365Settings.GraphUrl, DefaultGraphUrl),
            read.Flag(M365Settings.IncludeIdentities),
            read.List(M365Settings.BreakGlassAccounts).ToHashSet(StringComparer.OrdinalIgnoreCase),
            read.Percent(M365Settings.MfaPartialPercent, 90),
            read.List(M365Settings.PrivilegedRoles) is { Length: > 0 } roles ? roles : M365.PrivilegedRoles.Default));
    }
}
