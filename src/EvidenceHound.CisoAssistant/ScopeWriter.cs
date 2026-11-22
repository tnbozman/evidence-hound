using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>
/// Writes scope changes to CISO Assistant: frameworks as loaded libraries, domains as folders under the root folder, perimeters, systems as
/// assets, audits as compliance assessments, documents as evidence with their files, applied controls, and rows. Each write finds the IDs it
/// needs by name first (folders, assets, users' actors, frameworks, evidence, controls), so nothing is sent when a name cannot be resolved.
/// Creates and updates send every field the scope manages, with the <c>[hound scope &lt;state&gt;]</c> marker at the end of the description.
/// </summary>
internal sealed partial class ScopeWriter(HttpClient http, ICisoAssistantClient client) : IScopeWriter
{
    public async Task<Result<Unit>> WriteAsync(ScopeWrite write, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        var folders = await http.ListAllAsync("folders/", cancellationToken);
        return await folders.BindAsync(async all =>
            all.FirstOrDefault(f => Text(f["name"]) == write.RootFolder) is { } root
                ? await WriteAsync(write, new Folders(Id(root), all), cancellationToken)
                : Result.Fail<Unit>(ApiProblems.NotFound("root folder", $"name '{write.RootFolder}'")));
    }

    private Task<Result<Unit>> WriteAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken) => write.Change.Kind switch
    {
        ScopeKind.Domain => DomainAsync(write, folders, cancellationToken),
        ScopeKind.Perimeter => PerimeterAsync(write, folders, cancellationToken),
        ScopeKind.System => SystemAsync(write, folders, cancellationToken),
        ScopeKind.Framework => FrameworkAsync(write, cancellationToken),
        ScopeKind.Audit => AuditAsync(write, folders, cancellationToken),
        ScopeKind.Document => DocumentAsync(write, folders, cancellationToken),
        ScopeKind.Control => ControlAsync(write, folders, cancellationToken),
        ScopeKind.Assessment => RowAsync(write, cancellationToken),
        ScopeKind.User => UserAsync(write, folders, cancellationToken),
        ScopeKind.Team => TeamAsync(write, folders, cancellationToken),
        ScopeKind.FeatureFlag => FeatureFlagAsync(write, cancellationToken),
        ScopeKind.Setting => GeneralSettingAsync(write, cancellationToken),
        var kind => Task.FromResult(Result.Fail<Unit>(new Problem("scope.kind_not_applied", $"This version of hound does not write {kind.ToString().ToLowerInvariant()}s yet."))),
    };

    private async Task<Result<Unit>> DomainAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var existing = folders.Domain(write.Change.Key);
        if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
        {
            return await DeleteAsync("folders", existing, write, cancellationToken);
        }

        var body = new JsonObject { ["name"] = declared.Key, ["description"] = Described(declared, write.Change) };
        if (write.Change.Action == ScopeAction.Create)
        {
            body["parent_folder"] = folders.RootId;
            body["content_type"] = "DO";
            return await SendAsync(HttpMethod.Post, "folders/", body, $"to create domain {declared.Key}", cancellationToken);
        }

        return await UpdateAsync("folders", existing, body, write, cancellationToken);
    }

    private async Task<Result<Unit>> PerimeterAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var listed = await http.ListAllAsync("perimeters/", cancellationToken);
        return await listed.BindAsync(async perimeters =>
        {
            var name = write.Declared?.Fields.GetValueOrDefault("name") ?? write.Change.Key.Split('/', 2)[^1];
            var existing = perimeters.FirstOrDefault(p => Text(p["name"]) == name && RelationId(p["folder"]) == folders.Of(write.Change.Key.Split('/', 2)[0]));
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("perimeters", existing is null ? null : Id(existing), write, cancellationToken);
            }

            return await folders.FolderOf(declared).BindAsync(async folder =>
            {
                var body = new JsonObject { ["name"] = name, ["ref_id"] = Field(declared, "ref_id"), ["description"] = Described(declared, write.Change), ["folder"] = folder };
                Drop(body, "ref_id");
                return write.Change.Action == ScopeAction.Create
                    ? await SendAsync(HttpMethod.Post, "perimeters/", body, $"to create perimeter {declared.Key}", cancellationToken)
                    : await UpdateAsync("perimeters", existing is null ? null : Id(existing), body, write, cancellationToken);
            });
        });
    }

    private async Task<Result<Unit>> SystemAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var listed = await http.ListAllAsync("assets/", cancellationToken);
        return await listed.BindAsync(async assets =>
        {
            var byName = assets.GroupBy(a => Text(a["name"]) ?? "").ToDictionary(g => g.Key, g => Id(g.First()), StringComparer.Ordinal);
            var existing = byName.GetValueOrDefault(write.Change.Key);
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("assets", existing, write, cancellationToken);
            }

            var parents = Resolve(Split(Field(declared, "parents")), byName, "parent system");
            var owners = await ActorsAsync(Split(Field(declared, "owner")), cancellationToken);
            var folder = folders.FolderOf(declared);
            return await Result.Combine([folder.Map(f => (JsonNode)f), parents.Map(p => (JsonNode)p), owners.Map(o => (JsonNode)o)]).BindAsync(async resolved =>
            {
                var body = new JsonObject
                {
                    ["name"] = declared.Key,
                    ["ref_id"] = Field(declared, "ref_id"),
                    ["type"] = Field(declared, "type") == "primary" ? "PR" : "SP",
                    ["description"] = Described(declared, write.Change),
                    ["folder"] = resolved[0].DeepClone(),
                    ["parent_assets"] = resolved[1].DeepClone(),
                    ["owner"] = resolved[2].DeepClone(),
                };
                Drop(body, "ref_id");
                return write.Change.Action == ScopeAction.Create
                    ? await SendAsync(HttpMethod.Post, "assets/", body, $"to create system {declared.Key}", cancellationToken)
                    : await UpdateAsync("assets", existing, body, write, cancellationToken);
            });
        });
    }

    /// <summary>The users named by email, as the IDs of their actors: owners, authors and reviewers are actors in CISO Assistant.</summary>
    private async Task<Result<JsonArray>> ActorsAsync(string[] emails, CancellationToken cancellationToken)
    {
        if (emails.Length == 0)
        {
            return Result.Ok(new JsonArray());
        }

        var users = await http.ListAllAsync("users/", cancellationToken);
        return await users.BindAsync(async all =>
            (await http.ListAllAsync("actors/", cancellationToken)).Bind(actors => Resolve(emails, ActorIds(all, actors), "user")));
    }

    /// <summary>Each user's actor ID by the user's email.</summary>
    private static Dictionary<string, string> ActorIds(List<JsonNode> users, List<JsonNode> actors)
    {
        var actorOf = actors.Where(a => Text(a["type"]) == "user" && RelationId(a["specific"]) is not null)
            .GroupBy(a => RelationId(a["specific"])!).ToDictionary(g => g.Key, g => Id(g.First()), StringComparer.Ordinal);
        return users.Where(u => Text(u["email"]) is not null && actorOf.ContainsKey(Id(u)))
            .GroupBy(u => Text(u["email"])!).ToDictionary(g => g.Key, g => actorOf[Id(g.First())], StringComparer.Ordinal);
    }

    private static Result<JsonArray> Resolve(string[] names, Dictionary<string, string> ids, string what) =>
        names.Where(n => !ids.ContainsKey(n)).ToArray() is { Length: > 0 } missing
            ? Result.Fail<JsonArray>(missing.Select(n => ApiProblems.NotFound(what, $"'{n}'")))
            : Result.Ok(new JsonArray([.. names.Select(n => JsonValue.Create(ids[n]))]));

    private Task<Result<Unit>> UpdateAsync(string resource, string? id, JsonObject body, ScopeWrite write, CancellationToken cancellationToken) =>
        id is null
            ? Task.FromResult(Result.Fail<Unit>(ApiProblems.NotFound(Name(write.Change.Kind), $"'{write.Change.Key}'")))
            : SendAsync(HttpMethod.Patch, $"{resource}/{Uri.EscapeDataString(id)}/", body, $"to update {Name(write.Change.Kind)} {write.Change.Key}", cancellationToken);

    private Task<Result<Unit>> DeleteAsync(string resource, string? id, ScopeWrite write, CancellationToken cancellationToken) =>
        id is null
            ? Task.FromResult(Result.Fail<Unit>(ApiProblems.NotFound(Name(write.Change.Kind), $"'{write.Change.Key}'")))
            : SendAsync(HttpMethod.Delete, $"{resource}/{Uri.EscapeDataString(id)}/", null, $"to delete {Name(write.Change.Kind)} {write.Change.Key}", cancellationToken);

    private async Task<Result<Unit>> SendAsync(HttpMethod method, string path, JsonObject? body, string action, CancellationToken cancellationToken)
    {
        return (await SendForJsonAsync(method, path, body, action, cancellationToken)).Map(_ => Unit.Value);
    }

    private Task<Result<JsonNode>> SendForJsonAsync(HttpMethod method, string path, JsonObject? body, string action, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            // A sized body: CISO Assistant's server reads no chunked request body.
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        return http.SendForJsonAsync(request, action, cancellationToken);
    }

    private static string Described(ScopeObject declared, ScopeChange change) =>
        ScopeMarker.Mark(Field(declared, "description"), change.State ?? ScopeMarker.State(declared.Kind, declared.Fields));

    /// <summary>Leaves fields out rather than send empty ones, so CISO Assistant fills in what it fills in itself.</summary>
    private static void Drop(JsonObject body, params string[] fields)
    {
        foreach (var field in fields.Where(f => body[f] is null))
        {
            body.Remove(field);
        }
    }

    private static string? Field(ScopeObject declared, string name) => declared.Fields.GetValueOrDefault(name);

    private static string[] Split(string? joined) => joined is null ? [] : joined.Split(", ", StringSplitOptions.RemoveEmptyEntries);

    private static string Name(ScopeKind kind) => kind.ToString().ToLowerInvariant();

    private static string? RelationId(JsonNode? relation) => relation is JsonObject o ? Text(o["id"]) : Text(relation);

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;

    private static string Id(JsonNode node) => Text(node["id"]) ?? throw new InvalidOperationException("CISO Assistant returned an object without an id.");

    /// <summary>The root folder and the domains directly under it, by name.</summary>
    private sealed class Folders(string rootId, List<JsonNode> all)
    {
        public string RootId { get; } = rootId;

        /// <summary>Every folder's name by its ID.</summary>
        public IReadOnlyDictionary<string, string> Names { get; } = all.ToDictionary(Id, f => Text(f["name"]) ?? Id(f), StringComparer.Ordinal);

        public string? Domain(string name) =>
            all.Where(f => Text(f["name"]) == name && RelationId(f["parent_folder"]) == RootId).Select(Id).FirstOrDefault();

        /// <summary>The folder of a domain by name; the root folder's name stands for itself.</summary>
        public string? Of(string domain) => Domain(domain) ?? (all.Any(f => Id(f) == RootId && Text(f["name"]) == domain) ? RootId : null);

        /// <summary>The folder an object sits in: its domain's, or the root folder when it names none.</summary>
        public Result<string> FolderOf(ScopeObject declared) =>
            declared.Fields.GetValueOrDefault("domain") is not { } domain
                ? Result.Ok(RootId)
                : Domain(domain) is { } id ? Result.Ok(id) : Result.Fail<string>(ApiProblems.NotFound("domain", $"'{domain}'"));
    }
}
