using System.Text.Json.Nodes;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>
/// Reads what CISO Assistant holds under the scope's root folder, with GET requests only. Lists find the objects in the root folder and the
/// domains directly under it; each object's <c>/object/</c> view gives its fields as IDs and codes, which are turned into the names the scope
/// uses. Fields come out exactly as <see cref="ScopeDesired"/> writes them, so an unchanged object compares equal.
/// </summary>
internal sealed class ScopeStateReader(HttpClient http) : IScopeStateSource
{
    public async Task<Result<IReadOnlyList<ActualObject>>> ReadAsync(ScopeStateQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var reading = new Reading(http, cancellationToken);
        var folders = await reading.ListAsync("folders/");
        if (reading.Problems.Count > 0)
        {
            return Result.Fail<IReadOnlyList<ActualObject>>(reading.Problems);
        }

        if (folders.FirstOrDefault(f => Text(f["name"]) == query.RootFolder) is not { } root)
        {
            return Result.Fail<IReadOnlyList<ActualObject>>(ApiProblems.NotFound("root folder", $"name '{query.RootFolder}'"));
        }

        var rootId = Id(root);
        var domainIds = folders.Where(f => RelationId(f["parent_folder"]) == rootId).Select(Id).ToArray();
        var domainObjects = await reading.ObjectsAsync("folders", domainIds);
        var users = Actors(await reading.ListAsync("users/"), await reading.ListAsync("actors/"));
        var frameworks = (await reading.ListAsync("frameworks/")).ToDictionary(Id, f => Text(f["urn"]) ?? Id(f), StringComparer.Ordinal);
        HashSet<string> inScope = [rootId, .. domainIds];
        var perimeters = await reading.ObjectsInAsync("perimeters", inScope);
        var assets = await reading.ObjectsInAsync("assets", inScope);
        var audits = await reading.ObjectsInAsync("compliance-assessments", inScope);
        var evidences = await reading.ObjectsInAsync("evidences", inScope);
        var controls = await reading.ObjectsInAsync("applied-controls", inScope);
        var rows = await RowsAsync(reading, query, audits.Select(a => Text(a["ref_id"])).OfType<string>().ToHashSet(StringComparer.Ordinal));
        if (reading.Problems.Count > 0)
        {
            return Result.Fail<IReadOnlyList<ActualObject>>(reading.Problems);
        }

        var names = new Lookups(
            domainObjects.ToDictionary(Id, d => Text(d["name"]) ?? Id(d), StringComparer.Ordinal),
            Text(root["name"]) ?? query.RootFolder,
            users,
            assets.ToDictionary(Id, a => Text(a["name"]) ?? Id(a), StringComparer.Ordinal),
            perimeters.ToDictionary(Id, p => Text(p["name"]) ?? Id(p), StringComparer.Ordinal),
            evidences.ToDictionary(Id, e => Text(e["ref_id"]) ?? Id(e), StringComparer.Ordinal));
        return Result.Ok<IReadOnlyList<ActualObject>>(
        [
            .. frameworks.Values.Where(query.FrameworkUrns.Contains).Select(urn => new ActualObject(new ScopeObject(ScopeKind.Framework, urn, Fields()), null)),
            .. domainObjects.Select(d => Marked(ScopeKind.Domain, Text(d["name"]), d)),
            .. perimeters.Select(p => Marked(ScopeKind.Perimeter, ScopeDesired.PerimeterKey(names.Domain(p) ?? names.Root, Text(p["name"]) ?? Id(p)), p,
                ("name", Text(p["name"])), ("ref_id", Text(p["ref_id"])), ("domain", names.Domain(p)))),
            .. assets.Select(a => Marked(ScopeKind.System, Text(a["name"]), a,
                ("ref_id", Text(a["ref_id"])), ("type", AssetType(Text(a["type"]))), ("owner", Joined(a["owner"], names.Users)), ("domain", names.Domain(a)),
                ("parents", Joined(a["parent_assets"], names.Assets)))),
            .. audits.Select(a => Marked(ScopeKind.Audit, Text(a["ref_id"]), a,
                ("name", Text(a["name"])), ("framework", Lookup(a["framework"], frameworks)), ("groups", ScopeDesired.Joined(Strings(a["selected_implementation_groups"]))),
                ("domain", names.Domain(a)), ("perimeter", Lookup(a["perimeter"], names.Perimeters)), ("eta", Text(a["eta"])), ("due_date", Text(a["due_date"])),
                ("authors", Joined(a["authors"], names.Users)), ("reviewers", Joined(a["reviewers"], names.Users)))),
            .. evidences.Select(e => Marked(ScopeKind.Document, Text(e["ref_id"]), e, ("name", Text(e["name"])), ("domain", names.Domain(e)))),
            .. controls.Select(c => Marked(ScopeKind.Control, Text(c["ref_id"]), c,
                ("name", Text(c["name"])), ("domain", names.Domain(c)), ("owner", Joined(c["owner"], names.Users)), ("priority", Text(c["priority"])),
                ("category", Text(c["category"])), ("eta", Text(c["eta"])), ("systems", Joined(c["assets"], names.Assets)), ("documents", Joined(c["evidences"], names.Evidence)))),
            .. rows,
        ]);
    }

    /// <summary>The rows with hand-written results, from each audit CISO Assistant already holds, matched by requirement ref_id or URN.</summary>
    private static async Task<List<ActualObject>> RowsAsync(Reading reading, ScopeStateQuery query, HashSet<string> heldAudits)
    {
        var found = new List<ActualObject>();
        foreach (var (audit, requirements) in query.Rows.Where(r => heldAudits.Contains(r.Key)))
        {
            var rows = await reading.ListAsync($"requirement-assessments/?{Selector.RowsOfAudit(audit).ToQueryString()}");
            foreach (var requirement in requirements)
            {
                if (rows.FirstOrDefault(r => Matches(r["requirement"], requirement)) is { } row)
                {
                    found.Add(new ActualObject(
                        new ScopeObject(ScopeKind.Assessment, ScopeDesired.AssessmentKey(audit, requirement), Fields(
                            ("status", Choice(Text(row["status"]))), ("result", Choice(Text(row["result"]))), ("observation", Text(row["observation"])))),
                        null));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Names for actor IDs, which owners, authors and reviewers hold: a user's actor by the user's email, a team's or entity's by its name.
    /// </summary>
    private static Dictionary<string, string> Actors(List<JsonNode> users, List<JsonNode> actors)
    {
        var emails = users.ToDictionary(Id, u => Text(u["email"]) ?? Id(u), StringComparer.Ordinal);
        return actors.ToDictionary(Id, a => RelationId(a["specific"]) is { } id && emails.TryGetValue(id, out var email) ? email : Text(a["str"]) ?? Id(a), StringComparer.Ordinal);
    }

    private static bool Matches(JsonNode? requirement, RequirementKey key) =>
        Text(requirement?[key.Kind == RequirementKeyKind.Urn ? "urn" : "ref_id"]) == key.Value;

    /// <summary>An object with its description's marker: the text is a field, the marker is the state <c>hound</c> last wrote.</summary>
    private static ActualObject Marked(ScopeKind kind, string? key, JsonNode held, params (string Name, string? Value)[] fields)
    {
        var (description, state) = ScopeMarker.Read(Text(held["description"]));
        return new ActualObject(new ScopeObject(kind, key ?? Id(held), Fields([.. fields, ("description", description)])), state);
    }

    private static Dictionary<string, string> Fields(params (string Name, string? Value)[] fields) =>
        fields.Where(f => !string.IsNullOrEmpty(f.Value)).ToDictionary(f => f.Name, f => f.Value!, StringComparer.Ordinal);

    private static string? AssetType(string? code) => code switch
    {
        "PR" => "primary",
        "SP" => "supporting",
        _ => Choice(code),
    };

    /// <summary>A choice's value from its value or its display name: "To do" is "to_do".</summary>
    private static string? Choice(string? value) => value?.Trim().ToLowerInvariant().Replace(' ', '_');

    private static string? Lookup(JsonNode? relation, Dictionary<string, string> names) =>
        RelationId(relation) is { } id ? names.GetValueOrDefault(id, id) : null;

    private static string? Joined(JsonNode? relations, Dictionary<string, string> names) =>
        ScopeDesired.Joined((relations as JsonArray ?? []).Select(RelationId).OfType<string>().Select(id => names.GetValueOrDefault(id, id)));

    /// <summary>A related object's ID: a plain ID, or the <c>{"id", "str"}</c> object the read serializers give.</summary>
    private static string? RelationId(JsonNode? relation) => relation is JsonObject o ? Text(o["id"]) : Text(relation);

    private static string? Text(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => string.IsNullOrEmpty(text) ? null : text,
        JsonValue value => value.ToJsonString(),
        _ => null,
    };

    private static List<string> Strings(JsonNode? node) => [.. (node as JsonArray ?? []).Select(Text).OfType<string>()];

    private static string Id(JsonNode node) => Text(node["id"]) ?? throw new InvalidOperationException("CISO Assistant returned an object without an id.");

    /// <summary>Names for the IDs the objects refer to. Objects in the root folder have no domain.</summary>
    private sealed record Lookups(
        Dictionary<string, string> Domains,
        string Root,
        Dictionary<string, string> Users,
        Dictionary<string, string> Assets,
        Dictionary<string, string> Perimeters,
        Dictionary<string, string> Evidence)
    {
        public string? Domain(JsonNode held) => RelationId(held["folder"]) is { } id ? Domains.GetValueOrDefault(id) : null;
    }

    /// <summary>One read of CISO Assistant: every request goes on, and the first failures are kept to report at the end.</summary>
    private sealed class Reading(HttpClient http, CancellationToken cancellationToken)
    {
        public List<Problem> Problems { get; } = [];

        /// <summary>Every object of a list. <paramref name="path"/> may carry a query of its own.</summary>
        public async Task<List<JsonNode>> ListAsync(string path) =>
            Problems.Count > 0 ? [] : Kept(await http.ListAllAsync(path, cancellationToken)) ?? [];

        /// <summary>The <c>/object/</c> view of each listed object whose folder is one of <paramref name="folders"/>.</summary>
        public async Task<List<JsonNode>> ObjectsInAsync(string resource, HashSet<string> folders) =>
            await ObjectsAsync(resource, [.. (await ListAsync($"{resource}/")).Where(o => RelationId(o["folder"]) is { } f && folders.Contains(f)).Select(Id)]);

        public async Task<List<JsonNode>> ObjectsAsync(string resource, string[] ids)
        {
            var objects = new List<JsonNode>(ids.Length);
            foreach (var id in ids)
            {
                if (await GetAsync($"{resource}/{Uri.EscapeDataString(id)}/object/", $"to read {resource} {id}") is { } found)
                {
                    objects.Add(found);
                }
            }

            return objects;
        }

        private async Task<JsonNode?> GetAsync(string path, string action) =>
            Kept(await http.SendForJsonAsync(new HttpRequestMessage(HttpMethod.Get, path), action, cancellationToken));

        /// <summary>The value, or null with its problems kept for the end of the read.</summary>
        private T? Kept<T>(Result<T> result)
            where T : class =>
            result.Match<T?>(
                found => found,
                problems =>
                {
                    Problems.AddRange(problems);
                    return null;
                });
    }
}
