using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EvidenceHound.Core.Scope;

/// <summary>The marker <c>hound</c> appends to the description of an object it manages, recording the state it wrote. Pure.</summary>
public static partial class ScopeMarker
{
    /// <summary>A fingerprint of the fields, in any order: 16 hex characters of a SHA-256.</summary>
    public static string Fingerprint(IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var text = string.Join('\n', fields.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}={f.Value}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    /// <summary>
    /// The state <c>hound</c> records on an object of this kind: a fingerprint of its fields except its links and an audit's status and lock.
    /// Links are only ever added to, and runs add their own (a collector's health evidence on a control), so they are compared field by field
    /// instead; so are an audit's status and lock, when declared.
    /// </summary>
    public static string State(ScopeKind kind, IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        string[] left = [.. LinkFields(kind), .. kind == ScopeKind.Audit ? AuditLifecycleFields : []];
        return Fingerprint(fields.Where(f => !left.Contains(f.Key, StringComparer.Ordinal)).ToDictionary(StringComparer.Ordinal));
    }

    /// <summary>
    /// An audit's status and lock: only a period audit declares them, and they move on as its period ends and is signed off, so they are
    /// compared only when declared and left out of the state.
    /// </summary>
    public static IReadOnlyList<string> AuditLifecycleFields { get; } = ["status", "locked"];

    /// <summary>The fields of a kind that link it to other objects.</summary>
    public static string[] LinkFields(ScopeKind kind) => kind switch
    {
        ScopeKind.Control => ["systems", "documents"],
        ScopeKind.Assessment => ["controls", "documents"],
        ScopeKind.Document => ["hashes"],
        _ => [],
    };

    /// <summary>The description as written to CISO Assistant: the text, then the marker on its own line.</summary>
    public static string Mark(string? description, string fingerprint) =>
        string.IsNullOrWhiteSpace(description) ? $"[hound scope {fingerprint}]" : $"{description}\n[hound scope {fingerprint}]";

    /// <summary>A description read from CISO Assistant split into its text (null when empty) and its marker's fingerprint (null when none).</summary>
    public static (string? Text, string? Fingerprint) Read(string? description)
    {
        if (description is null || MarkPattern().Match(description) is not { Success: true } match)
        {
            return (NullIfEmpty(description), null);
        }

        return (NullIfEmpty(description[..match.Index]), match.Groups["state"].Value);
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.TrimEnd();

    [GeneratedRegex(@"\n?\[hound scope (?<state>[0-9a-f]{16})\]\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex MarkPattern();
}
