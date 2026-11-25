namespace EvidenceHound.Core.Scope;

/// <summary>How a row's evidence arrives: a collector, a person following a runbook, or both.</summary>
public enum EvidenceMethod
{
    Automated,
    Manual,
    Hybrid,
}

/// <summary>
/// <c>scope/evidence-plan.yaml</c>: one row per control (or some of its requirements), saying how its evidence arrives, who provides it
/// and how often. Manual and hybrid rows become recurring CISO Assistant tasks, so owners are reminded with their runbook.
/// </summary>
public sealed record ScopeEvidencePlan(ScopeRef? GrcTeam, IReadOnlyList<ScopeEvidenceRow> Rows, SourceLine At);

/// <summary>
/// One row of the evidence plan. <see cref="GrcTeam"/> is the row's own or the file's default. <see cref="Every"/> is a week or 3, 6 or 12 months,
/// and <see cref="Runbook"/> a Markdown file relative to the compliance repo.
/// </summary>
public sealed record ScopeEvidenceRow(string RefId, ScopeRef Control, EvidenceMethod Method, SourceLine At)
{
    public IReadOnlyList<RequirementRef> Requirements { get; init; } = [];

    public ScopeRef? Collector { get; init; }

    public ScopeRef? Owner { get; init; }

    public ScopeRef? GrcTeam { get; init; }

    public EvidenceInterval? Every { get; init; }

    public string? Runbook { get; init; }

    /// <summary>When the first reminder is due, such as just before the anniversary of an assessment (s17-06); null for one interval from today.</summary>
    public DateOnly? FirstDue { get; init; }

    /// <summary>The first due date on or after <paramref name="today"/>: <see cref="FirstDue"/> rolled forward by the interval, else one interval from today.</summary>
    public DateOnly? FirstDueFrom(DateOnly today)
    {
        if (Every is not { } every)
        {
            return null;
        }

        if (FirstDue is not { } due)
        {
            return every.After(today);
        }

        while (due < today)
        {
            due = every.After(due);
        }

        return due;
    }

    /// <summary>A person provides this row's evidence, so CISO Assistant reminds them.</summary>
    public bool NeedsPerson => Method is EvidenceMethod.Manual or EvidenceMethod.Hybrid;

    /// <summary>A collector provides this row's evidence, or part of it.</summary>
    public bool NeedsCollector => Method is EvidenceMethod.Automated or EvidenceMethod.Hybrid;
}
