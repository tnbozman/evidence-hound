using System.Text.RegularExpressions;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.M365;

/// <summary>The config keys of the Microsoft 365 collector's settings and secrets.</summary>
public static class M365Settings
{
    public const string TenantId = "tenant_id";
    public const string ClientId = "client_id";
    public const string GraphUrl = "graph_url";
    public const string SignIn = "sign_in";
    public const string IncludeIdentities = "include_identities";
    public const string BreakGlassAccounts = "break_glass_accounts";
    public const string MfaPartialPercent = "mfa_partial_percent";
    public const string PrivilegedRoles = "privileged_roles";
    public const string GlobalAdminMin = "global_admin_min";
    public const string GlobalAdminMax = "global_admin_max";
    public const string CompliantPassPercent = "compliant_pass_percent";
    public const string CompliantPartialPercent = "compliant_partial_percent";
    public const string SupportedWindowsBuilds = "supported_windows_builds";
    public const string ApplicationControlGroups = "application_control_groups";
    public const string ApplicationControlWindowDays = "application_control_window_days";
    public const string DefenderOnboardedPercent = "defender_onboarded_percent";
    public const string ClientCertificate = "client_certificate";
    public const string ClientSecret = "client_secret";
    public const string System = "system";
}

/// <summary>A Microsoft 365 collector instance's settings, validated once.</summary>
internal sealed partial record M365Options(
    string TenantId,
    string ClientId,
    Uri GraphUrl,
    bool IncludeIdentities,
    IReadOnlySet<string> BreakGlass,
    double MfaPartialPercent,
    IReadOnlyList<string> PrivilegedRoles,
    int GlobalAdminMin,
    int GlobalAdminMax,
    double CompliantPassPercent,
    double CompliantPartialPercent,
    IReadOnlyList<string> SupportedWindowsBuilds)
{
    /// <summary><c>app</c> signs in as the app registration; <c>none</c> sends a placeholder token, for the loopback dev server only.</summary>
    public string SignIn { get; init; } = "app";

    /// <summary>The group IDs an enforced application control policy must be assigned to, when none is assigned to all devices.</summary>
    public IReadOnlyList<string> ApplicationControlGroups { get; init; } = [];

    /// <summary>The days of application control events read through advanced hunting, 1 to 30.</summary>
    public int ApplicationControlWindowDays { get; init; } = 30;

    /// <summary>The share of Intune-managed Windows devices onboarded to Defender for Endpoint needed for the events check to pass.</summary>
    public double DefenderOnboardedPercent { get; init; } = 95;

    public static readonly Uri DefaultGraphUrl = new("https://graph.microsoft.com/");

    public static Result<M365Options> FromSettings(CollectorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var read = new SettingsReader(settings, M365Collector.TypeName);
        var adminMin = read.Count(M365Settings.GlobalAdminMin, 2);
        var adminMax = read.Count(M365Settings.GlobalAdminMax, 4);
        read.Require(adminMin <= adminMax, M365Settings.GlobalAdminMin, $"{adminMin} is more than {M365Settings.GlobalAdminMax} ({adminMax})");
        var compliantPass = read.Percent(M365Settings.CompliantPassPercent, 95);
        var compliantPartial = read.Percent(M365Settings.CompliantPartialPercent, 80);
        read.Require(compliantPartial <= compliantPass, M365Settings.CompliantPartialPercent, $"{compliantPartial} is more than {M365Settings.CompliantPassPercent} ({compliantPass})");
        read.Required(M365Settings.SupportedWindowsBuilds);
        var builds = read.List(M365Settings.SupportedWindowsBuilds);
        var badBuild = builds.FirstOrDefault(b => !WindowsBuild().IsMatch(b));
        read.Require(badBuild is null, M365Settings.SupportedWindowsBuilds, $"'{badBuild}' is not a Windows build such as 10.0.26100");
        var signIn = settings.Find(M365Settings.SignIn) ?? "app";
        read.Require(signIn is "app" or "none", M365Settings.SignIn, $"'{signIn}' is not app or none");
        var graphUrl = read.Url(M365Settings.GraphUrl, DefaultGraphUrl);
        var windowDays = read.Count(M365Settings.ApplicationControlWindowDays, 30);
        read.Require(windowDays is >= 1 and <= 30, M365Settings.ApplicationControlWindowDays, $"{windowDays} is not 1 to 30 (advanced hunting keeps 30 days)");
        var onboarded = read.Percent(M365Settings.DefenderOnboardedPercent, 95);
        if (signIn == "none" && !IsLoopback(graphUrl))
        {
            return Result.Fail<M365Options>(new Problem(
                "m365.sign_in_not_loopback",
                $"{M365Collector.TypeName}: {M365Settings.SignIn}: none only reads a dev server on this machine; {M365Settings.GraphUrl} '{graphUrl}' is not on localhost, 127.0.0.1 or [::1]. Remove {M365Settings.SignIn} to sign in as the app registration."));
        }

        return read.Finish(new M365Options(
            read.Required(M365Settings.TenantId),
            read.Required(M365Settings.ClientId),
            graphUrl,
            read.Flag(M365Settings.IncludeIdentities),
            read.List(M365Settings.BreakGlassAccounts).ToHashSet(StringComparer.OrdinalIgnoreCase),
            read.Percent(M365Settings.MfaPartialPercent, 90),
            read.List(M365Settings.PrivilegedRoles) is { Length: > 0 } roles ? roles : M365.PrivilegedRoles.Default,
            adminMin,
            adminMax,
            compliantPass,
            compliantPartial,
            [.. builds.Select(b => string.Join('.', b.Split('.').Take(3)))])
        { SignIn = signIn, ApplicationControlGroups = read.List(M365Settings.ApplicationControlGroups), ApplicationControlWindowDays = windowDays, DefenderOnboardedPercent = onboarded });
    }

    private static bool IsLoopback(Uri url) =>
        url.Scheme is "http" or "https" && (url.IsLoopback && url.Host is "localhost" or "127.0.0.1" or "[::1]");

    [GeneratedRegex(@"^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsBuild();
}
