using System.Globalization;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Review;

/// <summary>How soon a piece of work needs doing: past its date, broken, due within the window, or waiting on someone.</summary>
public enum NextUrgency
{
    Overdue,
    Broken,
    DueSoon,
    Waiting,
}

/// <summary>
/// One piece of work for <c>hound next</c>: what it is about, why, the <c>hound</c> command that handles it (and the skill that walks
/// through it), and about how many minutes it takes. Never carries evidence content or a secret.
/// </summary>
public sealed record NextItem(NextUrgency Urgency, string Kind, string Subject, string Detail, string Command, int Minutes)
{
    public DateOnly? Due { get; init; }

    public string? Skill { get; init; }
}

/// <summary>What needs doing now, most urgent first (s18-01). Pure: the shell gathers the facts and passes them in.</summary>
public static class NextWork
{
    /// <summary>How many days before a manual source falls due it is listed.</summary>
    public const int ManualWindowDays = 14;

    /// <summary>How long after a missed occurrence a collector counts as overdue, so a run that is merely late by its jitter is not listed.</summary>
    public static readonly TimeSpan CollectorGrace = TimeSpan.FromHours(6);

    /// <summary>Most urgent first, then the earliest date, then by kind and subject so the order is stable.</summary>
    public static IReadOnlyList<NextItem> Order(IEnumerable<NextItem> items) =>
    [
        .. items.OrderBy(i => i.Urgency)
            .ThenBy(i => i.Due ?? DateOnly.MaxValue)
            .ThenBy(i => i.Kind, StringComparer.Ordinal)
            .ThenBy(i => i.Subject, StringComparer.Ordinal),
    ];

    /// <summary>
    /// The review queue as at most two entries: the session the person runs, and the items the agent still has to recommend on. The
    /// session is overdue when any item is past its deadline.
    /// </summary>
    public static IReadOnlyList<NextItem> Review(IReadOnlyList<ReviewItem> queue, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(queue);
        if (queue.Count == 0)
        {
            return [];
        }

        var minutes = queue.Sum(i => i.Minutes);
        var late = queue.Where(i => i.Deadline is { } d && d < today).Select(i => i.Deadline).Min();
        var session = new NextItem(
            late is null ? NextUrgency.Waiting : NextUrgency.Overdue,
            "review",
            Text($"{queue.Count} item(s) wait on a person, about {minutes} minutes"),
            "The person accepts or challenges each item in one guided session; the agent goes through them with the person first.",
            Text($"hound review --for {RoundUp(minutes)}m"),
            minutes)
        {
            Due = late,
            Skill = "work-through-pending-decisions",
        };
        var bare = queue.Count(i => i.Recommendation is null && i.CoveredBy is null);
        return bare == 0
            ? [session]
            :
            [
                session,
                new NextItem(
                    NextUrgency.Waiting,
                    "recommendation",
                    Text($"{bare} item(s) have no recommendation yet"),
                    "Draft each recommendation with its reason and sources, so the session only asks the person to decide.",
                    "hound review pending --json",
                    bare * 3)
                {
                    Skill = "weekly-review",
                },
            ];
    }

    /// <summary>
    /// A collector that never ran, whose latest run failed, or that missed its schedule by more than <see cref="CollectorGrace"/>; null
    /// when it ran on time and passed. <paramref name="dueAfterLast"/> is its first scheduled occurrence after the latest run.
    /// </summary>
    public static NextItem? Collector(string name, DateTimeOffset? lastRun, bool failed, IReadOnlyList<string> problems, DateTimeOffset? dueAfterLast, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(problems);
        var command = $"hound run --config hound.yaml --only {name}";
        if (lastRun is null)
        {
            return new NextItem(NextUrgency.Broken, "collector", name, "It has never run, so nothing it checks has evidence.", command, 10) { Skill = "operate-collectors" };
        }

        if (failed)
        {
            var why = problems.Count == 0 ? "no reason recorded" : string.Join(" ", problems);
            return new NextItem(NextUrgency.Broken, "collector", name, $"Its latest run failed: {why}", command, 15) { Skill = "operate-collectors" };
        }

        return dueAfterLast is { } due && due + CollectorGrace < now
            ? new NextItem(NextUrgency.Overdue, "collector", name, $"It missed its schedule: due {Day(due)}, last ran {Day(lastRun.Value)}. Check that hound serve is running.", command, 10)
            {
                Due = DateOnly.FromDateTime(due.UtcDateTime),
                Skill = "operate-collectors",
            }
            : null;
    }

    /// <summary>
    /// A manual source whose evidence is overdue, never uploaded, or due within <see cref="ManualWindowDays"/>; null otherwise.
    /// <paramref name="lastEvidence"/> is when its newest file was attested or, unattested, last changed.
    /// </summary>
    public static NextItem? Manual(string source, string owner, int dueDays, DateTimeOffset? lastEvidence, DateOnly today)
    {
        var command = $"hound manual upload <file> --config hound.yaml --source {source}";
        if (lastEvidence is not { } last)
        {
            return new NextItem(NextUrgency.Overdue, "manual_source", source, $"No evidence uploaded yet; due every {dueDays} days from {owner}.", command, 15)
            {
                Due = today,
                Skill = "process-manual-evidence",
            };
        }

        var due = DateOnly.FromDateTime(last.UtcDateTime).AddDays(dueDays);
        return due > today.AddDays(ManualWindowDays)
            ? null
            : new NextItem(
                due < today ? NextUrgency.Overdue : NextUrgency.DueSoon,
                "manual_source",
                source,
                $"{(due < today ? "Overdue since" : "Due by")} {due:yyyy-MM-dd}; due every {dueDays} days from {owner}.",
                command,
                15)
            {
                Due = due,
                Skill = "process-manual-evidence",
            };
    }

    /// <summary>A security exception or risk acceptance that has expired or expires soon, from <see cref="ExceptionReview.Declared"/>.</summary>
    public static NextItem Exception(ExceptionNote note, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(note);
        DateOnly? expires = DateOnly.TryParseExact(note.Expires, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
        return new NextItem(
            expires is { } e && e >= today ? NextUrgency.DueSoon : NextUrgency.Overdue,
            "exception",
            note.RefId,
            $"{note.Kind} '{note.Name}', expiry {note.Expires ?? "not set"}: {note.Detail}",
            "hound scope plan scope --config hound.yaml",
            15)
        {
            Due = expires,
            Skill = "keep-exceptions-and-acceptances",
        };
    }

    /// <summary>s19-08: the first step for a scope with no audit yet; null once one exists.</summary>
    public static NextItem? Onboarding(int audits) => audits < 0 ? null : null;

    /// <summary>Scope changes not yet applied to CISO Assistant, and values that drifted there; null when there is nothing.</summary>
    public static NextItem? Scope(int changes, int drift) => changes == 0 && drift == 0
        ? null
        : new NextItem(
            NextUrgency.Waiting,
            "scope",
            Text($"{changes} change(s) to apply and {drift} drifted value(s)"),
            "Save the plan for the person's review session, which applies it.",
            "hound scope plan scope --config hound.yaml --out plan.json",
            10)
        {
            Skill = "review-before-apply",
        };

    private static int RoundUp(int minutes) => Math.Max(15, (minutes + 14) / 15 * 15);

    private static string Day(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
