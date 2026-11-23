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
    /// longer match the state <c>hound</c> recorded on it was edited in CISO Assistant: it is reported as drift and written back. Links (a
    /// control's systems and documents, a row's controls and documents, a document's file hashes) are added to what CISO Assistant holds, and
    /// removed only with <paramref name="prune"/>. A locked audit and its rows are never changed, and a framework loaded from an older library
    /// version is never upgraded: both are reported as drift.
    /// </remarks>
    public static ScopePlan Plan(IReadOnlyList<ScopeObject> declared, IReadOnlyList<ActualObject> actual, bool prune)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(actual);
        var held = actual.GroupBy(a => (a.Held.Kind, a.Held.Key)).ToDictionary(g => g.Key, g => g.First());
        var wanted = declared.Select(d => (d.Kind, d.Key)).ToHashSet();
        var locked = actual.Where(a => a.Locked && a.Held.Kind == ScopeKind.Audit).Select(a => a.Held.Key).ToHashSet(StringComparer.Ordinal);
        var changes = new List<ScopeChange>();
        var drift = new List<ScopeDrift>();

        foreach (var declaredObject in declared.OrderBy(d => d.Kind))
        {
            var isLocked = IsLocked(declaredObject, locked);
            if (!held.TryGetValue((declaredObject.Kind, declaredObject.Key), out var have))
            {
                var created = Created(declaredObject).ToList();
                if (isLocked)
                {
                    drift.Add(new ScopeDrift(declaredObject.Kind, declaredObject.Key, DriftReason.Locked, Managed: false, created));
                }
                else
                {
                    changes.Add(new ScopeChange(declaredObject.Kind, declaredObject.Key, ScopeAction.Create, created, StateOf(declaredObject)) { Desired = declaredObject });
                }

                continue;
            }

            if (declaredObject.Kind == ScopeKind.Framework)
            {
                drift.AddRange(Outdated(declaredObject, have));
                continue;
            }

            var want = prune ? declaredObject : Linked(declaredObject, have.Held.Fields);
            var comparable = Comparable(want.Kind, have.Held.Fields, want.Fields);
            var differences = Differences(comparable, want.Fields);
            var marked = Marked(want.Kind);
            if (marked && have.Managed && have.AppliedState != ScopeMarker.State(want.Kind, comparable))
            {
                drift.Add(new ScopeDrift(want.Kind, want.Key, DriftReason.Edited, Managed: true, differences));
            }

            if (differences.Count == 0 && !(marked && have.AppliedState != StateOf(want)))
            {
                continue;
            }

            if (isLocked)
            {
                drift.Add(new ScopeDrift(want.Kind, want.Key, DriftReason.Locked, have.Managed, differences));
            }
            else
            {
                changes.Add(new ScopeChange(want.Kind, want.Key, ScopeAction.Update, differences, StateOf(want)) { Desired = want });
            }
        }

        var extra = actual.Where(a => !wanted.Contains((a.Held.Kind, a.Held.Key)) && Reported(a)).OrderByDescending(a => a.Held.Kind).ToArray();
        drift.AddRange(extra.Select(a => new ScopeDrift(a.Held.Kind, a.Held.Key, DriftReason.NotInScope, a.Managed, [])));
        if (prune)
        {
            changes.AddRange(extra.Where(a => a.Managed && !a.Locked).Select(Pruned).OfType<ScopeChange>());
        }

        return new ScopePlan(changes, drift);
    }

    /// <summary>
    /// How a managed object the scope no longer declares is removed: deleted, except a user, who is deactivated (once) so their history in
    /// CISO Assistant stays attributable.
    /// </summary>
    private static ScopeChange? Pruned(ActualObject held)
    {
        if (held.Held.Kind != ScopeKind.User)
        {
            return new ScopeChange(held.Held.Kind, held.Held.Key, ScopeAction.Delete, [], null);
        }

        var active = held.Held.Fields.GetValueOrDefault("active");
        if (active == "false")
        {
            return null;
        }

        var deactivated = held.Held with { Fields = new Dictionary<string, string>(held.Held.Fields, StringComparer.Ordinal) { ["active"] = "false" } };
        return new ScopeChange(ScopeKind.User, held.Held.Key, ScopeAction.Update, [new FieldChange("active", active, "false")], StateOf(deactivated)) { Desired = deactivated };
    }

    /// <summary>
    /// The held fields to compare: a row's are only those the scope declares, because CISO Assistant and the run publisher own the rest. Any other
    /// object's leave out a <c>ref_id</c> the scope does not declare, because CISO Assistant fills a blank <c>ref_id</c> in itself.
    /// </summary>
    private static Dictionary<string, string> Comparable(ScopeKind kind, IReadOnlyDictionary<string, string> held, IReadOnlyDictionary<string, string> wanted) =>
        held.Where(h => kind == ScopeKind.Assessment || Optional(kind, h.Key) ? wanted.ContainsKey(h.Key) : h.Key != "ref_id" || wanted.ContainsKey(h.Key)).ToDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Fields compared only when the scope declares them: an audit's status and lock, which only a period audit declares, and a supplier
    /// review's conclusion, which the scope declares once a person concludes it with <c>hound approve</c>, and a supplier's levels and a
    /// solution's criticality, which a drafted supplier leaves to decide and CISO Assistant fills in with its defaults.
    /// </summary>
    private static bool Optional(ScopeKind kind, string field) => kind switch
    {
        ScopeKind.Audit => field is "status" or "locked",
        ScopeKind.EntityAssessment => field == "conclusion",
        ScopeKind.Entity => field is "dependency" or "penetration" or "maturity" or "trust",
        ScopeKind.Solution => field == "criticality",
        _ => false,
    };

    /// <summary>The declared object with the links CISO Assistant already holds added to its own.</summary>
    private static ScopeObject Linked(ScopeObject declared, IReadOnlyDictionary<string, string> held)
    {
        var links = ScopeMarker.LinkFields(declared.Kind);
        if (links.Length == 0)
        {
            return declared;
        }

        var fields = new Dictionary<string, string>(declared.Fields, StringComparer.Ordinal);
        foreach (var link in links.Where(held.ContainsKey))
        {
            var joined = ScopeDesired.Joined(Split(fields.GetValueOrDefault(link)).Union(Split(held[link]), StringComparer.Ordinal));
            if (joined is not null)
            {
                fields[link] = joined;
            }
        }

        return declared with { Fields = fields };
    }

    private static string[] Split(string? joined) => joined is null ? [] : joined.Split(", ", StringSplitOptions.RemoveEmptyEntries);

    /// <summary>An audit locked in CISO Assistant, or a row of one.</summary>
    private static bool IsLocked(ScopeObject declared, HashSet<string> lockedAudits) => declared.Kind switch
    {
        ScopeKind.Audit => lockedAudits.Contains(declared.Key),
        ScopeKind.Assessment => lockedAudits.Contains(declared.Key.Split('/', 2)[0]),
        _ => false,
    };

    /// <summary>A framework whose loaded library is older than the scope's snapshot.</summary>
    private static IEnumerable<ScopeDrift> Outdated(ScopeObject declared, ActualObject held)
    {
        if (Version(declared.Fields) is { } wanted && Version(held.Held.Fields) is { } loaded && loaded < wanted)
        {
            yield return new ScopeDrift(declared.Kind, declared.Key, DriftReason.Outdated, Managed: false, [new FieldChange("version", held.Held.Fields["version"], declared.Fields["version"])]);
        }
    }

    private static int? Version(IReadOnlyDictionary<string, string> fields) =>
        fields.TryGetValue("version", out var text) && int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var version) ? version : null;

    /// <summary>
    /// Frameworks come from libraries, assessments are rows of an audit, and flags and settings are CISO Assistant's own: none carries a marker,
    /// nor is ever deleted, and those the scope does not name are not drift.
    /// </summary>
    private static bool Marked(ScopeKind kind) => kind is not (ScopeKind.Framework or ScopeKind.Assessment or ScopeKind.FeatureFlag or ScopeKind.Setting or ScopeKind.MainEntity);

    /// <summary>
    /// Objects the scope does not declare are drift when <c>hound</c> manages them, or when they are structure (domains, perimeters, systems,
    /// audits, controls). Evidence <c>hound</c> does not manage is a collector's, not drift.
    /// </summary>
    private static bool Reported(ActualObject held) =>
        Marked(held.Held.Kind) && (held.Managed || held.Held.Kind != ScopeKind.Document);

    private static string? StateOf(ScopeObject declared) => Marked(declared.Kind) ? ScopeMarker.State(declared.Kind, declared.Fields) : null;

    private static IEnumerable<FieldChange> Created(ScopeObject declared) =>
        declared.Fields.Concat(declared.Initial.Where(i => !declared.Fields.ContainsKey(i.Key))).Select(f => new FieldChange(f.Key, null, f.Value));

    /// <summary>Each field whose value differs: the declared fields in order, then fields CISO Assistant holds that the scope clears.</summary>
    private static List<FieldChange> Differences(Dictionary<string, string> held, IReadOnlyDictionary<string, string> wanted) =>
    [
        .. wanted.Where(w => !held.TryGetValue(w.Key, out var value) || value != w.Value).Select(w => new FieldChange(w.Key, held.GetValueOrDefault(w.Key), w.Value)),
        .. held.Where(h => !wanted.ContainsKey(h.Key)).Select(h => new FieldChange(h.Key, h.Value, null)),
    ];
}
