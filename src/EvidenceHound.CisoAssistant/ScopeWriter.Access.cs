using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>
/// Users with their roles, and teams. A user is created without a password (they set their own from CISO Assistant's invitation) and is never
/// deleted, only deactivated; their marker goes at the end of <c>observation</c>, since users have no description. CISO Assistant lets no
/// one create role assignments through the API, so a role on a domain is membership of that domain's built-in group for it (or of a group
/// you made, for a custom role), and a user's <c>user_groups</c> is set to exactly the groups of the roles the scope gives them.
/// </summary>
internal sealed partial class ScopeWriter
{
    private async Task<Result<Unit>> UserAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
        {
            return Result.Fail<Unit>(new Problem("scope.user_not_deleted", $"hound never deletes user {write.Change.Key}; it deactivates them."));
        }

        var listed = await ListAsync(["users/", "user-groups/"], cancellationToken);
        return await listed.BindAsync(async lists =>
        {
            var (users, groups) = (lists[0], lists[1]);
            var existing = users.FirstOrDefault(u => string.Equals(Text(u["email"]), declared.Key, StringComparison.OrdinalIgnoreCase));
            var (observation, _) = ScopeMarker.Read(existing is null ? null : Text(existing["observation"]));
            var groupIds = groups.Select(g => (Role: RoleCodes.GroupRole(g, folders.Names), Id: Id(g))).Where(g => g.Role is not null)
                .GroupBy(g => g.Role!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
            return await Resolve(Split(Field(declared, "roles")), groupIds, "user group for role").BindAsync(async memberships =>
            {
                var body = new JsonObject
                {
                    ["first_name"] = Field(declared, "first_name") ?? "",
                    ["last_name"] = Field(declared, "last_name") ?? "",
                    ["is_active"] = Field(declared, "active") != "false",
                    ["observation"] = ScopeMarker.Mark(observation, write.Change.State ?? ScopeMarker.State(declared.Kind, declared.Fields)),
                    ["user_groups"] = memberships,
                };
                if (write.Change.Action == ScopeAction.Create)
                {
                    body["email"] = declared.Key;
                    return await SendAsync(HttpMethod.Post, "users/", body, $"to create user {declared.Key}", cancellationToken);
                }

                return await UpdateAsync("users", existing is null ? null : Id(existing), body, write, cancellationToken);
            });
        });
    }

    private async Task<Result<Unit>> TeamAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var listed = await ListAsync(["teams/", "users/"], cancellationToken);
        return await listed.BindAsync(async lists =>
        {
            var (teams, users) = (lists[0], lists[1]);
            var existing = teams.FirstOrDefault(t => Text(t["name"]) == write.Change.Key);
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("teams", existing is null ? null : Id(existing), write, cancellationToken);
            }

            var userIds = UserIds(users);
            var leader = Resolve(Split(Field(declared, "leader")), userIds, "user");
            var members = Resolve(Split(Field(declared, "members")), userIds, "user");
            var folder = Folder(folders, declared);
            return await Result.Combine([folder.Map(f => (JsonNode)f), leader.Map(l => (JsonNode)l), members.Map(m => (JsonNode)m)]).BindAsync(async resolved =>
            {
                var body = new JsonObject
                {
                    ["name"] = declared.Key,
                    ["description"] = Described(declared, write.Change),
                    ["folder"] = resolved[0].DeepClone(),
                    ["leader"] = resolved[1].AsArray().FirstOrDefault()?.DeepClone(),
                    ["deputies"] = new JsonArray(),
                    ["members"] = resolved[2].DeepClone(),
                };
                Drop(body, "leader");
                return write.Change.Action == ScopeAction.Create
                    ? await SendAsync(HttpMethod.Post, "teams/", body, $"to create team {declared.Key}", cancellationToken)
                    : await UpdateAsync("teams", existing is null ? null : Id(existing), body, write, cancellationToken);
            });
        });
    }

    /// <summary>Several lists, read before anything is sent; fails with every list that could not be read.</summary>
    private async Task<Result<List<JsonNode>[]>> ListAsync(string[] paths, CancellationToken cancellationToken)
    {
        var lists = new List<Result<List<JsonNode>>>();
        foreach (var path in paths)
        {
            lists.Add(await http.ListAllAsync(path, cancellationToken));
        }

        return Result.Combine(lists).Map(all => all.ToArray());
    }

    /// <summary>A team's folder: its domain's, or the root folder's by name.</summary>
    private static Result<string> Folder(Folders folders, ScopeObject declared) =>
        Field(declared, "domain") is { } domain
            ? folders.Of(domain) is { } id ? Result.Ok(id) : Result.Fail<string>(ApiProblems.NotFound("domain", $"'{domain}'"))
            : Result.Ok(folders.RootId);

    /// <summary>Each user's ID by their email, in lower case as the scope writes it.</summary>
    private static Dictionary<string, string> UserIds(List<JsonNode> users) =>
        users.Where(u => Text(u["email"]) is not null).GroupBy(u => Text(u["email"])!.ToLowerInvariant()).ToDictionary(g => g.Key, g => Id(g.First()), StringComparer.Ordinal);
}
