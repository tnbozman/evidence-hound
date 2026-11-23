using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.M365;

/// <summary>The parts of a tenant the checks read, parsed once from the Graph collections. Missing collections are empty.</summary>
internal sealed record TenantData(
    IReadOnlyList<DirectoryUser> Users,
    IReadOnlyList<MfaRegistration> Registrations,
    IReadOnlyList<CaPolicy> Policies,
    IReadOnlyList<RoleGrant> Assignments,
    IReadOnlyList<RoleGrant>? Eligibilities,
    IReadOnlyList<RoleGrant>? Instances,
    IReadOnlyList<ManagedDevice> Devices,
    IReadOnlyList<IntunePolicy> ConfigurationPolicies,
    IReadOnlyList<IntunePolicy> DeviceConfigurations,
    IReadOnlyList<AppControlPolicy> AppControl)
{
    /// <summary>Whether the tenant has Privileged Identity Management, that is whether its eligibility and schedule collections could be read.</summary>
    public bool HasPim => Eligibilities is not null && Instances is not null;

    /// <summary>Application control events in Defender for Endpoint, counted per device and action type.</summary>
    public IReadOnlyList<ControlEvents> AppControlEvents { get; init; } = [];

    /// <summary>The files application control blocked most often.</summary>
    public IReadOnlyList<BlockedFile> BlockedFiles { get; init; } = [];

    /// <summary>Windows devices in Defender for Endpoint, counted by onboarding status.</summary>
    public IReadOnlyDictionary<string, int> DefenderOnboarding { get; init; } = new Dictionary<string, int>();

    /// <summary>Sources the tenant refused (no licence, no permission), with the reason; their checks say why.</summary>
    public IReadOnlyDictionary<string, Problem> Unavailable { get; init; } = new Dictionary<string, Problem>();

    public static TenantData Parse(IReadOnlyDictionary<string, JsonArray?> collections)
    {
        ArgumentNullException.ThrowIfNull(collections);
        return new(
            Items(collections, GraphSources.Users).Select(DirectoryUser.From).ToArray(),
            Items(collections, GraphSources.MfaRegistration).Select(MfaRegistration.From).ToArray(),
            Items(collections, GraphSources.ConditionalAccess).Select(CaPolicy.From).ToArray(),
            Items(collections, GraphSources.RoleAssignments).Select(RoleGrant.From).ToArray(),
            Optional(collections, GraphSources.RoleEligibility)?.Select(RoleGrant.From).ToArray(),
            Optional(collections, GraphSources.RoleInstances)?.Select(RoleGrant.From).ToArray(),
            Items(collections, GraphSources.ManagedDevices).Select(ManagedDevice.From).ToArray(),
            Items(collections, GraphSources.ConfigurationPolicies).Select(IntunePolicy.FromSettingsCatalog).ToArray(),
            Items(collections, GraphSources.DeviceConfigurations).Select(IntunePolicy.FromDeviceConfiguration).ToArray(),
            [
                .. Items(collections, GraphSources.ConfigurationPolicies).SelectMany(AppControlPolicy.FromSettingsCatalog),
                .. Items(collections, GraphSources.DeviceConfigurations).SelectMany(AppControlPolicy.FromDeviceConfiguration),
            ])
        {
            AppControlEvents = [.. Items(collections, GraphSources.AppControlEvents).Select(ControlEvents.From)],
            BlockedFiles = [.. Items(collections, GraphSources.BlockedFiles).Select(BlockedFile.From)],
            DefenderOnboarding = Items(collections, GraphSources.DefenderOnboarding)
                .GroupBy(r => Json.Text(r, "OnboardingStatus"), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(r => Json.Number(r, "Devices")), StringComparer.OrdinalIgnoreCase),
        };
    }

    internal static IEnumerable<JsonObject> Items(IReadOnlyDictionary<string, JsonArray?> collections, string name) =>
        collections.GetValueOrDefault(name)?.OfType<JsonObject>() ?? [];

    private static IEnumerable<JsonObject>? Optional(IReadOnlyDictionary<string, JsonArray?> collections, string name) =>
        collections.GetValueOrDefault(name)?.OfType<JsonObject>();
}

/// <summary>One row of the application control events query: a device, an action type such as <c>AppControlCodeIntegrityPolicyBlocked</c>, and how many.</summary>
internal sealed record ControlEvents(string DeviceId, string ActionType, int Events)
{
    public bool Blocked => ActionType.Contains("Block", StringComparison.OrdinalIgnoreCase);

    public bool Audited => ActionType.Contains("Audit", StringComparison.OrdinalIgnoreCase);

    public static ControlEvents From(JsonObject row) => new(Json.Text(row, "DeviceId"), Json.Text(row, "ActionType"), Json.Number(row, "Events"));
}

/// <summary>A file application control blocked: its name, hash, publisher (empty when unsigned or not reported), and how often.</summary>
internal sealed record BlockedFile(string FileName, string Sha256, string Publisher, int Blocks)
{
    public static BlockedFile From(JsonObject row) =>
        new(Json.Text(row, "FileName"), Json.Text(row, "SHA256"), Json.Text(row, "Publisher"), Json.Number(row, "Blocks"));
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

/// <summary>An Intune-managed device: its operating system, version and compliance state.</summary>
internal sealed record ManagedDevice(string Id, string OperatingSystem, string OsVersion, string ComplianceState)
{
    public bool IsWindows => string.Equals(OperatingSystem, "Windows", StringComparison.OrdinalIgnoreCase);

    public bool IsCompliant => string.Equals(ComplianceState, "compliant", StringComparison.OrdinalIgnoreCase);

    /// <summary>The first three parts of the version, for example <c>10.0.26100</c> for <c>10.0.26100.2314</c>.</summary>
    public string Build => string.Join('.', OsVersion.Split('.').Take(3));

    public static ManagedDevice From(JsonObject item) =>
        new(Json.Text(item, "id"), Json.Text(item, "operatingSystem"), Json.Text(item, "osVersion"), Json.Text(item, "complianceState"));
}

/// <summary>
/// An Intune policy reduced to its name, who it is assigned to, and its settings as key and value pairs. For a settings catalog
/// policy the pairs are each setting definition ID and its chosen value, at any depth; for a device configuration profile they are
/// its text properties.
/// </summary>
internal sealed record IntunePolicy(string Name, bool AllUsers, bool AllDevices, bool Assigned, IReadOnlyList<KeyValuePair<string, string>> Settings)
{
    public static IntunePolicy FromSettingsCatalog(JsonObject item) =>
        Targeted(Json.Text(item, "name"), item, [.. Choices(item["settings"])]);

    public static IntunePolicy FromDeviceConfiguration(JsonObject item) =>
        Targeted(Json.Text(item, "displayName"), item, [.. item.Where(p => p.Value is JsonValue v && v.TryGetValue<string>(out _)).Select(p => KeyValuePair.Create(p.Key, p.Value!.GetValue<string>()))]);

    public string? Find(string key) => Settings.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    private static IntunePolicy Targeted(string name, JsonObject item, IReadOnlyList<KeyValuePair<string, string>> settings)
    {
        var targets = (item["assignments"] as JsonArray ?? []).Select(a => Json.Text(a?["target"], "@odata.type")).ToArray();
        return new(
            name,
            targets.Contains("#microsoft.graph.allLicensedUsersAssignmentTarget"),
            targets.Contains("#microsoft.graph.allDevicesAssignmentTarget"),
            targets.Any(t => t.Length > 0 && t != "#microsoft.graph.exclusionGroupAssignmentTarget"),
            settings);
    }

    /// <summary>Every choice setting below <paramref name="node"/>, including those nested in groups and children.</summary>
    private static IEnumerable<KeyValuePair<string, string>> Choices(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (Json.Text(obj, "settingDefinitionId") is { Length: > 0 } key && Json.Text(obj["choiceSettingValue"], "value") is { Length: > 0 } value)
                {
                    yield return KeyValuePair.Create(key, value);
                }

                foreach (var child in obj.Select(p => p.Value).SelectMany(Choices))
                {
                    yield return child;
                }

                break;
            case JsonArray array:
                foreach (var child in array.SelectMany(Choices))
                {
                    yield return child;
                }

                break;
        }
    }
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

    /// <summary>A whole number, as advanced hunting returns counts: a JSON number, or a number in text. Anything else is 0.</summary>
    public static int Number(JsonNode? node, string name) => node?[name] switch
    {
        JsonValue value when value.TryGetValue<int>(out var small) => small,
        JsonValue value when value.TryGetValue<long>(out var number) => (int)Math.Min(number, int.MaxValue),
        JsonValue value when value.TryGetValue<string>(out var text) && int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => 0,
    };

    public static IReadOnlyList<string> Texts(JsonNode? node, string name) =>
        node?[name] is JsonArray array ? [.. array.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var t) ? t : null).OfType<string>()] : [];
}
