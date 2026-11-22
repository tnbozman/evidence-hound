using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Scope;

/// <summary>One implementation group of an audit, by ref and name: requirements compliant before and after the plan, out of the group's total.</summary>
public sealed record ImpactGroup(string Group, string Name, int Before, int After, int Total);

/// <summary>A row whose result the plan changes: its requirement, title and parent (the strategy or domain), old and new result, and why.</summary>
public sealed record ImpactRow(string Requirement, string Title, string? Parent, string Old, string New, string Why, bool Regresses);

/// <summary>What a plan does to one audit's compliance: compliant counts per selected group, then each changed row, regressions first.</summary>
public sealed record AuditImpact(string Audit, IReadOnlyList<ImpactGroup> Groups, IReadOnlyList<ImpactRow> Rows)
{
    public int Regressions => Rows.Count(r => r.Regresses);
}

/// <summary>
/// A scope plan's effect on compliance, the way Terraform shows changes to outputs below the resource diff. Built from the framework snapshot and
/// every row CISO Assistant holds for each audit. Rows collector runs own are left out: their impact belongs to <c>hound plan</c>. Pure.
/// </summary>
public static class ComplianceImpact
{
    private static readonly string NotAssessed = "not_assessed";
    private static readonly string Compliant = "compliant";

    public static IReadOnlyList<AuditImpact> Of(ScopePlan plan, IReadOnlyList<ScopeObject> declared, IReadOnlyList<ActualObject> actual, ScopeInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(inputs);

        var held = actual.Where(a => a.Held.Kind == ScopeKind.Assessment)
            .GroupBy(a => a.Held.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Held.Fields.GetValueOrDefault("result") ?? NotAssessed, StringComparer.Ordinal);
        var rows = declared.Where(d => d.Kind == ScopeKind.Assessment).GroupBy(d => d.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var newResults = plan.Changes
            .Where(c => c.Kind == ScopeKind.Assessment && c.Action != ScopeAction.Delete)
            .SelectMany(c => c.Fields.Where(f => f.Field == "result" && f.New is not null).Select(f => (c.Key, f.Old, New: f.New!)))
            .ToArray();

        var impacts = new List<AuditImpact>();
        foreach (var audit in declared.Where(d => d.Kind == ScopeKind.Audit))
        {
            if (audit.Fields.GetValueOrDefault("framework") is not { } framework || inputs.Snapshots.GetValueOrDefault(framework) is not { } snapshot)
            {
                continue;
            }

            var runOwned = (inputs.Targets ?? []).Where(t => t.AuditRefId == audit.Key).SelectMany(t => t.Computed).ToArray();
            var nodes = snapshot.Nodes.Select(n => (Node: n, Key: $"{audit.Key}/{RowKey(snapshot, n)}")).ToArray();
            var changed = newResults
                .Where(r => r.Key.StartsWith($"{audit.Key}/", StringComparison.Ordinal))
                .Select(r => (Change: r, Node: nodes.FirstOrDefault(n => n.Key == r.Key || $"{audit.Key}/{n.Node.Urn}" == r.Key).Node))
                .Where(r => r.Node is not null && !runOwned.Any(k => Names(k, r.Node)))
                .Select(r =>
                {
                    var old = r.Change.Old ?? held.GetValueOrDefault(r.Change.Key) ?? NotAssessed;
                    return (Node: r.Node!, r.Change.Key, Old: old, r.Change.New);
                })
                .Where(r => r.Old != r.New)
                .ToArray();
            if (changed.Length == 0)
            {
                continue;
            }

            var after = changed.ToDictionary(c => c.Node.Urn, c => c.New, StringComparer.Ordinal);
            string Before(SnapshotNode node, string key) => held.GetValueOrDefault(key) ?? held.GetValueOrDefault($"{audit.Key}/{node.Urn}") ?? NotAssessed;
            var groups = Split(audit.Fields.GetValueOrDefault("groups"))
                .Select(group =>
                {
                    var inGroup = nodes.Where(n => n.Node.Groups.Count == 0 || n.Node.Groups.Contains(group)).ToArray();
                    return new ImpactGroup(
                        group,
                        snapshot.Groups.FirstOrDefault(g => g.RefId == group)?.Name ?? group,
                        inGroup.Count(n => Before(n.Node, n.Key) == Compliant),
                        inGroup.Count(n => (after.GetValueOrDefault(n.Node.Urn) ?? Before(n.Node, n.Key)) == Compliant),
                        inGroup.Length);
                })
                .ToArray();
            var order = nodes.Select((n, index) => (n.Node.Urn, index)).ToDictionary(n => n.Urn, n => n.index, StringComparer.Ordinal);
            var impactRows = changed
                .Select(c => new ImpactRow(
                    c.Node.RefId ?? c.Node.Urn,
                    c.Node.Title,
                    Parent(snapshot, c.Node),
                    c.Old,
                    c.New,
                    rows.GetValueOrDefault(c.Key)?.Fields.ContainsKey("status") == true ? "hand-written in the audit file" : "the unlisted rule",
                    Regresses(c.Old, c.New)))
                .OrderByDescending(r => r.Regresses)
                .ThenBy(r => order[changed.First(c => (c.Node.RefId ?? c.Node.Urn) == r.Requirement).Node.Urn])
                .ToArray();
            impacts.Add(new AuditImpact(audit.Key, groups, impactRows));
        }

        return impacts;
    }

    /// <summary>A result that goes backwards: from compliant or partially compliant to anything lower, or to not assessed.</summary>
    private static bool Regresses(string old, string @new) =>
        Rank(old) >= Rank("partially_compliant") && Rank(@new) < Rank(old);

    private static int Rank(string result) => result switch
    {
        "compliant" => 3,
        "partially_compliant" => 2,
        "non_compliant" => 1,
        _ => 0,
    };

    /// <summary>
    /// The requirement's parent as the strategy or domain: the parent node's ref or title when the snapshot holds it, otherwise the last part of
    /// its URN (the snapshot keeps only assessable nodes), upper case.
    /// </summary>
    private static string? Parent(FrameworkSnapshot snapshot, SnapshotNode node)
    {
        if (node.ParentUrn is not { } parentUrn)
        {
            return null;
        }

        if (snapshot.Nodes.FirstOrDefault(n => n.Urn == parentUrn) is { } parent)
        {
            return parent.RefId ?? parent.Title;
        }

        return parentUrn[(parentUrn.LastIndexOf(':') + 1)..].ToUpperInvariant();
    }

    /// <summary>A row's key in an audit: the requirement's ref, or its URN when the ref is missing or repeated, as the scope keys rows.</summary>
    private static string RowKey(FrameworkSnapshot snapshot, SnapshotNode node) =>
        node.RefId is { } refId && snapshot.Nodes.Count(n => n.RefId == refId) == 1 ? refId : node.Urn;

    private static bool Names(RequirementKey key, SnapshotNode? node) =>
        node is not null && (key.Kind == RequirementKeyKind.Urn ? key.Value == node.Urn : key.Value == node.RefId);

    private static string[] Split(string? joined) => joined is null ? [] : joined.Split(", ", StringSplitOptions.RemoveEmptyEntries);
}
