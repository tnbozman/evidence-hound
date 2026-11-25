using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Scope;

/// <summary>One write of a plan: the change, the object the scope declares (null for a delete) and the root folder domains sit under.</summary>
public sealed record ScopeWrite(ScopeChange Change, ScopeObject? Declared, string RootFolder);

/// <summary>Carries out one change in CISO Assistant, finding the IDs of the objects it refers to by name.</summary>
public interface IScopeWriter
{
    Task<Result<Unit>> WriteAsync(ScopeWrite write, CancellationToken cancellationToken);
}

public enum ApplyOutcome
{
    Written,
    Failed,
    Skipped,
}

/// <summary>What happened to one change: written, failed (with the problem), or skipped because something it depends on was not written.</summary>
public sealed record AppliedChange(ScopeChange Change, ApplyOutcome Outcome, string Detail);

/// <summary>
/// Each change the apply handled, in order, how many changes it left for a later version (kinds it does not apply yet), and the plan's drift,
/// which includes what it would not write (locked audits, outdated frameworks).
/// </summary>
public sealed record ScopeApplyReport(IReadOnlyList<AppliedChange> Changes, int NotApplied, IReadOnlyList<ScopeDrift> Drift)
{
    public int Written => Changes.Count(c => c.Outcome == ApplyOutcome.Written);

    public int Failed => Changes.Count(c => c.Outcome == ApplyOutcome.Failed);

    public int Skipped => Changes.Count(c => c.Outcome == ApplyOutcome.Skipped);
}

/// <summary>Carries out a scope plan through an <see cref="IScopeWriter"/>, in the plan's order.</summary>
public static class ScopeApplier
{
    /// <summary>
    /// Writes each change of the <paramref name="kinds"/> this version applies; changes of other kinds are counted, not written. A change whose
    /// domain, parent system, perimeter, framework, system, document, control or audit failed or was skipped in this apply is skipped too, so a failed
    /// write stops only what depends on it.
    /// </summary>
    public static async Task<ScopeApplyReport> ApplyAsync(
        ScopePlan plan, IReadOnlyList<ScopeObject> declared, IReadOnlyCollection<ScopeKind> kinds, string rootFolder, IScopeWriter writer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(writer);
        var objects = declared.GroupBy(d => (d.Kind, d.Key)).ToDictionary(g => g.Key, g => g.First());
        var notWritten = new HashSet<(ScopeKind, string)>();
        var applied = new List<AppliedChange>();
        foreach (var change in plan.Changes.Where(c => kinds.Contains(c.Kind)))
        {
            var target = change.Action == ScopeAction.Delete ? null : change.Desired ?? objects.GetValueOrDefault((change.Kind, change.Key));
            if (target is not null && DependsOn(target).FirstOrDefault(notWritten.Contains) is { Item2: not null } blocked)
            {
                notWritten.Add((change.Kind, change.Key));
                applied.Add(new AppliedChange(change, ApplyOutcome.Skipped, $"skipped: {Name(blocked.Item1)} '{blocked.Item2}' was not written."));
                continue;
            }

            var result = await writer.WriteAsync(new ScopeWrite(change, target, rootFolder), cancellationToken);
            applied.Add(result.Match(
                _ => new AppliedChange(change, ApplyOutcome.Written, Done(change.Action)),
                problems =>
                {
                    notWritten.Add((change.Kind, change.Key));
                    return new AppliedChange(change, ApplyOutcome.Failed, string.Join(" ", problems.Select(p => p.Message)));
                }));
        }

        return new ScopeApplyReport(applied, plan.Changes.Count(c => !kinds.Contains(c.Kind)), plan.Drift);
    }

    /// <summary>The objects a declared object refers to, by the fields that name them.</summary>
    public static IEnumerable<(ScopeKind, string)> DependsOn(ScopeObject declared)
    {
        ArgumentNullException.ThrowIfNull(declared);
        var fields = declared.Fields;
        if (fields.TryGetValue("domain", out var domain))
        {
            yield return (ScopeKind.Domain, domain);
        }

        foreach (var (field, kind) in new[]
        {
            ("parents", ScopeKind.System), ("systems", ScopeKind.System), ("documents", ScopeKind.Document), ("controls", ScopeKind.Control),
            ("existing_controls", ScopeKind.Control), ("planned_controls", ScopeKind.Control), ("study", ScopeKind.RiskStudy),
            ("exceptions", ScopeKind.SecurityException), ("scenario", ScopeKind.RiskScenario),
            ("bia", ScopeKind.Bia), ("system", ScopeKind.System), ("dependencies", ScopeKind.System), ("assessment", ScopeKind.AssetAssessment),
            ("definition", ScopeKind.MetricDefinition), ("metric", ScopeKind.MetricInstance), ("dashboard", ScopeKind.Dashboard),
        })
        {
            foreach (var name in Split(fields.GetValueOrDefault(field)))
            {
                yield return (kind, name);
            }
        }

        if (declared.Initial.TryGetValue("baseline", out var baseline))
        {
            yield return (ScopeKind.Audit, baseline);
        }

        if (fields.TryGetValue("framework", out var framework))
        {
            yield return (ScopeKind.Framework, framework);
        }

        if (domain is not null && fields.TryGetValue("perimeter", out var perimeter))
        {
            yield return (ScopeKind.Perimeter, ScopeDesired.PerimeterKey(domain, perimeter));
        }

        if (declared.Kind == ScopeKind.Assessment && declared.Key.Split('/', 2) is [var audit, _])
        {
            yield return (ScopeKind.Audit, audit);
        }
    }

    private static string[] Split(string? joined) => joined is null ? [] : joined.Split(", ", StringSplitOptions.RemoveEmptyEntries);

    private static string Done(ScopeAction action) => action switch
    {
        ScopeAction.Create => "created",
        ScopeAction.Update => "updated",
        _ => "deleted",
    };

    private static string Name(ScopeKind kind) => kind.ToString().ToLowerInvariant();
}
