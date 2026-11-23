using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.M365;

/// <summary>Office hardening checks over Intune policies: macros from the internet, and the Office attack surface reduction rules.</summary>
internal static class OfficeChecks
{
    private static readonly (string Name, string Prefix)[] OfficeApps =
    [
        ("Word", "user_vendor_msft_policy_config_word16"),
        ("Excel", "user_vendor_msft_policy_config_excel16"),
        ("PowerPoint", "user_vendor_msft_policy_config_ppt16"),
    ];

    private static readonly string AsrPrefix = "device_vendor_msft_policy_config_defender_attacksurfacereductionrules_";

    /// <summary>The four Office rules: their name, settings catalog fragment, and endpoint protection profile property.</summary>
    private static readonly (string Name, string Fragment, string Property)[] AsrRules =
    [
        ("Block all Office applications from creating child processes", "blockallofficeapplicationsfromcreatingchildprocesses", "defenderOfficeAppsLaunchChildProcessType"),
        ("Block Office applications from creating executable content", "blockofficeapplicationsfromcreatingexecutablecontent", "defenderOfficeAppsExecutableContentCreationOrLaunchType"),
        ("Block Office applications from injecting code into other processes", "blockofficeapplicationsfrominjectingcodeintootherprocesses", "defenderOfficeAppsOtherProcessInjectionType"),
        ("Block Win32 API calls from Office macros", "blockwin32apicallsfromofficemacros", "defenderOfficeMacroCodeAllowWin32ImportsType"),
    ];

    private enum Mode
    {
        NotConfigured,
        Audit,
        Warn,
        Block,
    }

    public static Finding MacrosBlockedFromInternet(TenantData tenant, M365Options options)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        const string id = "apps.office.macros_blocked_from_internet";
        string[] sources = [GraphSources.ConfigurationPolicies];
        var assigned = tenant.ConfigurationPolicies.Where(p => p.Assigned).ToArray();
        var apps = OfficeApps.Select(app => (
            app.Name,
            Policies: assigned.Where(p => BlocksMacros(p, app.Prefix)).ToArray())).ToArray();
        var forAllUsers = apps.Where(a => a.Policies.Any(p => p.AllUsers)).ToArray();
        var anywhere = apps.Where(a => a.Policies.Length > 0).ToArray();

        if (forAllUsers.Length == apps.Length)
        {
            var policies = Policies(forAllUsers.SelectMany(a => a.Policies.Where(p => p.AllUsers)).Select(p => p.Name));
            return new(id, CheckOutcome.Pass, $"Macros from the internet are blocked for all users in {Prose.And(apps.Select(a => a.Name))} ({policies}).", sources);
        }

        if (forAllUsers.Length > 0)
        {
            var missing = apps.Select(a => a.Name).Except(forAllUsers.Select(a => a.Name), StringComparer.Ordinal);
            return new(id, CheckOutcome.Partial, $"Macros from the internet are blocked for all users in {Prose.And(forAllUsers.Select(a => a.Name))}, but not in {Prose.And(missing)}.", sources);
        }

        return anywhere.Length > 0
            ? new(id, CheckOutcome.Partial, $"Macros from the internet are blocked in {Prose.And(anywhere.Select(a => a.Name))}, but only for some groups, not all users.", sources)
            : new(id, CheckOutcome.Fail, "No assigned configuration policy blocks macros from the internet in Word, Excel or PowerPoint.", sources);
    }

    public static Finding AsrOfficeRulesEnabled(TenantData tenant, M365Options options)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        const string id = "endpoint.asr.office_rules_enabled";
        string[] sources = [GraphSources.ConfigurationPolicies, GraphSources.DeviceConfigurations];
        var catalog = tenant.ConfigurationPolicies.Where(p => p.Assigned).ToArray();
        var profiles = tenant.DeviceConfigurations.Where(p => p.Assigned).ToArray();
        var rules = AsrRules.Select(rule =>
        {
            var modes = catalog.Select(p => (p.Name, Mode: CatalogMode(p, rule.Fragment)))
                .Concat(profiles.Select(p => (p.Name, Mode: ProfileMode(p.Find(rule.Property)))))
                .ToArray();
            var best = modes.Length == 0 ? Mode.NotConfigured : modes.Max(m => m.Mode);
            return (rule.Name, Mode: best, Policies: modes.Where(m => m.Mode == Mode.Block).Select(m => m.Name).ToArray());
        }).ToArray();

        var blocking = rules.Where(r => r.Mode == Mode.Block).ToArray();
        if (blocking.Length == rules.Length)
        {
            return new(id, CheckOutcome.Pass, $"All {rules.Length} Office attack surface reduction rules are in block mode ({Policies(blocking.SelectMany(r => r.Policies))}).", sources);
        }

        if (rules.All(r => r.Mode == Mode.NotConfigured))
        {
            return new(id, CheckOutcome.Fail, "No assigned policy puts any Office attack surface reduction rule in block or audit mode.", sources);
        }

        var notBlocking = string.Join(", ", rules.Where(r => r.Mode != Mode.Block).Select(r => $"{r.Name} ({Describe(r.Mode)})"));
        return new(id, CheckOutcome.Partial, $"{blocking.Length} of {rules.Length} Office attack surface reduction rules are in block mode. Not blocking: {notBlocking}.", sources);
    }

    private static bool BlocksMacros(IntunePolicy policy, string appPrefix) =>
        policy.Settings.Any(s =>
            s.Key.StartsWith(appPrefix, StringComparison.OrdinalIgnoreCase)
            && s.Key.EndsWith("blockmacroexecutionfrominternet", StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Value, s.Key + "_1", StringComparison.OrdinalIgnoreCase));

    private static Mode CatalogMode(IntunePolicy policy, string fragment) =>
        policy.Find(AsrPrefix + fragment) is { } value && value.LastIndexOf('_') is var at and >= 0
            ? value[(at + 1)..].ToUpperInvariant() switch
            {
                "BLOCK" => Mode.Block,
                "WARN" => Mode.Warn,
                "AUDIT" => Mode.Audit,
                _ => Mode.NotConfigured,
            }
            : Mode.NotConfigured;

    private static Mode ProfileMode(string? value) => value switch
    {
        "block" => Mode.Block,
        "warn" => Mode.Warn,
        "auditMode" => Mode.Audit,
        _ => Mode.NotConfigured,
    };

    private static string Describe(Mode mode) => mode switch
    {
        Mode.Audit => "audit",
        Mode.Warn => "warn",
        _ => "not configured",
    };

    /// <summary>"policy 'A'" or "policies 'A', 'B'", each policy named once.</summary>
    private static string Policies(IEnumerable<string> names)
    {
        var distinct = names.Distinct(StringComparer.Ordinal).Select(n => $"'{n}'").ToArray();
        return $"{(distinct.Length == 1 ? "policy" : "policies")} {string.Join(", ", distinct)}";
    }
}
