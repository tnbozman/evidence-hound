namespace EvidenceHound.Core.Scope;

/// <summary>Plans the writes that bring CISO Assistant in line with a scope. Pure.</summary>
public static class ScopePlanner
{
    /// <summary>
    /// Compares the <paramref name="declared"/> objects with the <paramref name="actual"/> ones. With <paramref name="prune"/>, managed objects the
    /// scope no longer declares are deleted; unmanaged objects never are.
    /// </summary>
    /// <remarks>
    /// Creates and updates come in dependency order (<see cref="ScopeKind"/>), deletes after them in reverse. A managed object whose fields no
    /// longer match the state <c>hound</c> recorded on it was edited in CISO Assistant: it is reported as drift and written back.
    /// </remarks>
    public static ScopePlan Plan(IReadOnlyList<ScopeObject> declared, IReadOnlyList<ActualObject> actual, bool prune)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(actual);
        var held = actual.GroupBy(a => (a.Held.Kind, a.Held.Key)).ToDictionary(g => g.Key, g => g.First());
        var wanted = declared.Select(d => (d.Kind, d.Key)).ToHashSet();
        var changes = new List<ScopeChange>();
        var drift = new List<ScopeDrift>();

        foreach (var want in declared.OrderBy(d => d.Kind))
        {
            if (!held.TryGetValue((want.Kind, want.Key), out var have))
            {
                changes.Add(new ScopeChange(want.Kind, want.Key, ScopeAction.Create, [.. Created(want)], StateOf(want)));
                continue;
            }

            var differences = Differences(have.Held.Fields, want.Fields);
            var marked = Marked(want.Kind);
            if (marked && have.Managed && have.AppliedState != ScopeMarker.Fingerprint(have.Held.Fields))
            {
                drift.Add(new ScopeDrift(want.Kind, want.Key, DriftReason.Edited, Managed: true, differences));
            }

            if (differences.Count > 0 || (marked && have.AppliedState != StateOf(want)))
            {
                changes.Add(new ScopeChange(want.Kind, want.Key, ScopeAction.Update, differences, StateOf(want)));
            }
        }

        var extra = actual.Where(a => !wanted.Contains((a.Held.Kind, a.Held.Key)) && Reported(a)).OrderByDescending(a => a.Held.Kind).ToArray();
        drift.AddRange(extra.Select(a => new ScopeDrift(a.Held.Kind, a.Held.Key, DriftReason.NotInScope, a.Managed, [])));
        if (prune)
        {
            changes.AddRange(extra.Where(a => a.Managed).Select(a => new ScopeChange(a.Held.Kind, a.Held.Key, ScopeAction.Delete, [], null)));
        }

        return new ScopePlan(changes, drift);
    }

    /// <summary>Frameworks come from libraries and assessments are rows of an audit: neither carries a marker, nor is ever deleted.</summary>
    private static bool Marked(ScopeKind kind) => kind is not (ScopeKind.Framework or ScopeKind.Assessment);

    /// <summary>
    /// Objects the scope does not declare are drift when <c>hound</c> manages them, or when they are structure (domains, perimeters, systems,
    /// audits, controls). Evidence <c>hound</c> does not manage is a collector's, not drift.
    /// </summary>
    private static bool Reported(ActualObject held) =>
        Marked(held.Held.Kind) && (held.Managed || held.Held.Kind != ScopeKind.Document);

    private static string? StateOf(ScopeObject declared) => Marked(declared.Kind) ? ScopeMarker.Fingerprint(declared.Fields) : null;

    private static IEnumerable<FieldChange> Created(ScopeObject declared) =>
        declared.Fields.Concat(declared.Initial.Where(i => !declared.Fields.ContainsKey(i.Key))).Select(f => new FieldChange(f.Key, null, f.Value));

    /// <summary>Each field whose value differs: the declared fields in order, then fields CISO Assistant holds that the scope clears.</summary>
    private static List<FieldChange> Differences(IReadOnlyDictionary<string, string> held, IReadOnlyDictionary<string, string> wanted) =>
    [
        .. wanted.Where(w => !held.TryGetValue(w.Key, out var value) || value != w.Value).Select(w => new FieldChange(w.Key, held.GetValueOrDefault(w.Key), w.Value)),
        .. held.Where(h => !wanted.ContainsKey(h.Key)).Select(h => new FieldChange(h.Key, h.Value, null)),
    ];
}
