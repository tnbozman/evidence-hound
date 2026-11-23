using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>
/// Third parties: entities, the solutions they provide, contracts and representatives, by <c>ref_id</c> (representatives by email). Each
/// finds the entity and solutions it names by <c>ref_id</c>. A representative is never given a login.
/// </summary>
internal sealed partial class ScopeWriter
{
    private async Task<Result<Unit>> EntityAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var listed = await http.ListAllAsync("entities/", cancellationToken);
        return await listed.BindAsync(async entities =>
        {
            var byRef = ByRefId(entities.Where(e => !IsBuiltin(e)));
            var existing = byRef.GetValueOrDefault(write.Change.Key);
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("entities", existing, write, cancellationToken);
            }

            var parent = Resolve(Split(Field(declared, "parent")), byRef, "third party").Map(ids => (JsonNode)ids);
            return await Result.Combine([folders.FolderOf(declared).Map(f => (JsonNode)f), parent]).BindAsync(async resolved =>
            {
                var body = new JsonObject
                {
                    ["name"] = Field(declared, "name"),
                    ["ref_id"] = declared.Key,
                    ["description"] = Described(declared, write.Change),
                    ["mission"] = Field(declared, "mission") ?? "",
                    ["country"] = Field(declared, "country") ?? "",
                    ["currency"] = Field(declared, "currency") ?? "",
                    ["folder"] = resolved[0].DeepClone(),
                    ["parent_entity"] = resolved[1].AsArray().FirstOrDefault()?.DeepClone(),
                    ["default_dependency"] = WholeNumber(declared, "dependency"),
                    ["default_penetration"] = WholeNumber(declared, "penetration"),
                    ["default_maturity"] = WholeNumber(declared, "maturity"),
                    ["default_trust"] = WholeNumber(declared, "trust"),
                    ["legal_identifiers"] = Field(declared, "identifiers") is { } identifiers ? JsonNode.Parse(identifiers) : new JsonObject(),
                };
                Drop(body, write.Change, "parent_entity", "default_dependency", "default_penetration", "default_maturity", "default_trust");
                return write.Change.Action == ScopeAction.Create
                    ? await SendAsync(HttpMethod.Post, "entities/", body, $"to create third party {declared.Key}", cancellationToken)
                    : await UpdateAsync("entities", existing, body, write, cancellationToken);
            });
        });
    }

    /// <summary>
    /// The organisation's own entity: CISO Assistant's builtin main entity, which it creates itself. It is only ever updated in place, so a
    /// create in the plan (nothing read yet) patches it too, and it carries no marker.
    /// </summary>
    private async Task<Result<Unit>> MainEntityAsync(ScopeWrite write, CancellationToken cancellationToken)
    {
        if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
        {
            return Result.Fail<Unit>(new Problem("scope.main_entity_not_deleted", "The organisation's own entity is never deleted; remove organisation.entity to stop managing it."));
        }

        var listed = await http.ListAllAsync("entities/", cancellationToken);
        return await listed.BindAsync(async entities =>
        {
            if (entities.FirstOrDefault(IsBuiltin) is not { } main)
            {
                return Result.Fail<Unit>(ApiProblems.NotFound("main entity", "(the organisation's own entity)"));
            }

            var body = new JsonObject
            {
                ["name"] = Field(declared, "name"),
                ["description"] = Field(declared, "description") ?? "",
                ["mission"] = Field(declared, "mission") ?? "",
                ["country"] = Field(declared, "country") ?? "",
                ["currency"] = Field(declared, "currency") ?? "",
                ["legal_identifiers"] = Field(declared, "identifiers") is { } identifiers ? JsonNode.Parse(identifiers) : new JsonObject(),
            };
            return await SendAsync(HttpMethod.Patch, $"entities/{Uri.EscapeDataString(Id(main))}/", body, "to update the organisation's own entity", cancellationToken);
        });
    }

    private async Task<Result<Unit>> SolutionAsync(ScopeWrite write, CancellationToken cancellationToken)
    {
        var lists = Result.Combine([await ListAsync("entities/", cancellationToken), await ListAsync("solutions/", cancellationToken)]);
        return await lists.BindAsync(async all =>
        {
            var existing = ByRefId(all[1]).GetValueOrDefault(write.Change.Key);
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("solutions", existing, write, cancellationToken);
            }

            return await Resolve(Split(Field(declared, "entity")), ByRefId(all[0].Where(e => !IsBuiltin(e))), "third party").BindAsync(async entity =>
            {
                var body = new JsonObject
                {
                    ["name"] = Field(declared, "name"),
                    ["ref_id"] = declared.Key,
                    ["description"] = Described(declared, write.Change),
                    ["provider_entity"] = entity[0]!.DeepClone(),
                    ["criticality"] = WholeNumber(declared, "criticality"),
                };
                Drop(body, write.Change, "criticality");
                return write.Change.Action == ScopeAction.Create
                    ? await SendAsync(HttpMethod.Post, "solutions/", body, $"to create solution {declared.Key}", cancellationToken)
                    : await UpdateAsync("solutions", existing, body, write, cancellationToken);
            });
        });
    }

    private async Task<Result<Unit>> ContractAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var lists = Result.Combine([await ListAsync("entities/", cancellationToken), await ListAsync("solutions/", cancellationToken), await ListAsync("contracts/", cancellationToken)]);
        return await lists.BindAsync(async all =>
        {
            var existing = ByRefId(all[2]).GetValueOrDefault(write.Change.Key);
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("contracts", existing, write, cancellationToken);
            }

            var entity = Resolve(Split(Field(declared, "entity")), ByRefId(all[0].Where(e => !IsBuiltin(e))), "third party").Map(ids => ids[0]!.DeepClone());
            var solutions = Resolve(Split(Field(declared, "solutions")), ByRefId(all[1]), "solution").Map(ids => (JsonNode)ids);
            return await Result.Combine([folders.FolderOf(declared).Map(f => (JsonNode)f), entity, solutions]).BindAsync(async resolved =>
            {
                var body = new JsonObject
                {
                    ["name"] = Field(declared, "name"),
                    ["ref_id"] = declared.Key,
                    ["description"] = Described(declared, write.Change),
                    ["folder"] = resolved[0].DeepClone(),
                    ["provider_entity"] = resolved[1].DeepClone(),
                    ["status"] = Field(declared, "status"),
                    ["start_date"] = Field(declared, "start_date"),
                    ["end_date"] = Field(declared, "end_date"),
                    ["annual_expense"] = Field(declared, "annual_expense") is { } expense ? decimal.Parse(expense, CultureInfo.InvariantCulture) : null,
                    ["currency"] = Field(declared, "currency") ?? "",
                    ["solutions"] = resolved[2].DeepClone(),
                };
                Drop(body, write.Change, "start_date", "end_date", "annual_expense");
                return write.Change.Action == ScopeAction.Create
                    ? await SendAsync(HttpMethod.Post, "contracts/", body, $"to create contract {declared.Key}", cancellationToken)
                    : await UpdateAsync("contracts", existing, body, write, cancellationToken);
            });
        });
    }

    private async Task<Result<Unit>> RepresentativeAsync(ScopeWrite write, CancellationToken cancellationToken)
    {
        var lists = Result.Combine([await ListAsync("entities/", cancellationToken), await ListAsync("representatives/", cancellationToken)]);
        return await lists.BindAsync(async all =>
        {
            var existing = all[1].Where(r => string.Equals(Text(r["email"]), write.Change.Key, StringComparison.OrdinalIgnoreCase)).Select(Id).FirstOrDefault();
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("representatives", existing, write, cancellationToken);
            }

            return await Resolve(Split(Field(declared, "entity")), ByRefId(all[0].Where(e => !IsBuiltin(e))), "third party").BindAsync(async entity =>
            {
                var body = new JsonObject
                {
                    ["email"] = declared.Key,
                    ["entity"] = entity[0]!.DeepClone(),
                    ["first_name"] = Field(declared, "first_name") ?? "",
                    ["last_name"] = Field(declared, "last_name") ?? "",
                    ["role"] = Field(declared, "role") ?? "",
                    ["phone"] = Field(declared, "phone") ?? "",
                    ["description"] = Described(declared, write.Change),
                };
                return write.Change.Action == ScopeAction.Create
                    ? await SendAsync(HttpMethod.Post, "representatives/", body, $"to create representative {declared.Key}", cancellationToken)
                    : await UpdateAsync("representatives", existing, body, write, cancellationToken);
            });
        });
    }

    /// <summary>
    /// A supplier assessment, in its perimeter. When it is created with a questionnaire, CISO Assistant creates the questionnaire's audit with it
    /// (in the supplier's own workspace). The conclusion, expiry date, answers and tier are never sent.
    /// </summary>
    private async Task<Result<Unit>> EntityAssessmentAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var lists = Result.Combine([await ListAsync("entities/", cancellationToken), await ListAsync("solutions/", cancellationToken), await ListAsync("entity-assessments/", cancellationToken)]);
        return await lists.BindAsync(async all =>
        {
            var existing = ByRefId(all[2]).GetValueOrDefault(write.Change.Key);
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("entity-assessments", existing, write, cancellationToken);
            }

            var folder = folders.FolderOf(declared);
            var entity = Resolve(Split(Field(declared, "entity")), ByRefId(all[0].Where(e => !IsBuiltin(e))), "third party").Map(ids => ids[0]!.DeepClone());
            var solutions = Resolve(Split(Field(declared, "solutions")), ByRefId(all[1]), "solution").Map(ids => (JsonNode)ids);
            var perimeter = await folder.BindAsync(f => PerimeterIdAsync(Field(declared, "perimeter"), f, cancellationToken));
            var create = write.Change.Action == ScopeAction.Create;
            var questionnaire = create && declared.Initial.GetValueOrDefault("questionnaire") is { } urn
                ? await FrameworkIdAsync(urn, cancellationToken)
                : Result.Ok("");
            return await Result.Combine([folder.Map(Node), entity, solutions, perimeter.Map(Node), questionnaire.Map(Node)]).BindAsync(async resolved =>
            {
                var body = new JsonObject
                {
                    ["name"] = Field(declared, "name"),
                    ["ref_id"] = declared.Key,
                    ["description"] = Described(declared, write.Change),
                    ["folder"] = resolved[0].DeepClone(),
                    ["entity"] = resolved[1].DeepClone(),
                    ["solutions"] = resolved[2].DeepClone(),
                    ["perimeter"] = resolved[3].DeepClone(),
                    ["due_date"] = Field(declared, "due_date"),
                    ["criticality"] = WholeNumber(declared, "criticality"),
                };
                Drop(body, write.Change, "due_date", "criticality");
                if (create && Text(resolved[4]) is { } framework)
                {
                    body["create_audit"] = true;
                    body["framework"] = framework;
                    body["selected_implementation_groups"] = new JsonArray([.. Split(declared.Initial.GetValueOrDefault("groups")).Select(g => JsonValue.Create(g))]);
                }

                return create
                    ? await SendAsync(HttpMethod.Post, "entity-assessments/", body, $"to create supplier assessment {declared.Key}", cancellationToken)
                    : await UpdateAsync("entity-assessments", existing, body, write, cancellationToken);
            });
        });
    }

    private async Task<Result<List<JsonNode>>> ListAsync(string path, CancellationToken cancellationToken) => await http.ListAllAsync(path, cancellationToken);

    /// <summary>Each object's ID by its <c>ref_id</c>.</summary>
    private static Dictionary<string, string> ByRefId(IEnumerable<JsonNode> objects) =>
        objects.Where(o => Text(o["ref_id"]) is not null).GroupBy(o => Text(o["ref_id"])!).ToDictionary(g => g.Key, g => Id(g.First()), StringComparer.Ordinal);

    /// <summary>The organisation's own entity, which CISO Assistant makes itself and the scope never manages as a third party.</summary>
    private static bool IsBuiltin(JsonNode entity) => entity["builtin"] is JsonValue builtin && builtin.TryGetValue<bool>(out var value) && value;

    private static JsonValue? WholeNumber(ScopeObject declared, string field) =>
        Field(declared, field) is { } text ? JsonValue.Create(int.Parse(text, CultureInfo.InvariantCulture)) : null;
}
