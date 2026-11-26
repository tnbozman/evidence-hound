using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>Metric definitions and instances (s16).</summary>
internal sealed partial class ScopeStateReader
{
    /// <summary>
    /// The metric definitions (not those a library loaded), instances and dashboards under the root folder (s16-02), each dashboard's
    /// widgets, and the risk studies a builtin widget may read.
    /// </summary>
    private static async Task<MetricLists> MetricsAsync(Reading reading, HashSet<string> inScope)
    {
        bool InScope(JsonNode o) => RelationId(o["folder"]) is { } f && inScope.Contains(f);
        var definitions = await reading.ListAsync("metrology/metric-definitions/");
        var dashboards = (await reading.ListAsync("metrology/dashboards/")).Where(InScope).ToList();
        var dashboardIds = dashboards.Select(Id).ToHashSet(StringComparer.Ordinal);
        var widgets = dashboards.Count == 0 ? [] : (await reading.ListAsync("metrology/dashboard-widgets/")).Where(w => RelationId(w["dashboard"]) is { } d && dashboardIds.Contains(d)).ToList();
        var studies = widgets.Any(w => Text(w["target_content_type_display"]) == WidgetBuiltin.Models["risk_study"]) ? Names(await reading.ListAsync("risk-assessments/"), "ref_id") : [];
        return new MetricLists(definitions, (await reading.ListAsync("metrology/metric-instances/")).Where(InScope).ToList(), dashboards, widgets, studies, inScope);
    }

    private static IEnumerable<ActualObject> MetricObjects(MetricLists metrics, Lookups names, Dictionary<string, string> audits)
    {
        var definitions = metrics.Definitions.ToDictionary(Id, StringComparer.Ordinal);
        string? DefinitionName(string? id) =>
            id is null ? null : definitions.TryGetValue(id, out var d) ? Text(d["ref_id"]) ?? Text(d["urn"]) ?? id : id;
        string[] Levels(JsonNode? definition) => [.. (definition?["choices_definition"] as JsonArray ?? []).Select(c => Text(c?["name"]) ?? "")];
        var dashboards = metrics.Dashboards.ToDictionary(Id, d => RefIdOrName(ScopeKind.Dashboard, d), StringComparer.Ordinal);
        var instances = metrics.Instances.ToDictionary(Id, i => RefIdOrName(ScopeKind.MetricInstance, i), StringComparer.Ordinal);
        string? Target(string of, string? id) => id is null
            ? null
            : of switch
            {
                "audit" => audits.GetValueOrDefault(id, id),
                "risk_study" => metrics.RiskStudies.GetValueOrDefault(id, id),
                _ => names.Domains.GetValueOrDefault(id) ?? (metrics.Folders.Contains(id) ? names.Root : id),
            };

        foreach (var d in metrics.Definitions.Where(d => d["library"] is not JsonObject && RelationId(d["folder"]) is { } f && metrics.Folders.Contains(f)))
        {
            var levels = Levels(d);
            yield return Marked(ScopeKind.MetricDefinition, RefIdOrName(ScopeKind.MetricDefinition, d), d,
                ("name", Text(d["name"])), ("domain", names.Domain(d)), ("unit", Text(d["unit"]?["name"])),
                ("levels", levels.Length == 0 ? null : string.Join(", ", levels)),
                ("direction", d["higher_is_better"] is JsonValue h && h.TryGetValue<bool>(out var higher) ? (higher ? "higher_is_better" : "lower_is_better") : "none"),
                ("decimals", Number(d["decimals"])), ("frequency", Text(d["default_collection_frequency"])), ("target", MetricTarget(d["default_target"], levels)),
                ("expression", Text(d["expression"])),
                ("inputs", ScopeDesired.Joined((d["inputs"] as JsonArray ?? []).Select(i => $"{Text(i?["key"])}={DefinitionName(Text(i?["definition"]))} {Text(i?["combine"]) ?? "one"}"))));
        }

        foreach (var i in metrics.Instances)
        {
            var definitionId = RelationId(i["metric_definition"]);
            yield return Marked(ScopeKind.MetricInstance, RefIdOrName(ScopeKind.MetricInstance, i), i,
                ("name", Text(i["name"])), ("definition", DefinitionName(definitionId)), ("domain", names.Domain(i)),
                ("frequency", Text(i["collection_frequency_value"])), ("grace_days", Number(i["collection_grace_days"])),
                ("target", MetricTarget(i["target_value"], Levels(definitionId is null ? null : definitions.GetValueOrDefault(definitionId)))),
                ("owners", Joined(i["owner"], names.Users)), ("controls", Joined(i["applied_controls"], names.Controls)));
        }

        foreach (var d in metrics.Dashboards)
        {
            yield return Marked(ScopeKind.Dashboard, dashboards[Id(d)], d, ("name", Text(d["name"])), ("domain", names.Domain(d)));
        }

        foreach (var w in metrics.Widgets)
        {
            var dashboard = dashboards[RelationId(w["dashboard"])!];
            var of = WidgetBuiltin.Models.FirstOrDefault(m => m.Value == Text(w["target_content_type_display"])).Key;
            var title = Text(w["title"]) ?? Id(w);
            yield return Marked(ScopeKind.DashboardWidget, Core.Scope.MetricObjects.WidgetKey(dashboard, title), w,
                ("dashboard", dashboard), ("title", title), ("metric", Lookup(w["metric_instance"], instances)),
                ("builtin", of is null ? null : Core.Scope.MetricObjects.Builtin(of, Target(of, Text(w["target_object_id"])) ?? "", Text(w["metric_key"]) ?? "")),
                ("text", Text(w["text_content"])), ("chart", Text(w["chart_type"])), ("time_range", Text(w["time_range"])), ("aggregation", Text(w["aggregation"])),
                ("position", Core.Scope.MetricObjects.Position(Int(w["position_x"]), Int(w["position_y"]), Int(w["width"]), Int(w["height"]))));
        }
    }

    /// <summary>A target as the scope writes it: a level's name (CISO Assistant counts levels from 1), or a number.</summary>
    private static string? MetricTarget(JsonNode? value, string[] levels) =>
        value is JsonValue v && v.TryGetValue<double>(out var number)
            ? levels.Length > 0
                ? (int)number is var position && position >= 1 && position <= levels.Length ? levels[position - 1] : null
                : number.ToString(CultureInfo.InvariantCulture)
            : null;

    private static string? Number(JsonNode? value) => value is JsonValue v && v.TryGetValue<int>(out var n) ? n.ToString(CultureInfo.InvariantCulture) : null;

    private static int Int(JsonNode? value) => value is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;

    private sealed record MetricLists(
        List<JsonNode> Definitions, List<JsonNode> Instances, List<JsonNode> Dashboards, List<JsonNode> Widgets, Dictionary<string, string> RiskStudies, HashSet<string> Folders)
    {
        public static MetricLists None { get; } = new([], [], [], [], [], []);
    }
}
