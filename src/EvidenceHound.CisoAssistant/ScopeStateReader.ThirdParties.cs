using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>Suppliers: entities, their solutions, contracts, assessments and tiers (s10).</summary>
internal sealed partial class ScopeStateReader
{
    /// <summary>
    /// Entities (but the organisation's own), solutions, contracts and representatives, keyed and linked by <c>ref_id</c> (representatives by
    /// email), as <see cref="ScopeDesired"/> declares them.
    /// </summary>
    private static IEnumerable<ActualObject> ThirdParties(ThirdPartyLists lists, Lookups names)
    {
        var entities = lists.Entities.Where(e => !IsBuiltin(e)).ToArray();
        var entityRefs = entities.ToDictionary(Id, e => Text(e["ref_id"]) ?? Id(e), StringComparer.Ordinal);
        var solutionRefs = lists.Solutions.ToDictionary(Id, s => Text(s["ref_id"]) ?? Id(s), StringComparer.Ordinal);
        var tiers = lists.Tiers.ToDictionary(Id, t => Text(t["key"]) ?? Text(t["name"]) ?? Id(t), StringComparer.Ordinal);
        return
        [
            .. entities.Select(e => Marked(ScopeKind.Entity, Text(e["ref_id"]), e,
                ("name", Text(e["name"])), ("mission", Text(e["mission"])), ("country", Text(e["country"])), ("currency", Text(e["currency"])),
                ("parent", Lookup(e["parent_entity"], entityRefs)), ("domain", names.Domain(e)), ("dependency", Text(e["default_dependency"])),
                ("penetration", Text(e["default_penetration"])), ("maturity", Text(e["default_maturity"])), ("trust", Text(e["default_trust"])),
                ("identifiers", Identifiers(e["legal_identifiers"]))) with
            {
                Observed = Fields(("tier", Lookup(e["tier"], tiers))),
            }),
            .. lists.Assessments.Where(a => RelationId(a["entity"]) is { } id && entityRefs.ContainsKey(id)).Select(a => Marked(ScopeKind.EntityAssessment, Text(a["ref_id"]), a,
                ("name", Text(a["name"])), ("entity", Lookup(a["entity"], entityRefs)), ("solutions", Joined(a["solutions"], solutionRefs)), ("domain", names.Domain(a)),
                ("perimeter", Lookup(a["perimeter"], names.Perimeters)), ("due_date", Text(a["due_date"])), ("criticality", Text(a["criticality"]) is "0" ? null : Text(a["criticality"])),
                ("conclusion", Choice(Text(a["conclusion"])))) with
            {
                Observed = Fields(("conclusion", Choice(Text(a["conclusion"]))), ("expiry_date", Text(a["expiry_date"]))),
            }),
            .. lists.Solutions.Where(s => RelationId(s["provider_entity"]) is { } id && entityRefs.ContainsKey(id)).Select(s => Marked(ScopeKind.Solution, Text(s["ref_id"]), s,
                ("name", Text(s["name"])), ("entity", Lookup(s["provider_entity"], entityRefs)), ("criticality", Text(s["criticality"])),
                ("labels", Joined(s["filtering_labels"], names.Labels)))),
            .. lists.Contracts.Select(c => Marked(ScopeKind.Contract, Text(c["ref_id"]), c,
                ("name", Text(c["name"])), ("entity", Lookup(c["provider_entity"], entityRefs)), ("domain", names.Domain(c)), ("status", Choice(Text(c["status"]))),
                ("start_date", Text(c["start_date"])), ("end_date", Text(c["end_date"])), ("annual_expense", ScopeDesired.Amount(Text(c["annual_expense"]))),
                ("currency", Text(c["currency"])), ("solutions", Joined(c["solutions"], solutionRefs)))),
            .. lists.Representatives.Select(r => Marked(ScopeKind.Representative, Text(r["email"])?.ToLowerInvariant(), r,
                ("entity", Lookup(r["entity"], entityRefs)), ("first_name", Text(r["first_name"])), ("last_name", Text(r["last_name"])), ("role", Text(r["role"])),
                ("phone", Text(r["phone"])))),
        ];
    }

    /// <summary>The organisation's own entity (the builtin main entity) as <see cref="ScopeDesired"/> declares it. It carries no marker.</summary>
    private static IEnumerable<ActualObject> MainEntity(List<JsonNode> entities) =>
        entities.Where(IsBuiltin).Take(1).Select(e => new ActualObject(new ScopeObject(ScopeKind.MainEntity, ScopeDesired.OrganisationKey, Fields(
            ("name", Text(e["name"])), ("description", Text(e["description"])), ("mission", Text(e["mission"])), ("country", Text(e["country"])),
            ("currency", Text(e["currency"])), ("identifiers", Identifiers(e["legal_identifiers"])))), null));

    private static bool IsBuiltin(JsonNode entity) => entity["builtin"] is JsonValue b && b.TryGetValue<bool>(out var builtin) && builtin;

    /// <summary>
    /// Legal identifiers as <see cref="ScopeDesired.Identifiers"/> writes them: a JSON object with sorted keys, or null when empty. v4.3.4's read
    /// serializer gives them as display text, one <c>name: value</c> per line; an object is read as is.
    /// </summary>
    private static string? Identifiers(JsonNode? identifiers) =>
        ScopeDesired.Identifiers(identifiers switch
        {
            JsonObject map => map.Where(i => Text(i.Value) is not null).ToDictionary(i => i.Key, i => Text(i.Value)!, StringComparer.Ordinal),
            _ => (Text(identifiers) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Split(": ", 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal),
        });

    private sealed record ThirdPartyLists(List<JsonNode> Entities, List<JsonNode> Solutions, List<JsonNode> Contracts, List<JsonNode> Representatives)
    {
        public List<JsonNode> Assessments { get; init; } = [];

        public List<JsonNode> Tiers { get; init; } = [];
    }
}
