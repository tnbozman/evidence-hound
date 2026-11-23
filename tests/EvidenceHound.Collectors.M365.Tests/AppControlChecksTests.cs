using System.Text.Json.Nodes;
using EvidenceHound.Core.Checks;
using static EvidenceHound.Collectors.M365.Tests.Tenants;

namespace EvidenceHound.Collectors.M365.Tests;

public class AppControlChecksTests
{
    private static readonly string AppControl = "00000000-0000-4000-8000-00000000cp03";

    private static readonly string AppLocker = "00000000-0000-4000-8000-00000000dc02";

    private static readonly string Kiosks = "00000000-0000-4000-8000-0000000000f2";

    private static readonly string Laptops = "00000000-0000-4000-8000-0000000000f3";

    private static readonly string GroupPolicy = " Policies set by Group Policy are not seen.";

    [Fact]
    public void Enforced_policy_assigned_to_all_devices_passes()
    {
        var finding = AppControlChecks.Enforced(Recorded(), Options());

        finding.CheckId.ShouldBe("endpoint.application_control.enforced");
        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("Application control is enforced on all devices (App Control for Business 'App Control for Business - Windows and Store apps').");
        finding.Sources.ShouldBe(["configuration-policies", "device-configurations"]);
    }

    [Fact]
    public void Audit_mode_policy_is_partial()
    {
        var tenant = Recorded(items =>
        {
            WithoutAppLocker(items);
            Mode(items)["value"] = "device_vendor_msft_policy_config_applicationcontrol_built_in_controls_enable_app_control_1";
        });

        var finding = AppControlChecks.Enforced(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Partial);
        finding.Observation.ShouldBe("No application control policy is enforced. In audit mode only: App Control for Business 'App Control for Business - Windows and Store apps'." + GroupPolicy);
    }

    [Fact]
    public void Enforced_for_every_listed_group_passes()
    {
        var tenant = Recorded(WithoutAppControlForBusiness);

        var finding = AppControlChecks.Enforced(tenant, Options(s => s["application_control_groups"] = Kiosks));

        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe($"Application control is enforced for every group in application_control_groups ({Kiosks}) (AppLocker 'AppLocker - finance kiosks').");
    }

    [Fact]
    public void Enforced_for_some_listed_groups_is_partial()
    {
        var tenant = Recorded(WithoutAppControlForBusiness);

        var finding = AppControlChecks.Enforced(tenant, Options(s => s["application_control_groups"] = $"{Kiosks}, {Laptops}"));

        finding.Outcome.ShouldBe(CheckOutcome.Partial);
        finding.Observation.ShouldBe($"Application control is enforced for 1 of 2 groups in application_control_groups (AppLocker 'AppLocker - finance kiosks'), not for {Laptops}." + GroupPolicy);
    }

    [Fact]
    public void Enforced_only_for_a_group_without_listed_groups_is_partial()
    {
        var tenant = Recorded(WithoutAppControlForBusiness);

        var finding = AppControlChecks.Enforced(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Partial);
        finding.Observation.ShouldBe("Application control is enforced only for some groups, not all devices (AppLocker 'AppLocker - finance kiosks'). List the groups that must be covered in application_control_groups." + GroupPolicy);
    }

    [Fact]
    public void Encrypted_applocker_values_are_partial()
    {
        var tenant = Recorded(items =>
        {
            WithoutAppControlForBusiness(items);
            foreach (var setting in Oma(items))
            {
                setting!["isEncrypted"] = true;
                setting["value"] = "bm90IHJlYWRhYmxl";
            }
        });

        var finding = AppControlChecks.Enforced(tenant, Options(s => s["application_control_groups"] = Kiosks));

        finding.Outcome.ShouldBe(CheckOutcome.Partial);
        finding.Observation.ShouldBe("No application control policy is enforced. Mode unknown, because Graph returned the rule collections encrypted: AppLocker 'AppLocker - finance kiosks'." + GroupPolicy);
    }

    [Fact]
    public void No_policy_fails()
    {
        var tenant = Recorded(items =>
        {
            WithoutAppControlForBusiness(items);
            WithoutAppLocker(items);
        });

        var enforced = AppControlChecks.Enforced(tenant, Options());
        var covered = AppControlChecks.FileTypesCovered(tenant, Options());

        enforced.Outcome.ShouldBe(CheckOutcome.Fail);
        enforced.Observation.ShouldBe("No assigned App Control for Business policy or AppLocker profile was found." + GroupPolicy);
        covered.CheckId.ShouldBe("endpoint.application_control.file_types_covered");
        covered.Outcome.ShouldBe(CheckOutcome.Fail);
        covered.Observation.ShouldBe("No enforced application control covers executables, software libraries, scripts or installers." + GroupPolicy);
    }

    [Fact]
    public void Unassigned_policy_is_not_counted()
    {
        var tenant = Recorded(items =>
        {
            WithoutAppLocker(items);
            items.Item("configuration-policies", AppControl)["assignments"] = new JsonArray();
        });

        AppControlChecks.Enforced(tenant, Options()).Outcome.ShouldBe(CheckOutcome.Fail);
    }

    [Fact]
    public void App_control_for_business_covers_every_file_type()
    {
        var finding = AppControlChecks.FileTypesCovered(Recorded(), Options());

        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("Enforced application control covers executables, software libraries, scripts and installers (App Control for Business 'App Control for Business - Windows and Store apps', AppLocker 'AppLocker - finance kiosks').");
    }

    [Fact]
    public void Applocker_without_dll_rules_names_missing_type()
    {
        var tenant = Recorded(items =>
        {
            WithoutAppControlForBusiness(items);
            Oma(items).RemoveAt(1);
        });

        var finding = AppControlChecks.FileTypesCovered(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe("Enforced application control does not cover software libraries. It covers executables, scripts and installers (AppLocker 'AppLocker - finance kiosks')." + GroupPolicy);
    }

    [Fact]
    public void Applocker_collection_in_audit_mode_is_not_covered()
    {
        var tenant = Recorded(items =>
        {
            WithoutAppControlForBusiness(items);
            var script = Oma(items)[2]!;
            script["value"] = script["value"]!.GetValue<string>().Replace("EnforcementMode=\"Enabled\"", "EnforcementMode=\"AuditOnly\"", StringComparison.Ordinal);
        });

        var finding = AppControlChecks.FileTypesCovered(tenant, Options());

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe("Enforced application control does not cover scripts. It covers executables, software libraries and installers (AppLocker 'AppLocker - finance kiosks')." + GroupPolicy);
    }

    private static void WithoutAppControlForBusiness(Dictionary<string, JsonArray> items) =>
        items["configuration-policies"].Remove(items.Item("configuration-policies", AppControl));

    private static void WithoutAppLocker(Dictionary<string, JsonArray> items) =>
        items["device-configurations"].Remove(items.Item("device-configurations", AppLocker));

    private static JsonArray Oma(Dictionary<string, JsonArray> items) => items.Item("device-configurations", AppLocker)["omaSettings"]!.AsArray();

    private static JsonObject Mode(Dictionary<string, JsonArray> items) =>
        items.Item("configuration-policies", AppControl)["settings"]![0]!["settingInstance"]!["choiceSettingValue"]!["children"]![0]!["choiceSettingValue"]!.AsObject();
}
