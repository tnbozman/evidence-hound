using System.Text.Json.Nodes;
using EvidenceHound.Core.Checks;
using static EvidenceHound.Collectors.M365.Tests.Tenants;

namespace EvidenceHound.Collectors.M365.Tests;

public class OfficeChecksTests
{
    private static readonly string Macros = "00000000-0000-4000-8000-00000000cp01";

    private static readonly string Asr = "00000000-0000-4000-8000-00000000cp02";

    private static readonly string AsrPrefix = "device_vendor_msft_policy_config_defender_attacksurfacereductionrules";

    [Fact]
    public void Macros_blocked_for_all_users_passes()
    {
        var finding = OfficeChecks.MacrosBlockedFromInternet(Recorded(), Options());

        finding.CheckId.ShouldBe("apps.office.macros_blocked_from_internet");
        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("Macros from the internet are blocked for all users in Word, Excel and PowerPoint (policy 'Office macro hardening').");
        finding.Sources.ShouldBe(["configuration-policies"]);
    }

    [Fact]
    public void Macros_blocked_in_some_apps_partial()
    {
        var tenant = Recorded(items => items.Item("configuration-policies", Macros)["settings"]!.AsArray().RemoveAt(2));

        var finding = OfficeChecks.MacrosBlockedFromInternet(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Partial);
        finding.Observation.ShouldBe("Macros from the internet are blocked for all users in Word and Excel, but not in PowerPoint.");
    }

    [Fact]
    public void Macros_blocked_only_for_a_group_partial()
    {
        var tenant = Recorded(items => items.Item("configuration-policies", Macros)["assignments"] = new JsonArray(
            new JsonObject { ["id"] = "g1", ["target"] = new JsonObject { ["@odata.type"] = "#microsoft.graph.groupAssignmentTarget", ["groupId"] = "00000000-0000-4000-8000-0000000000f1" } }));

        var finding = OfficeChecks.MacrosBlockedFromInternet(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Partial);
        finding.Observation.ShouldBe("Macros from the internet are blocked in Word, Excel and PowerPoint, but only for some groups, not all users.");
    }

    [Fact]
    public void Macros_setting_disabled_fails()
    {
        var tenant = Recorded(items =>
        {
            foreach (var setting in items.Item("configuration-policies", Macros)["settings"]!.AsArray())
            {
                var value = setting!["settingInstance"]!["choiceSettingValue"]!;
                value["value"] = value["value"]!.GetValue<string>().Replace("_1", "_0", StringComparison.Ordinal);
            }
        });

        var finding = OfficeChecks.MacrosBlockedFromInternet(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe("No assigned configuration policy blocks macros from the internet in Word, Excel or PowerPoint.");
    }

    [Fact]
    public void Macros_policy_not_assigned_fails()
    {
        var tenant = Recorded(items => items.Item("configuration-policies", Macros)["assignments"] = new JsonArray());

        OfficeChecks.MacrosBlockedFromInternet(tenant, Options()).Outcome.ShouldBe(CheckOutcome.Fail);
    }

    [Fact]
    public void Asr_office_rules_in_block_mode_passes()
    {
        var finding = OfficeChecks.AsrOfficeRulesEnabled(Recorded(), Options());

        finding.CheckId.ShouldBe("endpoint.asr.office_rules_enabled");
        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("All 4 Office attack surface reduction rules are in block mode (policy 'Attack surface reduction (Office)').");
        finding.Sources.ShouldBe(["configuration-policies", "device-configurations"]);
    }

    [Fact]
    public void Asr_rule_in_audit_mode_partial()
    {
        var tenant = Recorded(items => SetAsr(items, "blockwin32apicallsfromofficemacros", "audit"));

        var finding = OfficeChecks.AsrOfficeRulesEnabled(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Partial);
        finding.Observation.ShouldBe("3 of 4 Office attack surface reduction rules are in block mode. Not blocking: Block Win32 API calls from Office macros (audit).");
    }

    [Fact]
    public void Asr_rules_not_configured_fails()
    {
        var tenant = Recorded(items => items["configuration-policies"].Remove(items.Item("configuration-policies", Asr)));

        var finding = OfficeChecks.AsrOfficeRulesEnabled(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe("No assigned policy puts any Office attack surface reduction rule in block or audit mode.");
    }

    [Fact]
    public void Asr_rules_from_an_endpoint_protection_profile_pass()
    {
        var tenant = Recorded(items =>
        {
            items["configuration-policies"].Remove(items.Item("configuration-policies", Asr));
            items["device-configurations"].Add(new JsonObject
            {
                ["@odata.type"] = "#microsoft.graph.windows10EndpointProtectionConfiguration",
                ["id"] = "00000000-0000-4000-8000-00000000dc02",
                ["displayName"] = "Endpoint protection (legacy)",
                ["defenderOfficeAppsLaunchChildProcessType"] = "block",
                ["defenderOfficeAppsExecutableContentCreationOrLaunchType"] = "block",
                ["defenderOfficeAppsOtherProcessInjectionType"] = "block",
                ["defenderOfficeMacroCodeAllowWin32ImportsType"] = "block",
                ["assignments"] = new JsonArray(new JsonObject { ["id"] = "a", ["target"] = new JsonObject { ["@odata.type"] = "#microsoft.graph.allDevicesAssignmentTarget" } }),
            });
        });

        var finding = OfficeChecks.AsrOfficeRulesEnabled(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldContain("'Endpoint protection (legacy)'");
    }

    [Fact]
    public void Asr_policy_not_assigned_fails()
    {
        var tenant = Recorded(items => items.Item("configuration-policies", Asr)["assignments"] = new JsonArray());

        OfficeChecks.AsrOfficeRulesEnabled(tenant, Options()).Outcome.ShouldBe(CheckOutcome.Fail);
    }

    private static void SetAsr(Dictionary<string, JsonArray> items, string rule, string mode)
    {
        var children = items.Item("configuration-policies", Asr)["settings"]![0]!["settingInstance"]!["groupSettingCollectionValue"]![0]!["children"]!.AsArray();
        var setting = children.Single(c => c!["settingDefinitionId"]!.GetValue<string>() == $"{AsrPrefix}_{rule}")!;
        setting["choiceSettingValue"]!["value"] = $"{AsrPrefix}_{rule}_{mode}";
    }
}
