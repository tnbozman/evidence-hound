using System.Globalization;
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

    public static Result<M365Options> FromSettings(CollectorSettings settings) => throw new NotImplementedException();

    /// <summary>A comma-separated setting as a list; empty when not set.</summary>
    internal static string[] List(CollectorSettings settings, string name) =>
        (settings.Find(name) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static Result<double> Percent(CollectorSettings settings, string name, double fallback) =>
        settings.Find(name) is not { } text
            ? Result.Ok(fallback)
            : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 100
                ? Result.Ok(value)
                : Invalid<double>(name, $"'{text}' is not a percentage from 0 to 100");

    internal static Result<int> Count(CollectorSettings settings, string name, int fallback) =>
        settings.Find(name) is not { } text
            ? Result.Ok(fallback)
            : int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? Result.Ok(value)
                : Invalid<int>(name, $"'{text}' is not a whole number of 0 or more");

    internal static Result<T> Invalid<T>(string name, string why)
        where T : notnull => Result.Fail<T>(new Problem("m365.setting_invalid", $"Setting '{name}': {why}."));

    internal static Result<object> Box<T>(Result<T> result)
        where T : notnull => result.Map(value => (object)value);

    internal static T Get<T>(Result<T> result)
        where T : notnull => result.Match(value => value, _ => throw new InvalidOperationException("Read a failed setting."));

    private static Result<string> Required(CollectorSettings settings, string name) =>
        settings.Find(name) is { Length: > 0 } value
            ? Result.Ok(value)
            : Result.Fail<string>(new Problem("m365.setting_missing", $"Setting '{name}' is required."));

    private static Result<bool> Flag(CollectorSettings settings, string name) =>
        settings.Find(name) switch
        {
            null => Result.Ok(false),
            var text when bool.TryParse(text, out var value) => Result.Ok(value),
            var text => Invalid<bool>(name, $"'{text}' is not true or false"),
        };

    private static Result<Uri> GraphUrlOf(CollectorSettings settings)
    {
        var text = settings.Find(M365Settings.GraphUrl);
        if (text is null)
        {
            return Result.Ok(DefaultGraphUrl);
        }

        return Uri.TryCreate(text.EndsWith('/') ? text : text + "/", UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)
            ? Result.Ok(url)
            : Invalid<Uri>(M365Settings.GraphUrl, $"'{text}' is not an http or https address");
    }
}
