namespace EvidenceHound.Collectors.M365;

/// <summary>
/// One check the collector reports: its ID, the capabilities it judges (Essential Eight mitigation codes, as <c>scope/capabilities.yaml</c>
/// names them) and the pure function that evaluates it.
/// </summary>
internal sealed record M365Check(string Id, IReadOnlyList<string> Capabilities, Func<TenantData, M365Options, Finding> Evaluate);

/// <summary>Every check, in the order they are reported, grouped by capability.</summary>
internal static class M365Checks
{
    internal const string Mfa = "mfa";
    internal const string AdminPrivileges = "rap";
    internal const string PatchOperatingSystems = "pos";
    internal const string OfficeMacros = "rmom";
    internal const string UserApplicationHardening = "uah";
    internal const string ApplicationControl = "ac";

    public static IReadOnlyList<M365Check> All { get; } =
    [
        new("identity.mfa.registered_all_users", [Mfa], MfaChecks.RegisteredAllUsers),
        new("identity.mfa.enforced_conditional_access", [Mfa], MfaChecks.EnforcedByConditionalAccess),
        new("identity.legacy_auth_blocked", [Mfa], MfaChecks.LegacyAuthBlocked),
        new("identity.mfa.phishing_resistant_admins", [Mfa], MfaChecks.PhishingResistantAdmins),
        new("identity.privileged.global_admin_count", [AdminPrivileges], PrivilegedChecks.GlobalAdminCount),
        new("identity.privileged.admins_cloud_only", [AdminPrivileges], PrivilegedChecks.AdminsCloudOnly),
        new("identity.privileged.pim_eligible_not_permanent", [AdminPrivileges], PrivilegedChecks.PimEligibleNotPermanent),
        new("endpoint.managed.compliant_ratio", [PatchOperatingSystems], DeviceChecks.CompliantRatio),
        new("endpoint.os.supported_versions", [PatchOperatingSystems], DeviceChecks.SupportedVersions),
        new("apps.office.macros_blocked_from_internet", [OfficeMacros], OfficeChecks.MacrosBlockedFromInternet),
        new("endpoint.asr.office_rules_enabled", [OfficeMacros, UserApplicationHardening], OfficeChecks.AsrOfficeRulesEnabled),
        new("endpoint.application_control.enforced", [ApplicationControl], AppControlChecks.Enforced),
        new("endpoint.application_control.file_types_covered", [ApplicationControl], AppControlChecks.FileTypesCovered),
    ];

    /// <summary>The capabilities the checks judge, in order.</summary>
    public static IReadOnlyList<string> Capabilities { get; } = [.. All.SelectMany(c => c.Capabilities).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// The check's finding, or not applicable when every capability it judges is provided by another product, naming that product.
    /// </summary>
    public static Finding Evaluate(M365Check check, TenantData tenant, M365Options options, IReadOnlyDictionary<string, string> elsewhere) =>
        check.Capabilities.All(elsewhere.ContainsKey)
            ? new Finding(
                check.Id,
                Core.Checks.CheckOutcome.NotApplicable,
                $"Not judged on Microsoft 365: scope/capabilities.yaml records {string.Join("; ", check.Capabilities.Select(c => $"{c} as provided by {elsewhere[c]}"))}.",
                [])
            : check.Evaluate(tenant, options);
}
