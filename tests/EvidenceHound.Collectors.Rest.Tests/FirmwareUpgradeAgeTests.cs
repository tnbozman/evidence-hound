using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Rest.Engine;
using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.Rest.Tests;

/// <summary>
/// <c>sophos.firmware_upgrade_age</c>: how long each firewall has had a firmware upgrade offered, counted from the first earlier run
/// that saw the offer, because Sophos Central gives no release date.
/// </summary>
public sealed class FirmwareUpgradeAgeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 2, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, string> Window = new(StringComparer.Ordinal) { ["days"] = "14" };

    private static readonly JsonArray Firewalls = Rows("""[{ "id": "fw-1" }, { "id": "fw-2" }]""");

    [Fact]
    public void No_upgrade_offered_passes()
    {
        var finding = Check(Offers(("fw-1", []), ("fw-2", [])));

        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("No firewall has a firmware upgrade outstanding (2 firewalls).");
    }

    [Fact]
    public void Upgrade_offered_within_the_window_passes()
    {
        var finding = Check(Offers(("fw-1", ["21.5.0 GA-Build171"]), ("fw-2", [])), Earlier(10, ("fw-1", ["21.5.0 GA-Build171"])));

        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("1 of 2 firewalls has a firmware upgrade offered, none for more than 14 days (the longest 10 days). Ages count from the first run that saw an offer; the earliest run kept is 2026-11-12.");
    }

    [Fact]
    public void Upgrade_offered_for_longer_than_the_window_fails()
    {
        var finding = Check(
            Offers(("fw-1", ["21.5.0 GA-Build171"]), ("fw-2", ["21.5.0 GA-Build171"])),
            Earlier(20, ("fw-1", ["21.5.0 GA-Build171"]), ("fw-2", [])),
            Earlier(5, ("fw-1", ["21.5.0 GA-Build171"]), ("fw-2", ["21.5.0 GA-Build171"])));

        finding.Outcome.ShouldBe(CheckOutcome.Fail);
        finding.Observation.ShouldBe("1 of 2 firewalls has had a firmware upgrade outstanding for more than 14 days (the longest 20 days); 1 more has one offered within the window. Ages count from the first run that saw an offer; the earliest run kept is 2026-11-02.");
    }

    [Fact]
    public void An_offer_that_lapsed_starts_counting_again()
    {
        var finding = Check(
            Offers(("fw-1", ["21.5.1 MR-1"]), ("fw-2", [])),
            Earlier(30, ("fw-1", ["21.5.0 GA-Build171"])),
            Earlier(12, ("fw-1", [])),
            Earlier(3, ("fw-1", ["21.5.1 MR-1"])));

        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldContain("(the longest 3 days)");
    }

    [Fact]
    public void A_first_run_counts_the_offer_from_today_and_says_so()
    {
        var finding = Check(Offers(("fw-1", ["21.5.0 GA-Build171"]), ("fw-2", [])));

        finding.Outcome.ShouldBe(CheckOutcome.Pass);
        finding.Observation.ShouldBe("1 of 2 firewalls has a firmware upgrade offered, none for more than 14 days (the longest 0 days). Ages count from the first run that saw an offer; this is the first run, so counting starts today.");
    }

    [Fact]
    public void No_firewalls_is_not_applicable()
    {
        var finding = CodeChecks.FirmwareUpgradeAge(new CodeCheckInput([[], []], Window, Now));

        finding.Outcome.ShouldBe(CheckOutcome.NotApplicable);
        finding.Observation.ShouldBe("Sophos Central manages no firewalls.");
    }

    [Fact]
    public void A_firewall_missing_from_the_upgrade_check_cannot_be_assessed()
    {
        var finding = Check(Offers(("fw-1", [])));

        finding.Outcome.ShouldBe(CheckOutcome.Error);
        finding.Observation.ShouldBe("Unable to assess: the firmware upgrade check gave no answer for 1 of 2 firewalls.");
    }

    [Fact]
    public void Wrong_inputs_cannot_be_assessed()
    {
        CodeChecks.FirmwareUpgradeAge(new CodeCheckInput([Firewalls], Window, Now)).Outcome.ShouldBe(CheckOutcome.Error);
        CodeChecks.FirmwareUpgradeAge(new CodeCheckInput([Firewalls, Offers()], new Dictionary<string, string>(), Now)).Outcome.ShouldBe(CheckOutcome.Error);
    }

    private static Finding Check(JsonArray offers, params EarlierRows[] earlier) =>
        CodeChecks.FirmwareUpgradeAge(new CodeCheckInput([Firewalls, offers], Window, Now) { Earlier = earlier });

    private static EarlierRows Earlier(int daysAgo, params (string Id, string[] Versions)[] offers) => new(Now.AddDays(-daysAgo), Offers(offers));

    private static JsonArray Offers(params (string Id, string[] Versions)[] offers) =>
        [.. offers.Select(o => (JsonNode)new JsonObject { ["id"] = o.Id, ["upgradeToVersion"] = new JsonArray([.. o.Versions.Select(v => (JsonNode)JsonValue.Create(v))]) })];

    private static JsonArray Rows(string json) => JsonNode.Parse(json)!.AsArray();
}

/// <summary>A code check's <c>history</c>: the query whose earlier evidence it is given.</summary>
public sealed class CheckHistoryTests
{
    [Fact]
    public void History_names_a_query_the_check_is_over()
    {
        var profile = ProfileParser.Parse(Yaml("history: upgrades"), "history.yaml").Value();

        profile.Checks[0].History.ShouldBe("upgrades");
        ProfileValidator.Validate(profile, RestCollector.Validation).ShouldBeEmpty();
    }

    [Fact]
    public void History_of_a_query_the_check_is_not_over_is_refused()
    {
        Codes(Yaml("history: firmware")).ShouldContain("profile.history_invalid");
    }

    [Fact]
    public void History_on_a_declarative_check_is_refused()
    {
        var yaml = """
            profile: history
            version: "1.0.0"
            connection:
              base_url: https://api.example.com/
              allowed_hosts: [api.example.com]
              auth: { type: none }
            queries:
              devices:
                get: v1/devices
            checks:
              - id: example.devices_named
                over: devices
                history: devices
                each: { field: $.name, exists: true }
                when_empty: fail
            """;

        Codes(yaml).ShouldContain("profile.history_invalid");
    }

    private static IReadOnlyList<string> Codes(string yaml) =>
        [.. ProfileValidator.Validate(ProfileParser.Parse(yaml, "history.yaml").Value(), RestCollector.Validation).Select(p => p.Code)];

    private static string Yaml(string history) => $$"""
        profile: history
        version: "1.0.0"
        connection:
          base_url: https://api.example.com/
          allowed_hosts: [api.example.com]
          auth: { type: none }
        queries:
          devices:
            get: v1/devices
          upgrades:
            get: v1/upgrades
        checks:
          - id: example.firmware_current
            over: [devices, upgrades]
            {{history}}
            code: sophos.firmware_upgrade_age
            with: { days: "14" }
        """;
}
