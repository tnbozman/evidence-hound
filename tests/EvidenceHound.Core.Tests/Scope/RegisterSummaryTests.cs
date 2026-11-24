using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Tests.Scope;

/// <summary>s11-14: a programme's register summarised for the committee: counts by field, added and retired in the period, and failing items.</summary>
public class RegisterSummaryTests
{
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 11, 30);
    private static readonly SourceLine At = new("scope/setup.yaml", 1);

    private static readonly RegisterItem[] Items =
    [
        Item("Example Chat", "low", "customised", added: new(2026, 2, 10)),
        Item("Example lead scoring", "medium", "third_party", added: new(2026, 11, 3)),
        Item("Example translator", "low", "third_party", added: new(2025, 6, 1), retired: new(2026, 11, 15)),
        Item("Example old bot", "high", "built", added: new(2024, 1, 1), retired: new(2025, 12, 31)),
    ];

    [Fact]
    public void Counts_by_field()
    {
        var summary = RegisterSummary.Of(Items, ["tier", "use"], From, To, new HashSet<string>(StringComparer.Ordinal));

        summary.Total.ShouldBe(2);
        summary.Counts.ShouldBe(
        [
            new RegisterCount("tier", "low", 1), new RegisterCount("tier", "medium", 1),
            new RegisterCount("use", "customised", 1), new RegisterCount("use", "third_party", 1),
        ]);
    }

    [Fact]
    public void Added_and_retired_in_period()
    {
        var summary = RegisterSummary.Of(Items, ["tier"], From, To, new HashSet<string>(StringComparer.Ordinal));

        summary.Added.ShouldBe(["Example lead scoring"]);
        summary.Retired.ShouldBe(["Example translator"]);
    }

    [Fact]
    public void Empty_register_gives_zero_counts_not_missing_section()
    {
        var summary = RegisterSummary.Of([], ["tier", "use"], From, To, new HashSet<string>(StringComparer.Ordinal));

        (summary.Total, summary.Counts.Count, summary.Added.Count, summary.Retired.Count, summary.Failing.Count).ShouldBe((0, 0, 0, 0, 0));
        summary.By.ShouldBe(["tier", "use"]);
    }

    [Fact]
    public void Failing_lists_the_items_failing_checks_name()
    {
        var summary = RegisterSummary.Of(Items, ["tier"], From, To, new HashSet<string>(["Example lead scoring", "Not in the register"], StringComparer.Ordinal));

        summary.Failing.ShouldBe(["Example lead scoring"]);
    }

    [Fact]
    public void Ai_systems_give_their_register_fields_and_dates()
    {
        var ai = new AiRegistration(AiRiskTier.Medium, new(2026, 3, 2), AiUse.ThirdParty, AiOversight.HumanOnTheLoop, "Monthly sample.", At)
        {
            InUseFrom = new(2026, 4, 1),
            RetiredOn = new(2026, 10, 31),
        };
        var plain = new ScopeSystem("Example laptops", null, SystemType.Supporting, null, null, [], new Dictionary<string, string>(), At);
        var setup = new ScopeSetup("Global", [], [new ScopeDomain("Example Pty Ltd", null, [], [plain, plain with { Name = "Example lead scoring", Type = SystemType.Primary, Ai = ai }], At)]);

        var item = RegisterItems.Of(setup, RegisterItems.AiSystems).ShouldHaveSingleItem();

        (item.Name, item.Added, item.Retired).ShouldBe(("Example lead scoring", new DateOnly(2026, 4, 1), new DateOnly(2026, 10, 31)));
        item.Values.ShouldBe(new Dictionary<string, string> { ["tier"] = "medium", ["use"] = "third_party", ["oversight"] = "human_on_the_loop" });
        RegisterItems.Of(setup, RegisterItems.Systems).Select(i => (i.Name, i.Values["type"])).ShouldBe([("Example laptops", "supporting"), ("Example lead scoring", "primary")]);
        RegisterItems.Fields(RegisterItems.AiSystems).ShouldBe(["tier", "use", "oversight"]);
        RegisterItems.Fields(RegisterItems.Systems).ShouldBe(["type"]);
    }

    private static RegisterItem Item(string name, string tier, string use, DateOnly added, DateOnly? retired = null) =>
        new(name, new Dictionary<string, string> { ["tier"] = tier, ["use"] = use }, added, retired);
}
