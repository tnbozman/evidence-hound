namespace EvidenceHound.Collectors.M365;

/// <summary>
/// Built-in Entra roles that hold tenant-wide administrative power, by role template ID (the same in every tenant). The default for
/// the <c>privileged_roles</c> setting.
/// </summary>
internal static class PrivilegedRoles
{
    public const string GlobalAdministrator = "62e90394-69f5-4237-9190-012177145e10";

    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [GlobalAdministrator] = "Global Administrator",
        ["e8611ab8-c189-46e8-94e1-60213ab1f814"] = "Privileged Role Administrator",
        ["7be44c8a-adaf-4e2a-84d6-ab2649e08a13"] = "Privileged Authentication Administrator",
        ["194ae4cb-b126-40b2-bd5b-6091b380977d"] = "Security Administrator",
        ["b1be1c3e-b65d-4f19-8427-f6fa0d97feb9"] = "Conditional Access Administrator",
        ["29232cdf-9323-42fd-ade2-1d097af3e4de"] = "Exchange Administrator",
        ["f28a1f50-f6e7-4571-818b-6a12f2af6b6c"] = "SharePoint Administrator",
        ["fe930be7-5e62-47db-91af-98c3a49a38b1"] = "User Administrator",
        ["c4e39bd9-1100-46d3-8c65-fb160da0071f"] = "Authentication Administrator",
        ["9b895d92-2cd3-44c7-9d02-a6ac2d5ea5c3"] = "Application Administrator",
        ["158c047a-c907-4556-b7ef-446551a6b5f7"] = "Cloud Application Administrator",
        ["3a2c62db-5318-420d-8d74-23affee5d9d5"] = "Intune Administrator",
        ["729827e3-9c14-49f7-bb1b-9608f156bbb8"] = "Helpdesk Administrator",
        ["b0f54661-2d74-4c50-afa3-1ec803f12efe"] = "Billing Administrator",
    };

    public static IReadOnlyList<string> Default { get; } = [.. Names.Keys];

    /// <summary>The role's name, or its ID when it is not a known built-in role.</summary>
    public static string NameOf(string roleId) => Names.GetValueOrDefault(roleId, roleId);
}
