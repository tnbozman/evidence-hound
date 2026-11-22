using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.CisoAssistant;

/// <summary>
/// Users, teams and role assignments. A user is created without a password (they set their own from CISO Assistant's invitation) and is never
/// deleted, only deactivated; their marker goes at the end of <c>observation</c>, since users have no description.
/// </summary>
internal sealed partial class ScopeWriter
{
    private async Task<Result<Unit>> UserAsync(ScopeWrite write, CancellationToken cancellationToken)
    {
        if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
        {
            return Result.Fail<Unit>(new Problem("scope.user_not_deleted", $"hound never deletes user {write.Change.Key}; it deactivates them."));
        }

        var listed = await http.ListAllAsync("users/", cancellationToken);
        return await listed.BindAsync(async users =>
        {
            var existing = users.FirstOrDefault(u => string.Equals(Text(u["email"]), declared.Key, StringComparison.OrdinalIgnoreCase));
            var (observation, _) = ScopeMarker.Read(existing is null ? null : Text(existing["observation"]));
            var body = new JsonObject
            {
                ["first_name"] = Field(declared, "first_name") ?? "",
                ["last_name"] = Field(declared, "last_name") ?? "",
                ["is_active"] = Field(declared, "active") != "false",
                ["observation"] = ScopeMarker.Mark(observation, write.Change.State ?? ScopeMarker.State(declared.Kind, declared.Fields)),
            };
            if (write.Change.Action == ScopeAction.Create)
            {
                body["email"] = declared.Key;
                return await SendAsync(HttpMethod.Post, "users/", body, $"to create user {declared.Key}", cancellationToken);
            }

            return await UpdateAsync("users", existing is null ? null : Id(existing), body, write, cancellationToken);
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

    private async Task<Result<Unit>> RoleAssignmentAsync(ScopeWrite write, Folders folders, CancellationToken cancellationToken)
    {
        var listed = await ListAsync(["role-assignments/", "roles/", "users/"], cancellationToken);
        return await listed.BindAsync(async lists =>
        {
            var (assignments, roles, users) = (lists[0], lists[1], lists[2]);
            var parts = write.Change.Key.Split('|', 3);
            var (roleCode, domain, email) = parts.Length == 3 ? (parts[0], parts[1], parts[2]) : (write.Change.Key, "", "");
            var roleIds = roles.Where(r => Text(r["name"]) is not null).GroupBy(r => Text(r["name"])!).ToDictionary(g => g.Key, g => Id(g.First()), StringComparer.Ordinal);
            var userIds = UserIds(users);
            var folderId = folders.Of(domain);
            var existing = assignments.FirstOrDefault(a =>
                RelationId(a["role"]) == roleIds.GetValueOrDefault(roleCode)
                && RelationId(a["user"]) == userIds.GetValueOrDefault(email)
                && (a["perimeter_folders"] as JsonArray ?? []).Select(RelationId).Contains(folderId));
            if (write.Change.Action == ScopeAction.Delete || write.Declared is not { } declared)
            {
                return await DeleteAsync("role-assignments", existing is null ? null : Id(existing), write, cancellationToken);
            }

            var folder = folderId is null ? Result.Fail<string>(ApiProblems.NotFound("domain", $"'{domain}'")) : Result.Ok(folderId);
            var role = Resolve([roleCode], roleIds, "role");
            var user = Resolve([email], userIds, "user");
            return await Result.Combine([folder.Map(f => (JsonNode)f), role.Map(r => (JsonNode)r), user.Map(u => (JsonNode)u)]).BindAsync(async resolved =>
            {
                var body = new JsonObject
                {
                    ["name"] = $"{roleCode} on {domain} for {email}",
                    ["description"] = Described(declared, write.Change),
                    ["role"] = resolved[1].AsArray()[0]!.DeepClone(),
                    ["user"] = resolved[2].AsArray()[0]!.DeepClone(),
                    ["perimeter_folders"] = new JsonArray(resolved[0].DeepClone()),
                    ["folder"] = resolved[0].DeepClone(),
                    ["is_recursive"] = true,
                };
                return write.Change.Action == ScopeAction.Create
                    ? await SendAsync(HttpMethod.Post, "role-assignments/", body, $"to assign {roleCode} on {domain} to {email}", cancellationToken)
                    : await UpdateAsync("role-assignments", existing is null ? null : Id(existing), body, write, cancellationToken);
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
