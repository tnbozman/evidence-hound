using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Scope;

/// <summary>A framework's mitigation, such as application control: its code and the requirements under it.</summary>
public sealed record Mitigation(string Code, IReadOnlyList<SnapshotNode> Requirements);

/// <summary>A row to write in a system's audit: the requirement, by <c>ref</c> or <c>urn</c>, and why it is not applicable there.</summary>
public sealed record CapabilityRow(string Key, string Requirement, string Observation)
{
    /// <summary>The systems the capability map says meet the requirement.</summary>
    public IReadOnlyList<string> ProvidedBy { get; init; } = [];
}

/// <summary>
/// The rows the map writes into one system's own audit file, and the requirements left out because the audit's mapping computes them
/// (a hand-written row would conflict with it, <c>scope.result_conflict</c>).
/// </summary>
public sealed record AuditCapabilityRows(string AuditRefId, string File, IReadOnlyList<CapabilityRow> Rows)
{
    public IReadOnlyList<string> Computed { get; init; } = [];
}

/// <summary>A mitigation no product provides: its requirements, which no system's audit answers.</summary>
public sealed record CapabilityGap(string Mitigation, IReadOnlyList<string> Requirements);

/// <summary>A system the map names that <c>scope/setup.yaml</c> does not declare yet: onboard it, with its supplier.</summary>
public sealed record PendingSystem(string System, IReadOnlyList<string> Mitigations, string? Supplier);

public sealed record CapabilityRows(IReadOnlyList<AuditCapabilityRows> Audits, IReadOnlyList<CapabilityGap> Gaps);

/// <summary>
/// The capability map against the framework and the scope: every mitigation decided once, and the not-applicable rows it puts in each
/// system's own audit. Pure.
/// </summary>
public static class CapabilityMap
{
    public const string FileName = "scope/capabilities.yaml";

    /// <summary>The framework's mitigations: its assessable requirements grouped by their parent node, in the framework's order.</summary>
    public static IReadOnlyList<Mitigation> Mitigations(FrameworkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return [.. snapshot.Nodes.Where(n => n.ParentUrn is not null)
            .GroupBy(n => n.ParentUrn!, StringComparer.Ordinal)
            .Select(g => new Mitigation(g.Key[(g.Key.LastIndexOf(':') + 1)..], [.. g]))];
    }

    /// <summary>
    /// Every problem with the map: a framework the scope does not load, a mitigation decided twice, unknown or not at all, a decision without
    /// an approver, a system decision naming no system, and a supplier the scope does not declare. A system not declared yet is not a
    /// problem: it is <see cref="Pending"/>, to be onboarded.
    /// </summary>
    public static IReadOnlyList<ScopeProblem> Validate(ScopeDocument scope, IReadOnlyDictionary<string, FrameworkSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(snapshots);
        if (scope.Capabilities is not { } map)
        {
            return [];
        }

        if (!scope.Setup.Frameworks.Any(f => f.Urn == map.Framework.Value) || !snapshots.TryGetValue(map.Framework.Value, out var snapshot))
        {
            return [new ScopeProblem(
                "capability.framework_unknown", map.Framework.At, $"framework '{map.Framework.Value}' is not a framework of scope/setup.yaml with a snapshot.",
                "Map a framework that scope/setup.yaml loads, with its snapshot.")];
        }

        var codes = Mitigations(snapshot).Select(m => m.Code).ToArray();
        var suppliers = scope.ThirdParties.Select(e => e.RefId).ToHashSet(StringComparer.Ordinal);
        var problems = new List<ScopeProblem>();
        foreach (var repeated in map.Mitigations.GroupBy(m => m.Mitigation, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problems.AddRange(repeated.Skip(1).Select(m => new ScopeProblem(
                "scope.duplicate", m.At, $"mitigation '{m.Mitigation}' is decided more than once.", "Keep one decision for each mitigation.")));
        }

        foreach (var decision in map.Mitigations)
        {
            if (!codes.Contains(decision.Mitigation, StringComparer.Ordinal))
            {
                problems.Add(new ScopeProblem(
                    "capability.mitigation_unknown", decision.At, $"mitigation '{decision.Mitigation}' is not in {map.Framework.Value}.", $"Use one of: {string.Join(", ", codes)}."));
            }

            if (string.IsNullOrWhiteSpace(decision.ApprovedBy))
            {
                problems.Add(new ScopeProblem(
                    "capability.approver_missing", decision.At, $"mitigation '{decision.Mitigation}' has no approved_by.", "Name the role that approved this decision."));
            }

            if (decision.ProvidedBy == CapabilityProvision.System && decision.Systems.Count == 0)
            {
                problems.Add(new ScopeProblem(
                    "capability.systems_missing", decision.At, $"mitigation '{decision.Mitigation}' is provided by a system but names none.", "List the systems under 'systems', or use provided_by: manual or gap."));
            }

            if (decision.Supplier is { } supplier && !suppliers.Contains(supplier.Value))
            {
                problems.Add(new ScopeProblem(
                    "scope.reference_unknown", supplier.At, $"supplier '{supplier.Value}' is not declared in scope/third-parties/.", "Add the supplier's file first."));
            }
        }

        var decided = map.Mitigations.Select(m => m.Mitigation).ToHashSet(StringComparer.Ordinal);
        problems.AddRange(codes.Where(c => !decided.Contains(c)).Select(c => new ScopeProblem(
            "capability.decision_missing", map.At, $"mitigation '{c}' has no decision in {FileName}.", "Decide it: provided_by system (with its systems), manual or gap.")));
        return problems;
    }

    /// <summary>
    /// For each system's own audit of the map's framework, the requirements another system provides, as not-applicable rows naming that
    /// system and its audit; rows the audit already writes by hand, and requirements outside its groups, are left alone. Gaps are listed
    /// apart: no audit answers them, and none fails for them.
    /// </summary>
    /// <summary>The systems the map names that <c>scope/setup.yaml</c> does not declare yet, with their mitigations and supplier, in map order.</summary>
    public static IReadOnlyList<PendingSystem> Pending(ScopeDocument scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var declared = scope.Setup.Domains.SelectMany(d => d.Systems).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        return [.. (scope.Capabilities?.Mitigations ?? [])
            .SelectMany(m => m.Systems.Where(s => !declared.Contains(s.Value)).Select(s => (System: s.Value, Decision: m)))
            .GroupBy(x => x.System, StringComparer.Ordinal)
            .Select(g => new PendingSystem(g.Key, [.. g.Select(x => x.Decision.Mitigation)], g.Select(x => x.Decision.Supplier?.Value).FirstOrDefault(v => v is not null)))];
    }

    /// <summary>The mitigations no product provides, from the snapshot of the framework the map names; none without a map or that snapshot.</summary>
    public static IReadOnlyList<CapabilityGap> Gaps(ScopeDocument scope, IReadOnlyDictionary<string, FrameworkSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(snapshots);
        return scope.Capabilities is { } map && snapshots.TryGetValue(map.Framework.Value, out var snapshot) ? Rows(scope, snapshot).Gaps : [];
    }

    public static CapabilityRows Rows(ScopeDocument scope, FrameworkSnapshot snapshot) => Rows(scope, snapshot, []);

    /// <summary>The rows, leaving out each requirement that a target's mapping computes for the same audit.</summary>
    public static CapabilityRows Rows(ScopeDocument scope, FrameworkSnapshot snapshot, IReadOnlyList<ScopeTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (scope.Capabilities is not { } map || map.Framework.Value != snapshot.FrameworkUrn)
        {
            return new([], []);
        }

        var mitigations = Mitigations(snapshot);
        var decisions = map.Mitigations.GroupBy(m => m.Mitigation, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var refCounts = snapshot.Nodes.Where(n => n.RefId is not null).GroupBy(n => n.RefId!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var audits = scope.Audits.Where(a => a.Framework.Value == map.Framework.Value && a.Systems.Count > 0).ToArray();
        string Provider(string system) =>
            audits.FirstOrDefault(a => a.Systems.Any(s => s.Value == system)) is { } audit
                ? $"{system} (audit {audit.RefId})"
                : $"{system} (no audit yet; onboard it with 'hound onboard {system}')";

        var perAudit = new List<AuditCapabilityRows>();
        foreach (var audit in audits)
        {
            var own = audit.Systems.Select(s => s.Value).ToHashSet(StringComparer.Ordinal);
            var groups = audit.Groups.Select(g => g.Value).ToHashSet(StringComparer.Ordinal);
            var computed = targets.Where(t => t.AuditRefId == audit.RefId)
                .SelectMany(t => t.Computed.SelectMany(k => snapshot.Resolve(k) is { Count: > 0 } nodes ? nodes.Select(n => n.Urn) : [k.Value]))
                .ToHashSet(StringComparer.Ordinal);
            var rows = new List<CapabilityRow>();
            var leftToMapping = new List<string>();
            foreach (var mitigation in mitigations)
            {
                if (decisions.GetValueOrDefault(mitigation.Code) is not { ProvidedBy: CapabilityProvision.System } decision
                    || decision.Systems.Count == 0 || decision.Systems.Any(s => own.Contains(s.Value)))
                {
                    continue;
                }

                var approved = decision.ApprovedOn is null ? $"approved by {decision.ApprovedBy}" : $"approved by {decision.ApprovedBy} on {decision.ApprovedOn}";
                var observation = $"Met by {string.Join(" and ", decision.Systems.Select(s => Provider(s.Value)))}, as recorded in {FileName} ({approved}); open until that audit shows it met.";
                IReadOnlyList<string> providers = [.. decision.Systems.Select(s => s.Value)];
                var open = mitigation.Requirements
                    .Where(n => groups.Count == 0 || n.Groups.Any(groups.Contains))
                    .Where(n => !audit.Assessments.Any(a => a.Requirement.Key.Value == n.Urn || a.Requirement.Key.Value == n.RefId))
                    .ToArray();
                leftToMapping.AddRange(open.Where(n => computed.Contains(n.Urn)).Select(n => n.RefId ?? n.Urn));
                rows.AddRange(open.Where(n => !computed.Contains(n.Urn))
                    .Select(n => n.RefId is { } refId && refCounts.GetValueOrDefault(refId) == 1
                        ? new CapabilityRow("ref", refId, observation) { ProvidedBy = providers }
                        : new CapabilityRow("urn", n.Urn, observation) { ProvidedBy = providers }));
            }

            if (rows.Count > 0 || leftToMapping.Count > 0)
            {
                perAudit.Add(new AuditCapabilityRows(audit.RefId, audit.At.File, rows) { Computed = leftToMapping });
            }
        }

        var gaps = mitigations
            .Where(m => decisions.GetValueOrDefault(m.Code)?.ProvidedBy == CapabilityProvision.Gap)
            .Select(m => new CapabilityGap(m.Code, [.. m.Requirements.Select(n => n.RefId ?? n.Urn)]));
        return new(perAudit, [.. gaps]);
    }
}
