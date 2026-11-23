using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EvidenceHound.Collectors.M365;

/// <summary>The file types Essential Eight application control must cover.</summary>
internal enum FileType
{
    Executables,
    Libraries,
    Scripts,
    Installers,
}

/// <summary>How a policy applies application control to one file type.</summary>
internal enum ControlMode
{
    Enforced,
    Audit,

    /// <summary>The policy sets the file type, but Graph returned its value encrypted, so its mode cannot be read.</summary>
    Unknown,
}

/// <summary>
/// An Intune application control policy: an App Control for Business policy (endpoint security, settings catalog) or a custom
/// profile that configures the AppLocker CSP. <paramref name="Groups"/> are the group IDs it is assigned to.
/// </summary>
internal sealed partial record AppControlPolicy(
    string Kind,
    string Name,
    bool AllDevices,
    bool Assigned,
    IReadOnlyList<string> Groups,
    IReadOnlyDictionary<FileType, ControlMode> Modes)
{
    public const string AppControlForBusiness = "App Control for Business";
    public const string AppLocker = "AppLocker";

    private static readonly string TemplateFamily = "endpointSecurityApplicationControl";

    /// <summary>The built-in controls choice: <c>_0</c> enforces, <c>_1</c> audits. Confirm against a recorded export (s09-01 notes).</summary>
    private static readonly string BuiltInControls = "device_vendor_msft_policy_config_applicationcontrol_built_in_controls_enable_app_control";

    private static readonly Dictionary<string, FileType> AppLockerCollections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EXE"] = FileType.Executables,
        ["DLL"] = FileType.Libraries,
        ["Script"] = FileType.Scripts,
        ["MSI"] = FileType.Installers,
    };

    public string Label => $"{Kind} '{Name}'";

    public bool Enforces => Modes.Values.Contains(ControlMode.Enforced);

    /// <summary>
    /// The App Control for Business policies among settings catalog policies, identified by their template family. A policy is in
    /// audit mode when its built-in controls choice audits or its uploaded XML carries <c>Enabled:Audit Mode</c>; otherwise enforced.
    /// It covers every file type by design.
    /// </summary>
    public static IEnumerable<AppControlPolicy> FromSettingsCatalog(JsonObject item)
    {
        if (!string.Equals(Json.Text(item["templateReference"], "templateFamily"), TemplateFamily, StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        var values = Values(item["settings"]).ToArray();
        var audit = values.Any(v => v.Key.Equals(BuiltInControls, StringComparison.OrdinalIgnoreCase) && v.Value.EndsWith("_1", StringComparison.Ordinal))
            || values.Any(v => v.Value.Contains("Enabled:Audit Mode", StringComparison.OrdinalIgnoreCase));
        var mode = audit ? ControlMode.Audit : ControlMode.Enforced;
        yield return Targeted(AppControlForBusiness, Json.Text(item, "name"), item, Enum.GetValues<FileType>().ToDictionary(t => t, _ => mode));
    }

    /// <summary>
    /// The AppLocker policy in a custom profile: each <c>./Vendor/MSFT/AppLocker/ApplicationLaunchRestrictions/{Grouping}/{Collection}/Policy</c>
    /// setting, read from its rule collection's <c>EnforcementMode</c> (<c>Enabled</c> or <c>AuditOnly</c>; <c>NotConfigured</c> sets nothing).
    /// </summary>
    public static IEnumerable<AppControlPolicy> FromDeviceConfiguration(JsonObject item)
    {
        var modes = new Dictionary<FileType, ControlMode>();
        foreach (var setting in (item["omaSettings"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (AppLockerUri().Match(Json.Text(setting, "omaUri")) is not { Success: true } uri
                || !AppLockerCollections.TryGetValue(uri.Groups["collection"].Value, out var type))
            {
                continue;
            }

            var mode = Json.Flag(setting, "isEncrypted") ? ControlMode.Unknown
                : EnforcementMode().Match(Json.Text(setting, "value")).Groups["mode"].Value switch
                {
                    "Enabled" => ControlMode.Enforced,
                    "AuditOnly" => ControlMode.Audit,
                    _ => (ControlMode?)null,
                };
            if (mode is { } known && (!modes.TryGetValue(type, out var before) || known < before))
            {
                modes[type] = known;
            }
        }

        if (modes.Count > 0)
        {
            yield return Targeted(AppLocker, Json.Text(item, "displayName"), item, modes);
        }
    }

    private static AppControlPolicy Targeted(string kind, string name, JsonObject item, IReadOnlyDictionary<FileType, ControlMode> modes)
    {
        var targets = (item["assignments"] as JsonArray ?? []).Select(a => a?["target"]).ToArray();
        var types = targets.Select(t => Json.Text(t, "@odata.type")).ToArray();
        return new(
            kind,
            name,
            types.Contains("#microsoft.graph.allDevicesAssignmentTarget"),
            types.Any(t => t.Length > 0 && t != "#microsoft.graph.exclusionGroupAssignmentTarget"),
            [.. targets.Where(t => Json.Text(t, "@odata.type") == "#microsoft.graph.groupAssignmentTarget").Select(t => Json.Text(t, "groupId"))],
            modes);
    }

    /// <summary>Every choice and simple setting value below <paramref name="node"/>, by setting definition ID, at any depth.</summary>
    private static IEnumerable<KeyValuePair<string, string>> Values(JsonNode? node) => node switch
    {
        JsonObject obj => Own(obj).Concat(obj.Select(p => p.Value).SelectMany(Values)),
        JsonArray array => array.SelectMany(Values),
        _ => [],
    };

    private static IEnumerable<KeyValuePair<string, string>> Own(JsonObject obj)
    {
        var key = Json.Text(obj, "settingDefinitionId");
        if (key.Length == 0)
        {
            yield break;
        }

        foreach (var value in new[] { Json.Text(obj["choiceSettingValue"], "value"), Json.Text(obj["simpleSettingValue"], "value") }.Where(v => v.Length > 0))
        {
            yield return KeyValuePair.Create(key, value);
        }
    }

    [GeneratedRegex(@"^\./Vendor/MSFT/AppLocker/ApplicationLaunchRestrictions/[^/]+/(?<collection>[^/]+)/Policy$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AppLockerUri();

    [GeneratedRegex(@"EnforcementMode\s*=\s*""(?<mode>[A-Za-z]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex EnforcementMode();
}
