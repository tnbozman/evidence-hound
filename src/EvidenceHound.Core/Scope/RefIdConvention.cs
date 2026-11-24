using System.Text.RegularExpressions;

namespace EvidenceHound.Core.Scope;

/// <summary>The kinds of thing a scope gives a ref ID.</summary>
public enum RefIdKind
{
    Supplier,
    Solution,
    Contract,
    System,
    Control,
    Perimeter,
    EntityAssessment,
    Audit,
    Evidence,
}

/// <summary>How one kind's ref IDs read: the kind as scope problems name it, its prefix (empty for an audit) and an example.</summary>
public sealed record RefIdConvention(RefIdKind Kind, string Name, string Prefix, string Example);

/// <summary>
/// The one table of ref ID conventions, used by every prompt and by scope validation. A ref ID is its kind's prefix, then upper case
/// letters, digits and hyphens. An audit has no prefix: it reads <c>&lt;CODE&gt;-&lt;FRAMEWORK&gt;-&lt;YEAR&gt;</c>, as <c>hound init</c> writes. Pure.
/// </summary>
public static partial class RefIds
{
    public static readonly IReadOnlyList<RefIdConvention> Table =
    [
        new(RefIdKind.Supplier, "third party", "SUP-", "SUP-EXAMPLE"),
        new(RefIdKind.Solution, "solution", "SOL-", "SOL-EXAMPLE-HOSTING"),
        new(RefIdKind.Contract, "contract", "CTR-", "CTR-EXAMPLE-2026"),
        new(RefIdKind.System, "system", "SYS-", "SYS-FIREWALL"),
        new(RefIdKind.Control, "control", "AC-", "AC-BACKUP"),
        new(RefIdKind.Perimeter, "perimeter", "PER-", "PER-CORP"),
        new(RefIdKind.EntityAssessment, "supplier assessment", "EA-", "EA-SUP-EXAMPLE"),
        new(RefIdKind.Audit, "audit", "", "EX-E8-2026"),
        new(RefIdKind.Evidence, "evidence plan row", "EV-", "EV-BACKUP-RESTORE"),
    ];

    public static RefIdConvention For(RefIdKind kind) => Table.First(c => c.Kind == kind);

    public static string Prefix(RefIdKind kind) => For(kind).Prefix;

    /// <summary>Whether <paramref name="refId"/> reads as its kind's convention says.</summary>
    public static bool Follows(RefIdKind kind, string refId)
    {
        ArgumentNullException.ThrowIfNull(refId);
        var prefix = Prefix(kind);
        return kind == RefIdKind.Audit
            ? Rest().IsMatch(refId) && refId.Contains('-', StringComparison.Ordinal)
            : refId.StartsWith(prefix, StringComparison.Ordinal) && Rest().IsMatch(refId[prefix.Length..]);
    }

    /// <summary>
    /// The ref ID a person meant by <paramref name="typed"/>: upper-cased, with the prefix added when only the rest was typed. Null when
    /// what is left is not upper case letters, digits and hyphens.
    /// </summary>
    public static string? Complete(RefIdKind kind, string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);
        var prefix = Prefix(kind);
        var upper = typed.Trim().ToUpperInvariant();
        var full = upper.StartsWith(prefix, StringComparison.Ordinal) ? upper : prefix + upper;
        return Follows(kind, full) ? full : null;
    }

    /// <summary>The nearest ref ID that follows the convention, to suggest as the fix: words joined by hyphens, upper-cased, under the prefix.</summary>
    public static string Suggest(RefIdKind kind, string refId)
    {
        ArgumentNullException.ThrowIfNull(refId);
        var words = string.Join('-', NotAllowed().Split(refId.ToUpperInvariant()).Where(w => w.Length > 0));
        var prefix = Prefix(kind);
        return words.StartsWith(prefix, StringComparison.Ordinal) ? words : prefix + words;
    }

    /// <summary>The rule in a sentence, for a prompt that asks again and for a scope problem.</summary>
    public static string Rule(RefIdKind kind) => kind == RefIdKind.Audit
        ? $"An audit's ref ID is <CODE>-<FRAMEWORK>-<YEAR> in upper case letters, digits and hyphens, such as {For(kind).Example}."
        : $"A {For(kind).Name}'s ref ID is {Prefix(kind)} then upper case letters, digits and hyphens, such as {For(kind).Example}.";

    [GeneratedRegex("^[A-Z0-9]+(-[A-Z0-9]+)*$")]
    private static partial Regex Rest();

    [GeneratedRegex("[^A-Z0-9]+")]
    private static partial Regex NotAllowed();
}
