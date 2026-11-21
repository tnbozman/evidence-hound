using System.Globalization;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// The objects a scope declares, as the plan compares them with CISO Assistant. Pure. Each field is text and is left out when empty; lists are
/// sorted and joined with ", " so their order in the file does not matter.
/// </summary>
/// <remarks>
/// Keys: a framework by URN, a domain and a system by name, a perimeter as <c>domain/name</c>, an audit and a control by <c>ref_id</c>, a
/// document by its id (used as the evidence's <c>ref_id</c>), and a hand-written assessment as <c>audit/requirement</c>. A document declared
/// by several audits is one object, from its first declaration.
/// </remarks>
public static class ScopeDesired
{
    public static IReadOnlyList<ScopeObject> From(ScopeDocument scope) => From(scope, ScopeInputs.None);

    public static IReadOnlyList<ScopeObject> From(ScopeDocument scope, ScopeInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(inputs);
        scope = scope with { Audits = [.. scope.Audits.SelectMany(a => a.Cycle is null ? [a] : Periods(a, a.Cycle, inputs.Today))] };
        var setup = scope.Setup;
        return
        [
            .. setup.Frameworks.Select(f => Framework(f, inputs)),
            .. setup.Libraries.Select(l => new ScopeObject(ScopeKind.Framework, l.Value, Fields()) { Initial = Fields(("library", l.Value)) }),
            .. setup.Domains.Select(d => new ScopeObject(ScopeKind.Domain, d.Name, Fields(("description", d.Description)))),
            .. setup.Domains.SelectMany(d => d.Perimeters.Select(p => new ScopeObject(
                ScopeKind.Perimeter, PerimeterKey(d.Name, p.Name), Fields(("name", p.Name), ("ref_id", p.RefId), ("description", p.Description), ("domain", d.Name))))),
            .. setup.Domains.SelectMany(d => d.Systems.Select(s => new ScopeObject(ScopeKind.System, s.Name, Fields(
                ("ref_id", s.RefId), ("type", s.Type == SystemType.Primary ? "primary" : "supporting"), ("description", s.Description), ("owner", s.Owner),
                ("domain", d.Name), ("parents", Joined(s.Parents.Select(p => p.Value))))))),
            .. scope.Audits.Select(a => new ScopeObject(ScopeKind.Audit, a.RefId, Fields(
                ("name", a.Name), ("framework", a.Framework.Value), ("groups", Joined(a.Groups.Select(g => g.Value))), ("domain", a.Domain.Value),
                ("perimeter", a.Perimeter.Value), ("eta", a.Eta), ("due_date", a.DueDate), ("authors", Joined(a.Authors)), ("reviewers", Joined(a.Reviewers)),
                ("status", a.Period?.Status), ("locked", a.Period is { Locked: true } ? "true" : null)))
            {
                Initial = Fields(("baseline", a.Period?.Baseline)),
            }),
            .. scope.Audits.SelectMany(a => a.Documents.Select(d => (Audit: a, Document: d))).DistinctBy(x => x.Document.Id).Select(x => Document(x.Audit, x.Document, inputs)),
            .. scope.Controls.Select(c => new ScopeObject(ScopeKind.Control, c.RefId, Fields(
                ("name", c.Name), ("description", c.Description), ("domain", c.Domain?.Value), ("owner", c.Owner), ("priority", c.Priority), ("category", c.Category),
                ("eta", c.Eta), ("systems", Joined(c.Systems.Select(s => s.Value))), ("documents", Joined(c.Documents.Select(d => d.Value)))))
            {
                Initial = Fields(("status", c.Status), ("reference_control", c.ReferenceControl)),
            }),
            .. scope.Audits.SelectMany(a => Rows(scope, a, inputs)),
        ];
    }

    /// <summary>A framework, with its version and library URN from its snapshot, and its custom library file to upload.</summary>
    private static ScopeObject Framework(ScopeFramework framework, ScopeInputs inputs)
    {
        var snapshot = inputs.Snapshots.GetValueOrDefault(framework.Urn);
        return new ScopeObject(ScopeKind.Framework, framework.Urn, Fields(("version", snapshot?.Version.ToString(CultureInfo.InvariantCulture))))
        {
            Initial = Fields(("library", framework.LibraryUrn ?? snapshot?.LibraryUrn), ("file", framework.LibraryFile)),
            Files = framework.LibraryFile is null ? [] : inputs.Files.GetValueOrDefault(framework.Urn) ?? [],
        };
    }

    /// <summary>
    /// An audit with a cycle as its period audits, from the first period to the one open on <paramref name="today"/>. The open period carries the
    /// file's documents, controls and assessments; an ended one is in review until it closes (signed off in the file, or at once with
    /// <c>close: auto</c>), when it is done and locked. Each period opens from the previous one as its baseline.
    /// </summary>
    private static IEnumerable<ScopeAudit> Periods(ScopeAudit audit, AuditCycle cycle, DateOnly? today)
    {
        var periods = today is { } day ? AuditPeriods.Until(audit.RefId, cycle, day) : [];
        var signed = audit.SignOffs.Select(s => s.Period).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < periods.Count; i++)
        {
            var period = periods[i];
            var open = i == periods.Count - 1;
            var closed = !open && (cycle.Close == "auto" || signed.Contains(period.Label));
            var state = new PeriodState(audit.RefId, open ? "in_progress" : closed ? "done" : "in_review", closed, i > 0 ? periods[i - 1].RefId : null);
            var periodAudit = audit with
            {
                RefId = period.RefId,
                Name = $"{audit.Name} {period.Label}",
                Eta = period.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Period = state,
            };
            yield return open ? periodAudit : periodAudit with { Documents = [], Controls = [], Assessments = [] };
        }
    }

    private static ScopeObject Document(ScopeAudit audit, ScopeEvidenceDocument document, ScopeInputs inputs)
    {
        var files = inputs.Files.GetValueOrDefault(document.Id) ?? [];
        return new ScopeObject(ScopeKind.Document, document.Id, Fields(("name", document.Name), ("domain", audit.Domain.Value), ("hashes", Joined(files.Select(f => f.Sha256).Distinct()))))
        {
            Files = files,
        };
    }

    /// <summary>
    /// The rows of one audit the scope writes: hand-written results, the controls and documents linked to the rows they cite, and, when the
    /// deployment's targets are known, the <c>unlisted</c> rule for every other in-group requirement that no mapping computes. A document is
    /// linked in the audit that declares it and in every audit of the same framework that shares one of its systems.
    /// </summary>
    private static IEnumerable<ScopeObject> Rows(ScopeDocument scope, ScopeAudit audit, ScopeInputs inputs)
    {
        if (audit.Period is { Status: not "in_progress" })
        {
            return [];
        }

        var rows = new Dictionary<string, Dictionary<string, string?>>(StringComparer.Ordinal);
        Dictionary<string, string?> Row(string requirement) =>
            rows.TryGetValue(requirement, out var row) ? row : rows[requirement] = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var hand in audit.Assessments)
        {
            var row = Row(hand.Requirement.Key.Value);
            (row["status"], row["result"], row["observation"]) = (hand.Status, Names.Of(hand.Result), hand.Observation);
        }

        var links = audit.Controls.SelectMany(c => c.Requirements.Select(r => (Field: "controls", Requirement: r.Key.Value, Value: c.Control.Value)))
            .Concat(DocumentsCiting(scope, audit).SelectMany(d => d.Requirements.Select(r => (Field: "documents", Requirement: r.Key.Value, Value: d.Id))));
        foreach (var link in links.GroupBy(l => (l.Requirement, l.Field)))
        {
            Row(link.Key.Requirement)[link.Key.Field] = Joined(link.Select(l => l.Value).Distinct());
        }

        foreach (var (requirement, fields) in Unlisted(audit, inputs))
        {
            var row = Row(requirement);
            foreach (var (name, value) in fields)
            {
                row[name] = value;
            }
        }

        return rows.Select(r => new ScopeObject(ScopeKind.Assessment, $"{audit.RefId}/{r.Key}", Fields([.. r.Value.Select(f => (f.Key, f.Value))])));
    }

    private static IEnumerable<ScopeEvidenceDocument> DocumentsCiting(ScopeDocument scope, ScopeAudit audit)
    {
        var systems = audit.Systems.Select(s => s.Value).ToHashSet(StringComparer.Ordinal);
        return scope.Audits
            .Where(other => other == audit || other.Framework.Value == audit.Framework.Value)
            .SelectMany(other => other.Documents.Where(d => other == audit || d.Systems.Any(s => systems.Contains(s.Value))))
            .DistinctBy(d => d.Id);
    }

    /// <summary>The <c>unlisted</c> rule's fields for each in-group requirement that neither the file lists nor a mapping computes.</summary>
    private static IEnumerable<(string Requirement, (string Name, string? Value)[] Fields)> Unlisted(ScopeAudit audit, ScopeInputs inputs)
    {
        if (inputs.Targets is null || audit.Unlisted is not { } rule || inputs.Snapshots.GetValueOrDefault(audit.Framework.Value) is not { } snapshot)
        {
            return [];
        }

        var groups = audit.Groups.Select(g => g.Value).ToHashSet(StringComparer.Ordinal);
        RequirementKey[] covered =
        [
            .. audit.Assessments.Select(h => h.Requirement.Key),
            .. inputs.Targets.Where(t => t.AuditRefId == (audit.Period?.Series ?? audit.RefId)).SelectMany(t => t.Computed),
        ];
        var repeated = snapshot.Nodes.Where(n => n.RefId is not null).GroupBy(n => n.RefId!, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        return snapshot.Nodes
            .Where(n => groups.Count == 0 || n.Groups.Count == 0 || n.Groups.Any(groups.Contains))
            .Where(n => !covered.Any(k => k.Kind == RequirementKeyKind.Urn ? k.Value == n.Urn : k.Value == n.RefId))
            .Select(n => (n.RefId is { } refId && !repeated.Contains(refId) ? refId : n.Urn, new[] { ("result", (string?)Names.Of(rule.Result)), ("observation", rule.Reason) }));
    }

    /// <summary>A perimeter's key: its domain and name, because names are unique only within a domain.</summary>
    public static string PerimeterKey(string domain, string name) => $"{domain}/{name}";

    /// <summary>A hand-written assessment's key: the audit's <c>ref_id</c> and the requirement as the file names it.</summary>
    public static string AssessmentKey(string auditRefId, RequirementKey requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        return $"{auditRefId}/{requirement.Value}";
    }

    /// <summary>A list as one field value: sorted, joined with ", ", and null when empty.</summary>
    public static string? Joined(IEnumerable<string> values) =>
        values.Order(StringComparer.Ordinal).ToArray() is { Length: > 0 } sorted ? string.Join(", ", sorted) : null;

    private static Dictionary<string, string> Fields(params (string Name, string? Value)[] fields) =>
        fields.Where(f => !string.IsNullOrEmpty(f.Value)).ToDictionary(f => f.Name, f => f.Value!, StringComparer.Ordinal);
}

/// <summary>
/// What the scope's objects need from outside the scope files: the framework snapshots by framework URN, the deployment's targets (null when no
/// deployment config was given, so the rows a mapping computes are unknown and <c>unlisted</c> is not applied), and the files to upload: each
/// document's by its id, and a custom library file by its framework's URN.
/// </summary>
public sealed record ScopeInputs(
    IReadOnlyDictionary<string, FrameworkSnapshot> Snapshots, IReadOnlyList<ScopeTarget>? Targets, IReadOnlyDictionary<string, IReadOnlyList<ScopeFile>> Files)
{
    public static ScopeInputs None { get; } = new(new Dictionary<string, FrameworkSnapshot>(), null, new Dictionary<string, IReadOnlyList<ScopeFile>>());

    /// <summary>The day the plan is made, which decides the open period of each audit with a cycle; null opens none.</summary>
    public DateOnly? Today { get; init; }
}
