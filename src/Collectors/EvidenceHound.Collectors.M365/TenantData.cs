using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.M365;

/// <summary>The parts of a tenant the checks read, parsed once from the Graph collections. Missing collections are empty.</summary>
internal sealed record TenantData(
    IReadOnlyList<DirectoryUser> Users,
    IReadOnlyList<MfaRegistration> Registrations,
    IReadOnlyList<CaPolicy> Policies,
    IReadOnlyList<RoleGrant> Assignments,
    IReadOnlyList<RoleGrant>? Eligibilities,
    IReadOnlyList<RoleGrant>? Instances)
{
    /// <summary>Whether the tenant has Privileged Identity Management, that is whether its eligibility and schedule collections could be read.</summary>
    public bool HasPim => Eligibilities is not null && Instances is not null;

    public static TenantData Parse(IReadOnlyDictionary<string, JsonArray?> collections)
    {
        ArgumentNullException.ThrowIfNull(collections);
        return new(
            Items(collections, GraphSources.Users).Select(DirectoryUser.From).ToArray(),
            Items(collections, GraphSources.MfaRegistration).Select(MfaRegistration.From).ToArray(),
            Items(collections, GraphSources.ConditionalAccess).Select(CaPolicy.From).ToArray(),
            Items(collections, GraphSources.RoleAssignments).Select(RoleGrant.From).ToArray(),
            Optional(collections, GraphSources.RoleEligibility)?.Select(RoleGrant.From).ToArray(),
            Optional(collections, GraphSources.RoleInstances)?.Select(RoleGrant.From).ToArray());
    }

    internal static IEnumerable<JsonObject> Items(IReadOnlyDictionary<string, JsonArray?> collections, string name) =>
        collections.GetValueOrDefault(name)?.OfType<JsonObject>() ?? [];

    private static IEnumerable<JsonObject>? Optional(IReadOnlyDictionary<string, JsonArray?> collections, string name) =>
        collections.GetValueOrDefault(name)?.OfType<JsonObject>();
}

internal sealed record DirectoryUser(string Id, bool Enabled, bool IsMember, bool Synced)
{
    public static DirectoryUser From(JsonObject item) => new(
        Json.Text(item, "id"),
        Json.Flag(item, "accountEnabled"),
        string.Equals(Json.Text(item, "userType"), "Member", StringComparison.OrdinalIgnoreCase),
        Json.Flag(item, "onPremisesSyncEnabled"));
}

internal sealed record MfaRegistration(string Id, bool IsMfaCapable)
{
    public static MfaRegistration From(JsonObject item) => new(Json.Text(item, "id"), Json.Flag(item, "isMfaCapable"));
}

/// <summary>
/// A directory role held by a principal: an active assignment, an eligibility, or an assignment schedule instance. Only instances
/// have an assignment type (<c>Assigned</c> or <c>Activated</c>); an empty <paramref name="End"/> means it never expires.
/// </summary>
internal sealed record RoleGrant(string PrincipalId, string RoleId, string AssignmentType, string End)
{
    public bool IsPermanentAssignment => AssignmentType == "Assigned" && End.Length == 0;

    public static RoleGrant From(JsonObject item) =>
        new(Json.Text(item, "principalId"), Json.Text(item, "roleDefinitionId"), Json.Text(item, "assignmentType"), Json.Text(item, "endDateTime"));
}

/// <summary>A Conditional Access policy: who and what it applies to, and what it grants.</summary>
internal sealed record CaPolicy(
    string Id,
    string Name,
    string State,
    IReadOnlyList<string> IncludeUsers,
    IReadOnlyList<string> ExcludeUsers,
    IReadOnlyList<string> ExcludeGroups,
    IReadOnlyList<string> IncludeRoles,
    IReadOnlyList<string> ExcludeRoles,
    IReadOnlyList<string> IncludeApplications,
    IReadOnlyList<string> ClientAppTypes,
    string Operator,
    IReadOnlyList<string> BuiltInControls,
    string? AuthenticationStrengthId)
{
    public bool IsEnabled => State == "enabled";

    public bool ForAllUsers => IncludeUsers.Contains("All", StringComparer.OrdinalIgnoreCase);

    public bool ForAllApps => IncludeApplications.Contains("All", StringComparer.OrdinalIgnoreCase);

    public static CaPolicy From(JsonObject item)
    {
        var users = item["conditions"]?["users"];
        var grant = item["grantControls"];
        return new(
            Json.Text(item, "id"),
            Json.Text(item, "displayName"),
            Json.Text(item, "state"),
            Json.Texts(users, "includeUsers"),
            Json.Texts(users, "excludeUsers"),
            Json.Texts(users, "excludeGroups"),
            Json.Texts(users, "includeRoles"),
            Json.Texts(users, "excludeRoles"),
            Json.Texts(item["conditions"]?["applications"], "includeApplications"),
            Json.Texts(item["conditions"], "clientAppTypes"),
            Json.Text(grant, "operator"),
            Json.Texts(grant, "builtInControls"),
            grant?["authenticationStrength"]?["id"]?.GetValue<string>());
    }
}

/// <summary>Tolerant reads from Graph JSON: a missing or null property is an empty value, not an error.</summary>
internal static class Json
{
    public static string Text(JsonNode? node, string name) =>
        node?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

    public static bool Flag(JsonNode? node, string name) =>
        node?[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    public static IReadOnlyList<string> Texts(JsonNode? node, string name) =>
        node?[name] is JsonArray array ? [.. array.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var t) ? t : null).OfType<string>()] : [];
}
