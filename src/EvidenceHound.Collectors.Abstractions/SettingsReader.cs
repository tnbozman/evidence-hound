using System.Globalization;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors;

/// <summary>
/// Reads typed settings and keeps every problem it finds, so a config with several mistakes reports them all at once. A value
/// that cannot be read comes back as its fallback; check <see cref="Problems"/> before using the values. Problem codes start with
/// <paramref name="prefix"/>, the collector's type: <c>&lt;prefix&gt;.setting_missing</c> and <c>&lt;prefix&gt;.setting_invalid</c>.
/// </summary>
public sealed class SettingsReader(CollectorSettings settings, string prefix)
{
    private readonly List<Problem> _problems = [];

    public IReadOnlyList<Problem> Problems => _problems;

    public Result<T> Finish<T>(T value)
        where T : notnull => _problems.Count == 0 ? Result.Ok(value) : Result.Fail<T>(_problems);

    public string Required(string name)
    {
        if (settings.Find(name) is { Length: > 0 } value)
        {
            return value;
        }

        _problems.Add(new Problem($"{prefix}.setting_missing", $"Setting '{name}' is required."));
        return string.Empty;
    }

    /// <summary>A comma-separated setting as a list; empty when not set.</summary>
    public string[] List(string name) =>
        (settings.Find(name) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool Flag(string name) => settings.Find(name) switch
    {
        null => false,
        var text when bool.TryParse(text, out var value) => value,
        var text => Invalid(name, $"'{text}' is not true or false", false),
    };

    public double Percent(string name, double fallback) => settings.Find(name) switch
    {
        null => fallback,
        var text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 100 => value,
        var text => Invalid(name, $"'{text}' is not a percentage from 0 to 100", fallback),
    };

    public int Count(string name, int fallback) => settings.Find(name) switch
    {
        null => fallback,
        var text when int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) => value,
        var text => Invalid(name, $"'{text}' is not a whole number of 0 or more", fallback),
    };

    /// <summary>An http or https address, always ending in a slash so relative paths resolve beneath it.</summary>
    public Uri Url(string name, Uri fallback) => settings.Find(name) switch
    {
        null => fallback,
        var text when Uri.TryCreate(text.EndsWith('/') ? text : text + "/", UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp) => url,
        var text => Invalid(name, $"'{text}' is not an http or https address", fallback),
    };

    /// <summary>Records <paramref name="why"/> against <paramref name="name"/> unless <paramref name="valid"/>.</summary>
    public void Require(bool valid, string name, string why)
    {
        if (!valid)
        {
            Invalid(name, why, 0);
        }
    }

    private T Invalid<T>(string name, string why, T fallback)
    {
        _problems.Add(new Problem($"{prefix}.setting_invalid", $"Setting '{name}': {why}."));
        return fallback;
    }
}
