namespace EvidenceHound.Collectors.Rest.Profiles;

/// <summary>
/// Which hosts a profile may send credentials to. An entry is a host name (<c>api.example.com</c>), a wildcard for its sub-domains
/// (<c>*.example.com</c>) or an address whose host is taken (<c>https://api.example.com/</c>, as a filled-in placeholder gives).
/// Credentials go only over https, except to this machine (for recorded APIs in tests).
/// </summary>
public static class HostRules
{
    public static bool IsAllowed(Uri url, IEnumerable<string> allowedHosts)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(allowedHosts);
        return url.IsAbsoluteUri && IsSecure(url) && allowedHosts.Any(entry => Matches(url, entry));
    }

    /// <summary>True for https, or for http to this machine.</summary>
    public static bool IsSecure(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && url.IsLoopback);
    }

    private static bool Matches(Uri url, string entry)
    {
        var (host, port) = HostOf(entry.Trim());
        if (host.Length == 0 || (port is { } p && p != url.Port))
        {
            return false;
        }

        return host.StartsWith("*.", StringComparison.Ordinal)
            ? url.Host.EndsWith(host[1..], StringComparison.OrdinalIgnoreCase) && url.Host.Length > host.Length - 1
            : string.Equals(url.Host, host, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The host and, when given, the port of an entry.</summary>
    private static (string Host, int? Port) HostOf(string entry)
    {
        if (entry.Contains("://", StringComparison.Ordinal))
        {
            return Uri.TryCreate(entry, UriKind.Absolute, out var url) ? (url.Host, url.IsDefaultPort ? null : url.Port) : ("", null);
        }

        var colon = entry.LastIndexOf(':');
        return colon > 0 && int.TryParse(entry[(colon + 1)..], out var port) ? (entry[..colon], port) : (entry, null);
    }
}
