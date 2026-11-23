using System.Net;
using System.Text.RegularExpressions;
using EvidenceHound.Onboarding.Models;

namespace EvidenceHound.Onboarding.Guard;

/// <summary>What must never leave for a model: the values of the secrets in use, and the organisation's deny terms (its name, domains, projects).</summary>
public sealed record GuardTerms(IReadOnlyDictionary<string, string> Secrets, IReadOnlyList<string> DenyTerms)
{
    public static GuardTerms None { get; } = new(new Dictionary<string, string>(), []);
}

/// <summary>Something the guard found: where (the instructions, an input by name, or the schema) and what, never the matched text itself.</summary>
public sealed record GuardFinding(string Where, string What);

/// <summary>
/// Scans every request before it is sent: secret values in use, deny terms, and IP, MAC and email addresses. Documentation addresses
/// (RFC 5737 and 3849 ranges, loopback, <c>example.com</c>) pass, so fictional material can be sent. Pure; skills cannot change it.
/// </summary>
public static partial class OutboundGuard
{
    public static IReadOnlyList<GuardFinding> Check(DraftRequest request, GuardTerms terms)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(terms);
        IEnumerable<(string Where, string Text)> parts =
        [
            ("instructions", request.Instructions),
            .. request.Inputs.Select(i => (i.Name, i.Content)),
            ("schema", request.Schema.ToJsonString()),
        ];
        return [.. parts.SelectMany(p => Findings(p.Text, terms).Distinct(StringComparer.Ordinal).Select(what => new GuardFinding(p.Where, what)))];
    }

    /// <summary>What in <paramref name="text"/> would identify the organisation, a person or a device. Pure.</summary>
    public static IEnumerable<string> Findings(string text, GuardTerms terms)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(terms);
        foreach (var secret in terms.Secrets.Where(s => s.Value.Length >= 4 && text.Contains(s.Value, StringComparison.Ordinal)))
        {
            yield return $"the value of the secret {secret.Key}";
        }

        foreach (var term in terms.DenyTerms.Where(t => t.Trim().Length > 0 && text.Contains(t.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            yield return $"the deny term '{term.Trim()}'";
        }

        foreach (var what in Identities(text))
        {
            yield return what;
        }
    }

    /// <summary>The kinds of identity pattern in <paramref name="text"/>: email, IP and MAC addresses, outside documentation ranges.</summary>
    public static IEnumerable<string> Identities(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Email().Matches(text).Any(m => !DocumentationDomains.Contains(m.Groups["domain"].Value.ToLowerInvariant())))
        {
            yield return "an email address";
        }

        if (Mac().Matches(text).Any(m => !DocumentationMac(m.Value)))
        {
            yield return "a MAC address";
        }

        if (IPv4().Matches(text).Concat(IPv6().Matches(text)).Any(m => IPAddress.TryParse(m.Value, out var address) && !Documentation(address)))
        {
            yield return "an IP address";
        }
    }

    /// <summary>
    /// <paramref name="text"/> with what the guard would block replaced by placeholders: secret values, deny terms, CISO Assistant object IDs, and
    /// email, IP and MAC addresses outside documentation ranges. For material read by code before a model sees it. Pure.
    /// </summary>
    public static string Redact(string text, GuardTerms terms)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(terms);
        var redacted = terms.Secrets.Values.Where(v => v.Length >= 4).Aggregate(text, (t, secret) => t.Replace(secret, "[secret]", StringComparison.Ordinal));
        redacted = terms.DenyTerms.Select(t => t.Trim()).Where(t => t.Length > 0).Aggregate(redacted, (t, term) => t.Replace(term, "[redacted]", StringComparison.OrdinalIgnoreCase));
        redacted = Uuid().Replace(redacted, "[id]");
        redacted = Email().Replace(redacted, m => DocumentationDomains.Contains(m.Groups["domain"].Value.ToLowerInvariant()) ? m.Value : "[email]");
        redacted = Mac().Replace(redacted, m => DocumentationMac(m.Value) ? m.Value : "[mac]");
        redacted = IPv4().Replace(redacted, m => IPAddress.TryParse(m.Value, out var address) && !Documentation(address) ? "[ip]" : m.Value);
        return IPv6().Replace(redacted, m => IPAddress.TryParse(m.Value, out var address) && !Documentation(address) ? "[ip]" : m.Value);
    }

    private static readonly HashSet<string> DocumentationDomains = ["example.com", "example.org", "example.net"];

    private static readonly (IPAddress Network, int Bits)[] DocumentationRanges =
    [
        (IPAddress.Parse("192.0.2.0"), 24), (IPAddress.Parse("198.51.100.0"), 24), (IPAddress.Parse("203.0.113.0"), 24), (IPAddress.Parse("127.0.0.0"), 8),
        (IPAddress.Parse("0.0.0.0"), 32), (IPAddress.Parse("2001:db8::"), 32), (IPAddress.Parse("::1"), 128), (IPAddress.Parse("::"), 128),
    ];

    private static bool Documentation(IPAddress address) =>
        DocumentationRanges.Any(r => r.Network.AddressFamily == address.AddressFamily && new IPNetwork(r.Network, r.Bits).Contains(address));

    private static bool DocumentationMac(string mac)
    {
        var plain = mac.Replace(":", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
        return plain == "000000000000" || plain.StartsWith("00005E0053", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\b[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Uuid();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@(?<domain>(?:[A-Za-z0-9-]+\.)+[A-Za-z]{2,})", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    [GeneratedRegex(@"(?<![\w:-])(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}(?![\w:-])", RegexOptions.CultureInvariant)]
    private static partial Regex Mac();

    [GeneratedRegex(@"(?<![\d.])(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?!\.?\d)", RegexOptions.CultureInvariant)]
    private static partial Regex IPv4();

    [GeneratedRegex(@"(?<![\w:])(?:(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}|(?:[0-9A-Fa-f]{1,4}:){1,6}:(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,5})?)(?![\w:])", RegexOptions.CultureInvariant)]
    private static partial Regex IPv6();
}
