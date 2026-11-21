using EvidenceHound.Core.Checks;

namespace EvidenceHound.Collectors.Manual.Tests;

public class AttestationTests
{
    [Fact]
    public void Round_trips_through_yaml()
    {
        var attestation = new Attestation(
            "Alex Example",
            new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero),
            new Dictionary<string, CheckOutcome> { [DropFolder.Reviewed] = CheckOutcome.Pass, [DropFolder.Exported] = CheckOutcome.NotApplicable },
            "Reviewed: rules 1-40.");

        var parsed = Attestation.Parse(attestation.ToYaml(), "example.attest.yaml").Value();

        parsed.Who.ShouldBe(attestation.Who);
        parsed.Date.ShouldBe(attestation.Date);
        parsed.Outcomes.ShouldBe(attestation.Outcomes, ignoreOrder: true);
        parsed.Note.ShouldBe(attestation.Note);
    }

    [Fact]
    public void Date_without_a_time_is_midnight_utc_and_the_note_is_optional()
    {
        var parsed = Attestation.Parse("who: Alex Example\ndate: 2026-11-20\noutcomes: {network.firewall.rules_reviewed: partial}\n", "example.attest.yaml").Value();

        parsed.Date.ShouldBe(new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero));
        parsed.Note.ShouldBeNull();
        parsed.Outcomes[DropFolder.Reviewed].ShouldBe(CheckOutcome.Partial);
    }

    [Fact]
    public void Missing_who_and_date_and_unknown_outcomes_are_each_reported()
    {
        var problems = Attestation.Parse("outcomes: {network.firewall.rules_reviewed: maybe}\n", "example.attest.yaml").Problems();

        problems.ShouldAllBe(p => p.Code == "manual.attestation_invalid" && p.Message.StartsWith("example.attest.yaml: ", StringComparison.Ordinal));
        problems.Count.ShouldBe(3);
        problems.ShouldContain(p => p.Message.Contains("'who'", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Message.Contains("'date'", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Message.Contains("maybe", StringComparison.Ordinal));
    }

    [Fact]
    public void Text_that_is_not_yaml_is_a_problem() =>
        Attestation.Parse("who: [unclosed", "example.attest.yaml").Problems().ShouldHaveSingleItem().Code.ShouldBe("manual.attestation_invalid");
}
