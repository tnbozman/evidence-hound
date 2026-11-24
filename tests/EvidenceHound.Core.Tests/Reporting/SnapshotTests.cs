using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Reporting;
using EvidenceHound.Core.Tests.Checks;
using static EvidenceHound.Core.Tests.Reporting.ReportingFixtures;

namespace EvidenceHound.Core.Tests.Reporting;

/// <summary>s12-04: a period's posture per programme, counted by the programme's own groups and parent nodes.</summary>
public class SnapshotTests
{
    [Fact]
    public void Counts_use_programme_groups_not_maturity_levels()
    {
        var ai6 = Posture.Programme("ai6", "AI6", Ai6, "EX-AI6-2026", ["F"], [Row(Ai6, "AI6-1.1", ComplianceResult.Compliant), Row(Ai6, "AI6-1.2", ComplianceResult.NonCompliant)]);

        ai6.Requirements.Select(r => r.RefId).ShouldBe(["AI6-1.1"]);
        ai6.Counts.ShouldBe(
        [
            new ResultCount("F", null, 1, 0, 0, 0, 0),
            new ResultCount("F", Govern, 1, 0, 0, 0, 0),
        ]);
        ai6.Groups.Select(g => g.Name).ShouldBe(["Foundations"]);
    }

    [Fact]
    public void Two_programmes_are_counted_separately()
    {
        var e8 = E8(("E8-1", ComplianceResult.Compliant), ("E8-2", ComplianceResult.PartiallyCompliant), ("E8-3", ComplianceResult.NotAssessed), ("E8-4", ComplianceResult.NonCompliant));
        var ai6 = Posture.Programme("ai6", "AI6", Ai6, "EX-AI6-2026", ["F"], [Row(Ai6, "AI6-1.1", ComplianceResult.NonCompliant)]);

        e8.Counts.ShouldBe(
        [
            new ResultCount("1", null, 1, 0, 0, 1, 0),
            new ResultCount("1", Mfa, 1, 0, 0, 0, 0),
            new ResultCount("1", Backups, 0, 0, 0, 1, 0),
            new ResultCount("2", null, 1, 1, 0, 1, 0),
            new ResultCount("2", Mfa, 1, 1, 0, 0, 0),
            new ResultCount("2", Backups, 0, 0, 0, 1, 0),
        ]);
        ai6.Counts[0].ShouldBe(new ResultCount("F", null, 0, 0, 1, 0, 0));
        e8.Parents.ShouldBe([new ParentNode(Mfa, "mfa"), new ParentNode(Backups, "rb")]);
    }

    [Fact]
    public void Rows_carry_their_parent_from_the_framework_snapshot()
    {
        var e8 = E8(("E8-3", ComplianceResult.Compliant));

        e8.Requirements.Single().ParentUrn.ShouldBe(Backups);
        e8.FrameworkVersion.ShouldBe(3);
        e8.Audit.ShouldBe("EX-E8-2026");
    }

    [Fact]
    public void Snapshot_json_round_trips()
    {
        var snapshot = Snapshot(November, [E8(("E8-1", ComplianceResult.Compliant))], new AcceptanceItem(AcceptanceKind.RiskAcceptance, "RA-1", "Legacy kiosk", "accepted", "Example CTO", new DateOnly(2026, 11, 3), new DateOnly(2027, 5, 1)));

        var json = PostureSnapshotJson.Write(snapshot);
        var read = PostureSnapshotJson.Read(json, "snapshot.json").Value();

        PostureSnapshotJson.Write(read).ShouldBe(json);
        json.ShouldContain("\"period\": \"2026-11\"");
        json.ShouldContain("\"kind\": \"risk_acceptance\"");
        json.ShouldContain("\"result\": \"compliant\"");
    }

    [Theory]
    [InlineData("2026-11", "2026-11-01", "2026-11-30", "2026-10")]
    [InlineData("2026-01", "2026-01-01", "2026-01-31", "2025-12")]
    [InlineData("2026-Q4", "2026-10-01", "2026-12-31", "2026-Q3")]
    [InlineData("2026-Q1", "2026-01-01", "2026-03-31", "2025-Q4")]
    public void Periods_parse_with_their_dates_and_previous_period(string text, string start, string end, string previous)
    {
        var period = ReportingPeriod.Parse(text).Value();

        period.ToString().ShouldBe(text);
        period.Start.ShouldBe(DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture));
        period.End.ShouldBe(DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture));
        period.Previous().ToString().ShouldBe(previous);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026-Q5")]
    [InlineData("November")]
    public void Bad_periods_are_refused(string text)
    {
        ReportingPeriod.Parse(text).Match(_ => "", ps => ps[0].Code).ShouldBe("report.period_invalid");
    }
}
