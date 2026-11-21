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
    public static IReadOnlyList<ScopeObject> From(ScopeDocument scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var setup = scope.Setup;
        return
        [
            .. setup.Frameworks.Select(f => new ScopeObject(ScopeKind.Framework, f.Urn, Fields()) { Initial = Fields(("library", f.LibraryUrn), ("file", f.LibraryFile)) }),
            .. setup.Domains.Select(d => new ScopeObject(ScopeKind.Domain, d.Name, Fields(("description", d.Description)))),
            .. setup.Domains.SelectMany(d => d.Perimeters.Select(p => new ScopeObject(
                ScopeKind.Perimeter, PerimeterKey(d.Name, p.Name), Fields(("name", p.Name), ("ref_id", p.RefId), ("description", p.Description), ("domain", d.Name))))),
            .. setup.Domains.SelectMany(d => d.Systems.Select(s => new ScopeObject(ScopeKind.System, s.Name, Fields(
                ("ref_id", s.RefId), ("type", s.Type == SystemType.Primary ? "primary" : "supporting"), ("description", s.Description), ("owner", s.Owner),
                ("domain", d.Name), ("parents", Joined(s.Parents.Select(p => p.Value))))))),
            .. scope.Audits.Select(a => new ScopeObject(ScopeKind.Audit, a.RefId, Fields(
                ("name", a.Name), ("framework", a.Framework.Value), ("groups", Joined(a.Groups.Select(g => g.Value))), ("domain", a.Domain.Value),
                ("perimeter", a.Perimeter.Value), ("eta", a.Eta), ("due_date", a.DueDate), ("authors", Joined(a.Authors)), ("reviewers", Joined(a.Reviewers))))),
            .. scope.Audits.SelectMany(a => a.Documents.Select(d => (Audit: a, Document: d))).DistinctBy(x => x.Document.Id).Select(x => new ScopeObject(
                ScopeKind.Document, x.Document.Id, Fields(("name", x.Document.Name), ("domain", x.Audit.Domain.Value)))),
            .. scope.Controls.Select(c => new ScopeObject(ScopeKind.Control, c.RefId, Fields(
                ("name", c.Name), ("description", c.Description), ("domain", c.Domain?.Value), ("owner", c.Owner), ("priority", c.Priority), ("category", c.Category),
                ("eta", c.Eta), ("systems", Joined(c.Systems.Select(s => s.Value))), ("documents", Joined(c.Documents.Select(d => d.Value)))))
            {
                Initial = Fields(("status", c.Status), ("reference_control", c.ReferenceControl)),
            }),
            .. scope.Audits.SelectMany(a => a.Assessments.Select(h => new ScopeObject(ScopeKind.Assessment, AssessmentKey(a.RefId, h.Requirement.Key), Fields(
                ("status", h.Status), ("result", Names.Of(h.Result)), ("observation", h.Observation))))),
        ];
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
