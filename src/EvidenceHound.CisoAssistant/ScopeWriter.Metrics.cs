using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>
/// Metric definitions, metric instances, dashboards and their widgets (s16-02), under CISO Assistant's <c>metrology/</c> API. Units are
/// terminologies found by name; a definition is named by <c>ref_id</c>, or by URN when a library loaded it.
/// </summary>
internal sealed partial class ScopeWriter
{
    private const string Definitions = "metrology/metric-definitions";
    private const string Instances = "metrology/metric-instances";
    private const string Dashboards = "metrology/dashboards";
    private const string Widgets = "metrology/dashboard-widgets";

    private async Task<Result<Unit>> MetricDefinitionAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var listed = await http.ListAllAsync($"{Definitions}/", cancellationToken);
        return await listed.BindAsync(async definitions =>
        {
            var ids = IdByRefId(definitions, ScopeKind.MetricDefinition);
            var existing = ids.GetValueOrDefault(write.Change.Key);
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync(Definitions, existing, write, cancellationToken);
            }

            var levels = Split(Field(declared, "levels"));
            var unit = Field(declared, "unit") is { } named
                ? (await http.ListAllAsync("terminologies/?field_path=metric_definition.unit", cancellationToken))
                    .Bind(all => IdByField(all, "name").GetValueOrDefault(named) is { } id ? Result.Ok(id) : Result.Fail<string>(ApiProblems.NotFound("metric unit", $"'{named}'")))
                : Result.Ok("");
            var inputs = Inputs(Field(declared, "inputs"), ids);
            return await Result.Combine([folders.FolderOf(declared).Map(Node), unit.Map(Node), inputs.Map(i => (JsonNode)i)]).BindAsync(async resolved =>
            {
                var body = new JsonObject
                {
                    ["ref_id"] = declared.Key,
                    ["name"] = Field(declared, "name"),
                    ["description"] = Described(declared, write.Change),
                    ["folder"] = resolved[0].DeepClone(),
                    ["category"] = levels.Length > 0 ? "qualitative" : "quantitative",
                    ["unit"] = resolved[1].GetValue<string>().Length > 0 ? resolved[1].DeepClone() : null,
                    ["choices_definition"] = levels.Length > 0 ? new JsonArray([.. levels.Select(l => (JsonNode)new JsonObject { ["ref_id"] = Slug(l), ["name"] = l })]) : null,
                    ["higher_is_better"] = Field(declared, "direction") switch { "higher_is_better" => true, "lower_is_better" => false, _ => null },
                    ["decimals"] = Whole(Field(declared, "decimals")),
                    ["default_collection_frequency"] = Field(declared, "frequency"),
                    ["default_target"] = TargetValue(Field(declared, "target"), levels),
                    ["expression"] = Field(declared, "expression") ?? "",
                    ["inputs"] = resolved[2].AsArray().Count > 0 ? resolved[2].DeepClone() : null,
                };
                return write.Change.Action == ScopeAction.Create
                    ? await SendAsync(HttpMethod.Post, $"{Definitions}/", body, $"to create metric definition {declared.Key}", cancellationToken)
                    : await UpdateAsync(Definitions, existing, body, write, cancellationToken);
            });
        });
    }

    private async Task<Result<Unit>> MetricInstanceAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var listed = await http.ListAllAsync($"{Instances}/", cancellationToken);
        return await listed.BindAsync(async instances =>
        {
            var existing = IdByRefId(instances, ScopeKind.MetricInstance).GetValueOrDefault(write.Change.Key);
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync(Instances, existing, write, cancellationToken);
            }

            var named = Field(declared, "definition") ?? "";
            var definition = (await http.ListAllAsync($"{Definitions}/", cancellationToken)).Bind(all =>
                all.FirstOrDefault(d => named.StartsWith("urn:", StringComparison.OrdinalIgnoreCase)
                    ? string.Equals(Text(d["urn"]), named, StringComparison.OrdinalIgnoreCase)
                    : Text(d["ref_id"]) == named) is { } found
                    ? Result.Ok(found)
                    : Result.Fail<JsonNode>(ApiProblems.NotFound("metric definition", $"'{named}'")));
            var owners = await ActorsAsync(Split(Field(declared, "owners")), cancellationToken);
            var controls = await IdsAsync("applied-controls/", "ref_id", Split(Field(declared, "controls")), "applied control", cancellationToken);
            return await Result.Combine([folders.FolderOf(declared).Map(Node), definition, owners.Map(o => (JsonNode)o), controls.Map(c => (JsonNode)c)]).BindAsync(async resolved =>
            {
                var levels = (resolved[1]["choices_definition"] as JsonArray ?? []).Select(c => Text(c?["name"]) ?? "").ToArray();
                var body = new JsonObject
                {
                    ["ref_id"] = declared.Key,
                    ["name"] = Field(declared, "name"),
                    ["description"] = Described(declared, write.Change),
                    ["folder"] = resolved[0].DeepClone(),
                    ["metric_definition"] = Id(resolved[1]),
                    ["collection_frequency"] = Field(declared, "frequency"),
                    ["collection_grace_days"] = Whole(Field(declared, "grace_days")),
                    ["target_value"] = TargetValue(Field(declared, "target"), levels),
                    ["owner"] = resolved[2].DeepClone(),
                    ["applied_controls"] = resolved[3].DeepClone(),
                };
                if (write.Change.Action == ScopeAction.Create)
                {
                    // A metric made through the API starts as a draft; one the repo declares is in use.
                    body["status"] = "active";
                    return await SendAsync(HttpMethod.Post, $"{Instances}/", body, $"to create metric instance {declared.Key}", cancellationToken);
                }

                return await UpdateAsync(Instances, existing, body, write, cancellationToken);
            });
        });
    }

    private async Task<Result<Unit>> DashboardAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var listed = await http.ListAllAsync($"{Dashboards}/", cancellationToken);
        return await listed.BindAsync(async dashboards =>
        {
            var existing = IdByRefId(dashboards, ScopeKind.Dashboard).GetValueOrDefault(write.Change.Key);
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync(Dashboards, existing, write, cancellationToken);
            }

            return await folders.FolderOf(declared).BindAsync(async folder =>
            {
                var body = new JsonObject
                {
                    ["ref_id"] = declared.Key,
                    ["name"] = Field(declared, "name"),
                    ["description"] = Described(declared, write.Change),
                    ["folder"] = folder,
                };
                return write.Change.Action == ScopeAction.Create
                    ? await SendAsync(HttpMethod.Post, $"{Dashboards}/", body, $"to create dashboard {declared.Key}", cancellationToken)
                    : await UpdateAsync(Dashboards, existing, body, write, cancellationToken);
            });
        });
    }

    /// <summary>A widget, keyed <c>dashboard/title</c>, in its dashboard's folder: a metric instance, a builtin metric of an object, or text.</summary>
    private async Task<Result<Unit>> DashboardWidgetAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var (dashboardRef, title) = Split2(write.Change.Key);
        var dashboard = (await http.ListAllAsync($"{Dashboards}/", cancellationToken))
            .Map(all => all.Where(d => Text(d["ref_id"]) == dashboardRef).Take(1).ToArray());
        return await dashboard.BindAsync(async found =>
        {
            if (found is not [var held])
            {
                return write.Change.Action == ScopeAction.Delete ? Result.Ok(Unit.Value) : Result.Fail<Unit>(ApiProblems.NotFound("dashboard", $"'{dashboardRef}'"));
            }

            var listed = await http.ListAllAsync($"{Widgets}/", cancellationToken);
            return await listed.BindAsync(async widgets =>
            {
                var existing = widgets.FirstOrDefault(w => RelationId(w["dashboard"]) == Id(held) && Text(w["title"]) == title) is { } widget ? Id(widget) : null;
                if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
                {
                    return await DeleteAsync(Widgets, existing, write, cancellationToken);
                }

                var metric = Field(declared, "metric") is { } instance
                    ? (await IdsAsync($"{Instances}/", "ref_id", [instance], "metric instance", cancellationToken)).Map(ids => ids[0]!.DeepClone())
                    : Result.Ok<JsonNode>(JsonValue.Create("")!);
                var builtin = Field(declared, "builtin")?.Split(' ', 3) is [var of, var target, var key]
                    ? (await BuiltinTargetAsync(of, target, folders, cancellationToken)).Map(id => new[] { of, id, key })
                    : Result.Ok(Array.Empty<string>());
                return await metric.BindAsync(m => builtin.BindAsync(async b =>
                {
                    var position = Split(Field(declared, "position")).Select(p => int.Parse(p, NumberStyles.None, CultureInfo.InvariantCulture)).ToArray();
                    var body = new JsonObject
                    {
                        ["dashboard"] = Id(held),
                        ["folder"] = RelationId(held["folder"]),
                        ["title"] = title,
                        ["metric_instance"] = m.GetValue<string>().Length > 0 ? m.DeepClone() : null,
                        ["target_model"] = b.Length == 3 ? WidgetBuiltin.Models[b[0]] : null,
                        ["target_object_id"] = b.ElementAtOrDefault(1),
                        ["metric_key"] = b.ElementAtOrDefault(2),
                        ["text_content"] = Field(declared, "text"),
                        ["chart_type"] = Field(declared, "chart"),
                        ["time_range"] = Field(declared, "time_range"),
                        ["aggregation"] = Field(declared, "aggregation"),
                        ["position_x"] = position.ElementAtOrDefault(0),
                        ["position_y"] = position.ElementAtOrDefault(1),
                        ["width"] = position.ElementAtOrDefault(2),
                        ["height"] = position.ElementAtOrDefault(3),
                    };
                    if (b.Length == 0)
                    {
                        // Clears a builtin the widget showed before, so CISO Assistant does not see both kinds at once.
                        body["target_content_type"] = null;
                    }

                    return write.Change.Action == ScopeAction.Create
                        ? await SendAsync(HttpMethod.Post, $"{Widgets}/", body, $"to create dashboard widget {declared.Key}", cancellationToken)
                        : await UpdateAsync(Widgets, existing, body, write, cancellationToken);
                }));
            });
        });
    }

    /// <summary>The object a builtin widget reads: an audit or risk study by <c>ref_id</c>, or a domain (or the root folder) by name.</summary>
    private async Task<Result<string>> BuiltinTargetAsync(string of, string target, Folders folders, CancellationToken cancellationToken) => of switch
    {
        "audit" => (await IdsAsync("compliance-assessments/", "ref_id", [target], "audit", cancellationToken)).Map(ids => ids[0]!.GetValue<string>()),
        "risk_study" => (await IdsAsync("risk-assessments/", "ref_id", [target], "risk study", cancellationToken)).Map(ids => ids[0]!.GetValue<string>()),
        _ => folders.Of(target) is { } id ? Result.Ok(id) : Result.Fail<string>(ApiProblems.NotFound("domain", $"'{target}'")),
    };

    /// <summary>A formula's inputs, from <c>key=definition combine</c>: a declared definition by its ID, a library one by its URN.</summary>
    private static Result<JsonArray> Inputs(string? joined, Dictionary<string, string> definitions)
    {
        var inputs = Split(joined).Select(i => i.Split('=', 2) is [var key, var rest] && rest.Split(' ', 2) is [var definition, var combine] ? (key, definition, combine) : (key: i, definition: "", combine: "one")).ToArray();
        var missing = inputs.Where(i => !i.definition.StartsWith("urn:", StringComparison.OrdinalIgnoreCase) && !definitions.ContainsKey(i.definition)).ToArray();
        return missing.Length > 0
            ? Result.Fail<JsonArray>(missing.Select(i => ApiProblems.NotFound("metric definition", $"'{i.definition}'")))
            : Result.Ok(new JsonArray([.. inputs.Select(i => (JsonNode)new JsonObject
            {
                ["key"] = i.key,
                ["definition"] = i.definition.StartsWith("urn:", StringComparison.OrdinalIgnoreCase) ? i.definition : definitions[i.definition],
                ["combine"] = i.combine,
            })]));
    }

    /// <summary>A target as CISO Assistant holds it: a number, or a level's position counted from 1.</summary>
    private static double? TargetValue(string? target, string[] levels) =>
        target is null
            ? null
            : levels.Length > 0
                ? Array.IndexOf(levels, target) is var index and >= 0 ? index + 1 : null
                : double.TryParse(target, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static int? Whole(string? text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>A level's ref_id from its name: lower case, words joined by dashes.</summary>
    private static string Slug(string name)
    {
        var slug = new StringBuilder(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            slug.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        }

        return slug.ToString().Trim('-');
    }
}
