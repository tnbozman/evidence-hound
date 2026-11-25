using System.Globalization;

namespace EvidenceHound.Core.Scope;

/// <summary>A note on a security exception or risk acceptance CISO Assistant holds: its status, or that it expires soon or has expired while approved.</summary>
public sealed record ExceptionNote(string Code, string Kind, string RefId, string Name, string Status, string? Expires, string Detail);

/// <summary>
/// Reviews the security exceptions and risk acceptances CISO Assistant holds (s15-03). Pure. Every one gets an <c>exception.status</c> note,
/// so the plan shows what an approver has decided; an approved one that expires within <see cref="Window"/> days, or has already expired, gets
/// a note asking for renewal or closure. Status comes from the held object's observed <c>status</c> and <c>expiration_date</c>.
/// </summary>
public static class ExceptionReview
{
    /// <summary>How many days before expiry an approved exception or acceptance is listed for renewal.</summary>
    public const int Window = 60;

    private static readonly string[] Approved = ["approved", "accepted"];

    public static IReadOnlyList<ExceptionNote> Notes(IReadOnlyList<ActualObject> held, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(held);
        return [.. held.Where(h => h.Held.Kind is ScopeKind.SecurityException or ScopeKind.RiskAcceptance).SelectMany(h => Notes(h, today))];
    }

    private static IEnumerable<ExceptionNote> Notes(ActualObject held, DateOnly today)
    {
        var kind = held.Held.Kind == ScopeKind.SecurityException ? "security exception" : "risk acceptance";
        var name = held.Held.Fields.GetValueOrDefault("name") ?? held.Held.Key;
        var status = held.Observed.GetValueOrDefault("status") ?? "unknown";
        var expires = held.Observed.GetValueOrDefault("expiration_date");
        ExceptionNote Note(string code, string detail) => new(code, kind, held.Held.Key, name, status, expires, detail);

        yield return Note("exception.status", expires is null ? $"{status}, no expiry" : $"{status}, expires {expires}");
        if (!Approved.Contains(status, StringComparer.Ordinal)
            || !DateOnly.TryParseExact(expires, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            yield break;
        }

        var days = date.DayNumber - today.DayNumber;
        if (days < 0)
        {
            yield return Note("exception.expired_but_approved", $"expired {-days} days ago but is still {status}: renew it, or resolve the gap and close it in CISO Assistant.");
        }
        else if (days <= Window)
        {
            yield return Note("exception.expiring", $"expires in {days} days: renew it, or plan to resolve the gap before then.");
        }
    }
}
