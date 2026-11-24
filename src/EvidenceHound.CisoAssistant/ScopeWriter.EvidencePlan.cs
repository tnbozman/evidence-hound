using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>
/// The evidence plan: an evidence item per row, owned by a team and labelled with its method and collector, and for each row a person
/// provides, a recurring task template assigned to the owner and the GRC team. Teams are assigned through their actors. <c>hound</c> writes
/// the templates only; CISO Assistant makes their occurrences, and nothing here writes an occurrence's status.
/// </summary>
internal sealed partial class ScopeWriter
{
    private async Task<Result<Unit>> EvidenceAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var listed = await http.ListAllAsync("evidences/", cancellationToken);
        return await listed.BindAsync(async evidences =>
        {
            var existing = IdByField(evidences, "ref_id").GetValueOrDefault(write.Change.Key);
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("evidences", existing, write, cancellationToken);
            }

            var folder = Folder(folders, declared);
            var owners = await TeamActorsAsync(Split(Field(declared, "owner")), cancellationToken);
            var controls = await IdsAsync("applied-controls/", "ref_id", Split(Field(declared, "controls")), "applied control", cancellationToken);
            var labels = await LabelsAsync(Split(Field(declared, "labels")), cancellationToken);
            return await Result.Combine([folder.Map(Node), owners.Map(o => (JsonNode)o), controls.Map(c => (JsonNode)c), labels.Map(l => (JsonNode)l)]).BindAsync(async resolved =>
            {
                var body = new JsonObject
                {
                    ["ref_id"] = declared.Key,
                    ["name"] = Field(declared, "name"),
                    ["description"] = Described(declared, write.Change),
                    ["folder"] = resolved[0].DeepClone(),
                    ["owner"] = resolved[1].DeepClone(),
                    ["applied_controls"] = resolved[2].DeepClone(),
                    ["filtering_labels"] = resolved[3].DeepClone(),
                };
                return write.Change.Action == ScopeAction.Create
                    ? await SendAsync(HttpMethod.Post, "evidences/", body, $"to create evidence item {declared.Key}", cancellationToken)
                    : await UpdateAsync("evidences", existing, body, write, cancellationToken);
            });
        });
    }

    private async Task<Result<Unit>> TaskTemplateAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var listed = await http.ListAllAsync("task-templates/", cancellationToken);
        return await listed.BindAsync(async templates =>
        {
            var existing = IdByField(templates, "ref_id").GetValueOrDefault(write.Change.Key);
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("task-templates", existing, write, cancellationToken);
            }

            var folder = Folder(folders, declared);
            var assignees = await TeamActorsAsync(Split(Field(declared, "assignees")), cancellationToken);
            var evidence = await IdsAsync("evidences/", "ref_id", Split(Field(declared, "evidence")), "evidence item", cancellationToken);
            var controls = await IdsAsync("applied-controls/", "ref_id", Split(Field(declared, "controls")), "applied control", cancellationToken);
            var rows = await RowIdsAsync(Split(Field(declared, "requirements")), cancellationToken);
            var schedule = Schedule(Field(declared, "schedule"));
            return await Result.Combine([folder.Map(Node), assignees.Map(a => (JsonNode)a), evidence.Map(e => (JsonNode)e), controls.Map(c => (JsonNode)c), rows.Map(r => (JsonNode)r), schedule.Map(s => (JsonNode)s)])
                .BindAsync(async resolved =>
                {
                    var body = new JsonObject
                    {
                        ["ref_id"] = declared.Key,
                        ["name"] = Field(declared, "name"),
                        ["description"] = Described(declared, write.Change),
                        ["folder"] = resolved[0].DeepClone(),
                        ["assigned_to"] = resolved[1].DeepClone(),
                        ["evidences"] = resolved[2].DeepClone(),
                        ["applied_controls"] = resolved[3].DeepClone(),
                        ["requirement_assessments"] = resolved[4].DeepClone(),
                        ["is_recurrent"] = true,
                        ["schedule"] = resolved[5].DeepClone(),
                        ["enabled"] = Field(declared, "enabled") != "false",
                    };
                    if (write.Change.Action == ScopeAction.Create)
                    {
                        // The first due date is set once; CISO Assistant moves the schedule on from there.
                        body["task_date"] = declared.Initial.GetValueOrDefault("first_due");
                        Drop(body, "task_date");
                        return await SendAsync(HttpMethod.Post, "task-templates/", body, $"to create task {declared.Key}", cancellationToken);
                    }

                    return await UpdateAsync("task-templates", existing, body, write, cancellationToken);
                });
        });
    }

    /// <summary>A schedule as CISO Assistant holds it, from "every 1 week" or "every N months".</summary>
    private static Result<JsonObject> Schedule(string? every)
    {
        if (EvidenceInterval.Parse(every) is not { } interval)
        {
            return Result.Fail<JsonObject>(new Problem("scope.value_invalid", $"'{every}' is not a task schedule; use every 1 week, or every 3, 6 or 12 months."));
        }

        var schedule = TaskSchedules.Of(interval);
        return Result.Ok(new JsonObject
        {
            ["interval"] = schedule.Interval,
            ["frequency"] = schedule.Frequency,
            ["days_of_week"] = new JsonArray(),
            ["weeks_of_month"] = new JsonArray(),
            ["months_of_year"] = new JsonArray(),
        });
    }

    /// <summary>Teams named by name, as the IDs of their actors: owners and assignees are actors in CISO Assistant.</summary>
    private async Task<Result<JsonArray>> TeamActorsAsync(string[] teams, CancellationToken cancellationToken)
    {
        if (teams.Length == 0)
        {
            return Result.Ok(new JsonArray());
        }

        var listed = await ListAsync(["teams/", "actors/"], cancellationToken);
        return listed.Bind(lists =>
        {
            var names = lists[0].ToDictionary(Id, t => Text(t["name"]) ?? Id(t), StringComparer.Ordinal);
            var actors = lists[1].Where(a => Text(a["type"]) == "team" && RelationId(a["specific"]) is { } team && names.ContainsKey(team))
                .GroupBy(a => names[RelationId(a["specific"])!], StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => Id(g.First()), StringComparer.Ordinal);
            return Resolve(teams, actors, "team");
        });
    }

    /// <summary>Filtering labels by text, made when missing.</summary>
    private async Task<Result<JsonArray>> LabelsAsync(string[] labels, CancellationToken cancellationToken)
    {
        if (labels.Length == 0)
        {
            return Result.Ok(new JsonArray());
        }

        var listed = await http.ListAllAsync("filtering-labels/", cancellationToken);
        return await listed.BindAsync(async all =>
        {
            var ids = IdByField(all, "label");
            var found = new JsonArray();
            foreach (var label in labels)
            {
                if (ids.TryGetValue(label, out var id))
                {
                    found.Add(id);
                    continue;
                }

                var made = await SendForJsonAsync(HttpMethod.Post, "filtering-labels/", new JsonObject { ["label"] = label }, $"to create label {label}", cancellationToken);
                if (made.Match<string?>(m => Text(m["id"]), _ => null) is not { } madeId)
                {
                    return made.Map(_ => new JsonArray());
                }

                found.Add(madeId);
            }

            return Result.Ok(found);
        });
    }

    /// <summary>The rows of each <c>AUDIT/REF</c> in their audit's selected groups.</summary>
    private async Task<Result<JsonArray>> RowIdsAsync(string[] keys, CancellationToken cancellationToken)
    {
        var ids = new JsonArray();
        foreach (var key in keys)
        {
            if (key.Split('/', 2) is not [var audit, var requirement])
            {
                return Result.Fail<JsonArray>(ApiProblems.NotFound("row", $"'{key}'"));
            }

            var requirementKey = requirement.StartsWith("urn:", StringComparison.Ordinal) ? RequirementKey.Urn(requirement) : RequirementKey.Ref(requirement, allRows: true);
            var found = await client.FindAuditAsync(audit, cancellationToken);
            var rows = await found.BindAsync(async a =>
                (await client.FindRequirementAssessmentsAsync(audit, requirementKey, cancellationToken)).Bind(all => RequirementRows.InScope(requirementKey, all, a.SelectedGroups)));
            if (rows.Match<IReadOnlyList<RemoteRequirementAssessment>?>(r => r, _ => null) is not { } inScope)
            {
                return rows.Map(_ => new JsonArray());
            }

            foreach (var row in inScope)
            {
                ids.Add(row.Id);
            }
        }

        return Result.Ok(ids);
    }
}
