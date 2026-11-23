using System.Globalization;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.M365DevServer;

/// <summary>Which tenant the dev server answers as: every control in place, or every named deviation applied.</summary>
public enum DevVariant
{
    Compliant,
    Gaps,
}

/// <summary>A directory account. <paramref name="Guest"/> accounts are invited, not members.</summary>
public sealed record Person(string Id, string Upn, string Name, bool Enabled = true, bool Synced = false, bool MfaCapable = true, bool Guest = false);

/// <summary>How a principal holds a directory role: a permanent active assignment, or eligibility through PIM.</summary>
public enum Grant
{
    Permanent,
    Eligible,
}

public sealed record RoleHolding(string PersonId, string RoleId, Grant Grant);

public sealed record Device(string Id, string Name, string OperatingSystem, string OsVersion, string ComplianceState, string OwnerUpn);

/// <summary>A Conditional Access policy as the dev tenant holds it, with where it came from.</summary>
public sealed record AccessPolicy(
    string Id,
    string Name,
    string State,
    IReadOnlyList<string> ClientAppTypes,
    IReadOnlyList<string> BuiltInControls,
    string? AuthenticationStrengthId,
    IReadOnlyList<string> ExcludeUsers,
    IReadOnlyList<string> ExcludeGroups,
    string Source);

/// <summary>
/// An AppLocker custom profile: the groups it is assigned to (none means all devices) and each rule collection's enforcement mode
/// (<c>Enabled</c> or <c>AuditOnly</c>), by its CSP name (<c>EXE</c>, <c>DLL</c>, <c>Script</c>, <c>MSI</c>).
/// </summary>
public sealed record AppLockerProfile(IReadOnlyList<string> Groups, IReadOnlyDictionary<string, string> Collections);

/// <summary>Application control events Defender for Endpoint received from one device in the window, by action type.</summary>
public sealed record ControlEvents(string DeviceId, string ActionType, int Events);

/// <summary>
/// What Defender for Endpoint holds: how many Windows devices are onboarded, the application control events received, and the
/// files blocked most often (name, invented hash, publisher, blocks).
/// </summary>
public sealed record Defender(int OnboardedWindows, IReadOnlyList<ControlEvents> Events, IReadOnlyList<(string FileName, string Sha256, string Publisher, int Blocks)> BlockedFiles);

/// <summary>A named way the <c>gaps</c> tenant differs from the compliant one, and the outcome each check it breaks is expected to have.</summary>
public sealed record Deviation(string Name, string Description, IReadOnlyDictionary<string, CheckOutcome> Fails, Func<DevTenant, DevTenant> Apply);

/// <summary>
/// The fictional Example Pty Ltd tenant, described once. <see cref="GraphCollections"/> renders it as the Graph collections the
/// m365 collector reads, so the same IDs appear in every collection. Intune and Conditional Access settings follow the ASD Blueprint
/// for Secure Cloud (see <see cref="Blueprint"/>); people, devices and roles are invented.
/// </summary>
public sealed record DevTenant(
    string TenantId,
    IReadOnlyList<Person> People,
    IReadOnlyList<RoleHolding> Roles,
    IReadOnlyList<Device> Devices,
    IReadOnlyList<AccessPolicy> AccessPolicies,
    bool OfficeHardeningAssigned,
    IReadOnlyDictionary<string, string> AsrRules,
    AppLockerProfile AppLocker,
    Defender Defender)
{
    public static readonly string GlobalAdministrator = "62e90394-69f5-4237-9190-012177145e10";
    public static readonly string ExchangeAdministrator = "29232cdf-9323-42fd-ade2-1d097af3e4de";
    public static readonly string SecurityAdministrator = "194ae4cb-b126-40b2-bd5b-6091b380977d";

    /// <summary>The built-in "Phishing-resistant MFA" authentication strength, the same in every tenant.</summary>
    public static readonly string PhishingResistantMfa = "00000000-0000-0000-0000-000000000004";

    public static readonly string SupportedWindowsBuilds = "10.0.22631, 10.0.26100";

    private static readonly string EntraDsc = "ASD Blueprint for Secure Cloud, automation/dsc/BlueprintEntraDsc.txt";

    private static readonly Person[] Staff =
    [
        new(Id(1, 1), "alex.admin@example.com", "Alex Admin"),
        new(Id(1, 2), "sam.admin@example.com", "Sam Admin"),
        new(Id(1, 3), "breakglass1@example.com", "Break glass 1"),
        new(Id(1, 4), "breakglass2@example.com", "Break glass 2"),
        new(Id(1, 5), "jo.example@example.com", "Jo Example"),
        new(Id(1, 6), "kim.example@example.com", "Kim Example"),
        new(Id(1, 7), "lee.example@example.com", "Lee Example"),
        new(Id(1, 8), "pat.example@example.com", "Pat Example"),
        new(Id(1, 9), "rob.example@example.com", "Rob Example"),
        new(Id(1, 10), "mia.example@example.com", "Mia Example"),
        new(Id(1, 11), "ana.example@example.com", "Ana Example"),
        new(Id(1, 12), "ben.example@example.com", "Ben Example"),
        new(Id(1, 13), "former.staff@example.com", "Former Staff", Enabled: false, MfaCapable: false),
        new(Id(1, 14), "auditor_example.net#EXT#@example.com", "Visiting Auditor", Guest: true, MfaCapable: false),
    ];

    private static string Alex => Staff[0].Id;

    private static string Sam => Staff[1].Id;

    private static string Jo => Staff[4].Id;

    private static string Ben => Staff[11].Id;

    private static IReadOnlyList<string> BreakGlass => [Staff[2].Id, Staff[3].Id];

    /// <summary>Example Pty Ltd with every control the m365 collector checks in place.</summary>
    public static DevTenant Compliant { get; } = new(
        "00000000-0000-4000-8000-00000000e044",
        Staff,
        [
            new(BreakGlass[0], GlobalAdministrator, Grant.Permanent),
            new(BreakGlass[1], GlobalAdministrator, Grant.Permanent),
            new(Alex, GlobalAdministrator, Grant.Eligible),
            new(Sam, GlobalAdministrator, Grant.Eligible),
            new(Alex, ExchangeAdministrator, Grant.Eligible),
            new(Sam, SecurityAdministrator, Grant.Eligible),
        ],
        [
            Laptop(1, "alex.admin", "10.0.26100.2605"),
            Laptop(2, "sam.admin", "10.0.26100.2605"),
            Laptop(3, "jo.example", "10.0.26100.2605"),
            Laptop(4, "kim.example", "10.0.26100.2605"),
            Laptop(5, "lee.example", "10.0.22631.4602"),
            Laptop(6, "pat.example", "10.0.22631.4602"),
            Laptop(7, "rob.example", "10.0.26100.2605"),
            Laptop(8, "mia.example", "10.0.26100.2605"),
            new(Id(4, 9), "EX-PH-0009", "iOS", "18.1.1", "compliant", "ana.example@example.com"),
        ],
        [
            new(Id(5, 1), "USR - G - Require strong auth", "enabled", ["all"], [], PhishingResistantMfa, BreakGlass, [], EntraDsc),
            new(Id(5, 2), "USR - B - Block access via legacy auth", "enabled", ["exchangeActiveSync", "other"], ["block"], null, BreakGlass, [], EntraDsc),
        ],
        OfficeHardeningAssigned: true,
        Blueprint.AsrRules,
        new([], new Dictionary<string, string>(StringComparer.Ordinal) { ["EXE"] = "Enabled", ["DLL"] = "Enabled", ["Script"] = "Enabled", ["MSI"] = "Enabled" }),
        new(
            8,
            [
                new(MdeId(3), "AppLockerBlockExecutable", 2),
                new(MdeId(5), "AppLockerBlockScript", 1),
                new(MdeId(5), "AppLockerAuditExecutable", 4),
            ],
            [
                ("example-portable-tool.exe", new string('0', 58) + "ex0001", "", 2),
                ("unsigned-installer.ps1", new string('0', 58) + "ex0002", "", 1),
            ]));

    /// <summary>The deviations the <c>gaps</c> tenant carries, each failing the checks it names and no other.</summary>
    public static IReadOnlyList<Deviation> Deviations { get; } =
    [
        new(
            "staff-member-without-mfa",
            "Ben has registered no MFA method: 11 of 12 enabled members are MFA-capable.",
            Expect(("identity.mfa.registered_all_users", CheckOutcome.Partial)),
            t => t with { People = [.. t.People.Select(p => p.Id == Ben ? p with { MfaCapable = false } : p)] }),
        new(
            "mfa-policy-report-only",
            "\"USR - G - Require strong auth\" is left in report-only mode, as the Blueprint ships it.",
            Expect(("identity.mfa.enforced_conditional_access", CheckOutcome.Fail), ("identity.mfa.phishing_resistant_admins", CheckOutcome.Fail)),
            t => t.EditPolicy(Id(5, 1), p => p with { State = "enabledForReportingButNotEnforced" })),
        new(
            "legacy-auth-excludes-a-group",
            "The legacy authentication block excludes a group that is not documented as break-glass.",
            Expect(("identity.legacy_auth_blocked", CheckOutcome.Fail)),
            t => t.EditPolicy(Id(5, 2), p => p with { ExcludeGroups = [Id(6, 1)] })),
        new(
            "permanent-global-admin",
            "Jo holds Global Administrator permanently: five Global Administrators, one of them not through PIM.",
            Expect(("identity.privileged.global_admin_count", CheckOutcome.Fail), ("identity.privileged.pim_eligible_not_permanent", CheckOutcome.Fail)),
            t => t with { Roles = [.. t.Roles, new(Jo, GlobalAdministrator, Grant.Permanent)] }),
        new(
            "synced-admin",
            "Alex's admin account is synchronised from on-premises Active Directory.",
            Expect(("identity.privileged.admins_cloud_only", CheckOutcome.Fail)),
            t => t with { People = [.. t.People.Select(p => p.Id == Alex ? p with { Synced = true } : p)] }),
        new(
            "unsupported-windows-build",
            "One laptop still runs Windows 10 22H2 (10.0.19045).",
            Expect(("endpoint.os.supported_versions", CheckOutcome.Fail)),
            t => t.EditDevice(Id(4, 6), d => d with { OsVersion = "10.0.19045.5131" })),
        new(
            "noncompliant-laptop",
            "One of eight Windows laptops is not compliant: 87.5 %, between the partial and pass thresholds.",
            Expect(("endpoint.managed.compliant_ratio", CheckOutcome.Partial)),
            t => t.EditDevice(Id(4, 7), d => d with { ComplianceState = "noncompliant" })),
        new(
            "office-hardening-unassigned",
            "The ASD Office hardening policy exists but is assigned to nobody.",
            Expect(("apps.office.macros_blocked_from_internet", CheckOutcome.Fail)),
            t => t with { OfficeHardeningAssigned = false }),
        new(
            "asr-office-rule-in-audit",
            "\"Block Win32 API calls from Office macros\" is in audit mode instead of block.",
            Expect(("endpoint.asr.office_rules_enabled", CheckOutcome.Partial)),
            t => t with { AsrRules = new Dictionary<string, string>(t.AsrRules, StringComparer.Ordinal) { ["blockwin32apicallsfromofficemacros"] = "audit" } }),
        new(
            "applocker-for-one-group",
            "The AppLocker profile is assigned to one group of kiosks instead of all devices.",
            Expect(("endpoint.application_control.enforced", CheckOutcome.Partial)),
            t => t with { AppLocker = t.AppLocker with { Groups = [Id(6, 2)] } }),
        new(
            "applocker-dll-rules-in-audit",
            "The AppLocker DLL rule collection is in audit mode, so software libraries are not controlled.",
            Expect(("endpoint.application_control.file_types_covered", CheckOutcome.Fail)),
            t => t with { AppLocker = t.AppLocker with { Collections = new Dictionary<string, string>(t.AppLocker.Collections, StringComparer.Ordinal) { ["DLL"] = "AuditOnly" } } }),
        new(
            "defender-missing-on-two-laptops",
            "Two of the eight Windows laptops are not onboarded to Defender for Endpoint (75 %), so their application control events are not centrally logged.",
            Expect(("endpoint.application_control.events_centrally_logged", CheckOutcome.Partial)),
            t => t with { Defender = t.Defender with { OnboardedWindows = 6 } }),
    ];

    public static DevTenant For(DevVariant variant) =>
        variant == DevVariant.Compliant ? Compliant : Deviations.Aggregate(Compliant, (tenant, deviation) => tenant.With(deviation));

    /// <summary>Settings for an m365 collector instance that reads this tenant; add <c>graph_url</c> for the server.</summary>
    public static Dictionary<string, string> Settings() => new(StringComparer.Ordinal)
    {
        ["tenant_id"] = Compliant.TenantId,
        ["client_id"] = "00000000-0000-4000-8000-00000000c044",
        ["break_glass_accounts"] = string.Join(", ", BreakGlass),
        ["supported_windows_builds"] = SupportedWindowsBuilds,
    };

    public DevTenant With(Deviation deviation)
    {
        ArgumentNullException.ThrowIfNull(deviation);
        return deviation.Apply(this);
    }

    /// <summary>A GUID-shaped ID that is obviously invented: group <paramref name="group"/>, number <paramref name="n"/>.</summary>
    internal static string Id(int group, int n) =>
        string.Create(CultureInfo.InvariantCulture, $"00000000-0000-4000-8{group:x3}-{n:x12}");

    private DevTenant EditPolicy(string id, Func<AccessPolicy, AccessPolicy> edit) =>
        this with { AccessPolicies = [.. AccessPolicies.Select(p => p.Id == id ? edit(p) : p)] };

    private DevTenant EditDevice(string id, Func<Device, Device> edit) =>
        this with { Devices = [.. Devices.Select(d => d.Id == id ? edit(d) : d)] };

    /// <summary>An invented 40-character Defender for Endpoint device ID.</summary>
    private static string MdeId(int n) => string.Create(CultureInfo.InvariantCulture, $"{n:x40}");

    private static Device Laptop(int n, string user, string version) =>
        new(Id(4, n), string.Create(CultureInfo.InvariantCulture, $"EX-LT-{n:0000}"), "Windows", version, "compliant", $"{user}@example.com");

    private static Dictionary<string, CheckOutcome> Expect(params (string Check, CheckOutcome Outcome)[] fails) =>
        fails.ToDictionary(f => f.Check, f => f.Outcome, StringComparer.Ordinal);
}
