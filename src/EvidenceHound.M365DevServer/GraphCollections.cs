using System.Text.Json.Nodes;

namespace EvidenceHound.M365DevServer;

/// <summary>Renders a <see cref="DevTenant"/> as the Microsoft Graph collections the m365 collector reads, item by item.</summary>
public static class GraphCollections
{
    /// <summary>Each collection's name (also the evidence file the collector writes) and the Graph path that answers it.</summary>
    public static IReadOnlyDictionary<string, string> Paths { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["users"] = "/v1.0/users",
        ["mfa-registration"] = "/v1.0/reports/authenticationMethods/userRegistrationDetails",
        ["conditional-access-policies"] = "/v1.0/identity/conditionalAccess/policies",
        ["role-assignments"] = "/v1.0/roleManagement/directory/roleAssignments",
        ["role-eligibility-schedules"] = "/v1.0/roleManagement/directory/roleEligibilitySchedules",
        ["role-assignment-schedule-instances"] = "/v1.0/roleManagement/directory/roleAssignmentScheduleInstances",
        ["managed-devices"] = "/v1.0/deviceManagement/managedDevices",
        ["device-configurations"] = "/v1.0/deviceManagement/deviceConfigurations",
        ["configuration-policies"] = "/beta/deviceManagement/configurationPolicies",
        ["hunting-app-control-events"] = HuntingPath,
        ["hunting-app-control-blocked-files"] = HuntingPath,
        ["hunting-defender-onboarding"] = HuntingPath,
    };

    /// <summary>Advanced hunting: one POST address, each query told apart by its <c>// evidence-hound: &lt;name&gt;</c> marker.</summary>
    public const string HuntingPath = "/v1.0/security/runHuntingQuery";

    public static bool IsHunting(string name) => name.StartsWith("hunting-", StringComparison.Ordinal);

    private static readonly string AsrSetting = "device_vendor_msft_policy_config_defender_attacksurfacereductionrules";

    private static readonly string Since = "2026-07-01T00:00:00Z";

    public static Dictionary<string, JsonArray> Render(DevTenant tenant, Uri graph)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(graph);
        return new(StringComparer.Ordinal)
        {
            ["users"] = [.. tenant.People.Select(User)],
            ["mfa-registration"] = [.. tenant.People.Select(Registration)],
            ["conditional-access-policies"] = [.. tenant.AccessPolicies.Select(Policy)],
            ["role-assignments"] = [.. Held(tenant, Grant.Permanent).Select((r, i) => Role(r, $"ra-{i + 1:0000}"))],
            ["role-eligibility-schedules"] = [.. Held(tenant, Grant.Eligible).Select((r, i) => Eligibility(r, $"es-{i + 1:0000}"))],
            ["role-assignment-schedule-instances"] = [.. Held(tenant, Grant.Permanent).Select((r, i) => Instance(r, $"ai-{i + 1:0000}"))],
            ["managed-devices"] = [.. tenant.Devices.Select(Device)],
            ["device-configurations"] = [DeviceConfiguration(), AppLocker(tenant.AppLocker)],
            ["configuration-policies"] = [OfficeHardening(tenant.OfficeHardeningAssigned), Asr(tenant.AsrRules)],
            ["hunting-app-control-events"] = [.. tenant.Defender.Events.Select(e => (JsonNode)new JsonObject
            {
                ["DeviceId"] = e.DeviceId,
                ["ActionType"] = e.ActionType,
                ["Events"] = e.Events,
                ["LastSeen"] = "2026-11-22T21:40:00Z",
            })],
            ["hunting-app-control-blocked-files"] = [.. tenant.Defender.BlockedFiles.Select(f => (JsonNode)new JsonObject
            {
                ["FileName"] = f.FileName,
                ["SHA256"] = f.Sha256,
                ["Publisher"] = f.Publisher,
                ["Blocks"] = f.Blocks,
                ["Devices"] = 1,
            })],
            ["hunting-defender-onboarding"] = new JsonArray(new JsonObject { ["OnboardingStatus"] = "Onboarded", ["Devices"] = tenant.Defender.OnboardedWindows }),
        };
    }

    private static IEnumerable<RoleHolding> Held(DevTenant tenant, Grant grant) => tenant.Roles.Where(r => r.Grant == grant);

    private static JsonObject User(Person person) => new()
    {
        ["id"] = person.Id,
        ["userPrincipalName"] = person.Upn,
        ["displayName"] = person.Name,
        ["accountEnabled"] = person.Enabled,
        ["userType"] = person.Guest ? "Guest" : "Member",
        ["onPremisesSyncEnabled"] = person.Synced ? true : null,
    };

    private static JsonObject Registration(Person person) => new()
    {
        ["id"] = person.Id,
        ["userPrincipalName"] = person.Upn,
        ["userDisplayName"] = person.Name,
        ["userType"] = person.Guest ? "guest" : "member",
        ["isMfaCapable"] = person.MfaCapable,
        ["isMfaRegistered"] = person.MfaCapable,
        ["methodsRegistered"] = person.MfaCapable ? new JsonArray("microsoftAuthenticatorPush", "fido2") : new JsonArray(),
    };

    private static JsonObject Policy(AccessPolicy policy) => new()
    {
        ["id"] = policy.Id,
        ["displayName"] = policy.Name,
        ["description"] = $"From the {policy.Source}.",
        ["state"] = policy.State,
        ["conditions"] = new JsonObject
        {
            ["clientAppTypes"] = Strings(policy.ClientAppTypes),
            ["applications"] = new JsonObject { ["includeApplications"] = Strings(["All"]), ["excludeApplications"] = new JsonArray() },
            ["users"] = new JsonObject
            {
                ["includeUsers"] = Strings(["All"]),
                ["excludeUsers"] = Strings(policy.ExcludeUsers),
                ["includeGroups"] = new JsonArray(),
                ["excludeGroups"] = Strings(policy.ExcludeGroups),
                ["includeRoles"] = new JsonArray(),
                ["excludeRoles"] = new JsonArray(),
            },
        },
        ["grantControls"] = new JsonObject
        {
            ["operator"] = "OR",
            ["builtInControls"] = Strings(policy.BuiltInControls),
            ["authenticationStrength"] = policy.AuthenticationStrengthId is { } strength ? new JsonObject { ["id"] = strength } : null,
        },
    };

    private static JsonObject Role(RoleHolding role, string id) => new()
    {
        ["id"] = id,
        ["principalId"] = role.PersonId,
        ["roleDefinitionId"] = role.RoleId,
        ["directoryScopeId"] = "/",
    };

    private static JsonObject Eligibility(RoleHolding role, string id)
    {
        var item = Role(role, id);
        item["memberType"] = "Direct";
        item["status"] = "Provisioned";
        item["scheduleInfo"] = new JsonObject { ["startDateTime"] = Since, ["expiration"] = new JsonObject { ["type"] = "noExpiration" } };
        return item;
    }

    private static JsonObject Instance(RoleHolding role, string id)
    {
        var item = Role(role, id);
        item["assignmentType"] = "Assigned";
        item["memberType"] = "Direct";
        item["startDateTime"] = Since;
        item["endDateTime"] = null;
        return item;
    }

    private static JsonObject Device(Device device) => new()
    {
        ["id"] = device.Id,
        ["deviceName"] = device.Name,
        ["operatingSystem"] = device.OperatingSystem,
        ["osVersion"] = device.OsVersion,
        ["complianceState"] = device.ComplianceState,
        ["userPrincipalName"] = device.OwnerUpn,
    };

    private static JsonObject DeviceConfiguration() => new()
    {
        ["@odata.type"] = "#microsoft.graph.windows10GeneralConfiguration",
        ["id"] = DevTenant.Id(7, 1),
        ["displayName"] = "Windows baseline restrictions",
        ["passwordRequired"] = true,
        ["assignments"] = new JsonArray(Assignment("dc01-a1", "#microsoft.graph.allDevicesAssignmentTarget")),
    };

    /// <summary>An invented AppLocker custom profile: one OMA-URI setting per rule collection, each allowing only the Windows folder.</summary>
    private static JsonObject AppLocker(AppLockerProfile profile) => new()
    {
        ["@odata.type"] = "#microsoft.graph.windows10CustomConfiguration",
        ["id"] = DevTenant.Id(7, 2),
        ["displayName"] = "AppLocker - Windows folder only",
        ["description"] = "Invented for the dev tenant: AppLocker rules through the AppLocker CSP.",
        ["omaSettings"] = new JsonArray([.. profile.Collections.Select((c, i) => (JsonNode)new JsonObject
        {
            ["@odata.type"] = "#microsoft.graph.omaSettingString",
            ["displayName"] = $"AppLocker {c.Key}",
            ["omaUri"] = $"./Vendor/MSFT/AppLocker/ApplicationLaunchRestrictions/ExampleApps/{c.Key}/Policy",
            ["isEncrypted"] = false,
            ["value"] = $"<RuleCollection Type=\"{RuleType(c.Key)}\" EnforcementMode=\"{c.Value}\"><FilePathRule Id=\"{DevTenant.Id(9, i + 1)}\" Name=\"Windows folder\" UserOrGroupSid=\"S-1-1-0\" Action=\"Allow\"><Conditions><FilePathCondition Path=\"%WINDIR%\\*\" /></Conditions></FilePathRule></RuleCollection>",
        })]),
        ["assignments"] = profile.Groups.Count == 0
            ? new JsonArray(Assignment("al-a1", "#microsoft.graph.allDevicesAssignmentTarget"))
            : new JsonArray([.. profile.Groups.Select((g, i) => (JsonNode)new JsonObject
            {
                ["id"] = $"al-g{i + 1}",
                ["target"] = new JsonObject { ["@odata.type"] = "#microsoft.graph.groupAssignmentTarget", ["groupId"] = g },
            })]),
    };

    private static string RuleType(string collection) => collection switch
    {
        "EXE" => "Exe",
        "DLL" => "Dll",
        "MSI" => "Msi",
        _ => collection,
    };

    /// <summary>The Blueprint's Office hardening export as published, plus the description and assignment an export does not carry.</summary>
    private static JsonObject OfficeHardening(bool assigned)
    {
        var policy = Blueprint.OfficeHardening();
        policy.Remove("@odata.context");
        policy["description"] = $"From the ASD Blueprint for Secure Cloud, intune-config-policies/OfficeHardening.txt at {Blueprint.Commit[..7]}.";
        policy["assignments"] = assigned ? new JsonArray(Assignment("oh-a1", "#microsoft.graph.allLicensedUsersAssignmentTarget")) : new JsonArray();
        return policy;
    }

    private static JsonObject Asr(IReadOnlyDictionary<string, string> rules) => new()
    {
        ["id"] = DevTenant.Id(8, 1),
        ["name"] = "ASD Windows Hardening Guidelines - Attack Surface Reduction",
        ["description"] = "From the ASD Blueprint for Secure Cloud, automation/dsc/BlueprintDefenderDsc.txt (IntuneSettingCatalogASRRulesPolicyWindows10).",
        ["platforms"] = "windows10",
        ["technologies"] = "mdm,microsoftSense",
        ["settings"] = new JsonArray(new JsonObject
        {
            ["id"] = "0",
            ["settingInstance"] = new JsonObject
            {
                ["@odata.type"] = "#microsoft.graph.deviceManagementConfigurationGroupSettingCollectionInstance",
                ["settingDefinitionId"] = AsrSetting,
                ["groupSettingCollectionValue"] = new JsonArray(new JsonObject
                {
                    ["children"] = new JsonArray([.. rules.Select(rule => (JsonNode)new JsonObject
                    {
                        ["@odata.type"] = "#microsoft.graph.deviceManagementConfigurationChoiceSettingInstance",
                        ["settingDefinitionId"] = $"{AsrSetting}_{rule.Key}",
                        ["choiceSettingValue"] = new JsonObject { ["value"] = $"{AsrSetting}_{rule.Key}_{rule.Value}", ["children"] = new JsonArray() },
                    })]),
                }),
            },
        }),
        ["assignments"] = new JsonArray(Assignment("asr-a1", "#microsoft.graph.allLicensedUsersAssignmentTarget")),
    };

    private static JsonObject Assignment(string id, string target) =>
        new() { ["id"] = id, ["target"] = new JsonObject { ["@odata.type"] = target } };

    private static JsonArray Strings(IEnumerable<string> values) => [.. values.Select(v => (JsonNode)JsonValue.Create(v))];
}
