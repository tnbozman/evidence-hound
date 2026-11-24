using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Reporting;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using static EvidenceHound.Core.Tests.Reporting.ReportingFixtures;

namespace EvidenceHound.Core.Tests.Reporting;

/// <summary>s12-04: reports compare two snapshots with a pure diff.</summary>
public class DiffTests
{
    private static readonly AcceptanceItem Kiosk = new(AcceptanceKind.RiskAcceptance, "RA-1", "Legacy kiosk", "accepted", "Example CTO", new DateOnly(2026, 6, 1), new DateOnly(2026, 12, 15));
    private static readonly AcceptanceItem Macro = new(AcceptanceKind.SecurityException, "SE-1", "Finance macros", "approved", "Example CTO", new DateOnly(2026, 12, 2), new DateOnly(2027, 6, 1));
    private static readonly AcceptanceItem Printer = new(AcceptanceKind.SecurityException, "SE-2", "Old printer", "approved", "Example CTO", new DateOnly(2026, 1, 10), new DateOnly(2027, 1, 10));

    [Fact]
    public void Detects_requirement_result_changes_between_periods()
    {
        var before = Snapshot(November, [E8(("E8-1", ComplianceResult.NonCompliant), ("E8-3", ComplianceResult.Compliant))]);
        var after = Snapshot(December, [E8(("E8-1", ComplianceResult.Compliant), ("E8-2", ComplianceResult.NotAssessed), ("E8-3", ComplianceResult.Compliant))]);

        var diff = Snapshots.Diff(before, after);

        diff.Requirements.ShouldBe(
        [
            new RequirementChange("essential-eight", $"{Mfa}:e8-1", "E8-1", Mfa, ComplianceResult.NonCompliant, ComplianceResult.Compliant),
            new RequirementChange("essential-eight", $"{Mfa}:e8-2", "E8-2", Mfa, null, ComplianceResult.NotAssessed),
        ]);
    }

    [Fact]
    public void Detects_new_expired_and_closed_exceptions()
    {
        var before = Snapshot(November, [], Kiosk, Printer);
        var after = Snapshot(December, [], Kiosk, Macro, Printer with { State = "resolved" });

        var diff = Snapshots.Diff(before, after);

        diff.NewAcceptances.ShouldBe([Macro]);
        diff.ExpiredAcceptances.ShouldBe([Kiosk]);
        diff.ClosedAcceptances.Select(a => a.RefId).ShouldBe(["SE-2"]);
    }

    [Fact]
    public void An_exception_gone_from_the_later_snapshot_is_closed()
    {
        var diff = Snapshots.Diff(Snapshot(November, [], Printer), Snapshot(December, []));

        diff.ClosedAcceptances.ShouldBe([Printer]);
    }

    [Fact]
    public void Detects_control_status_changes()
    {
        var before = Snapshot(November, []) with { Controls = [new ControlItem("AC-1", "Backups", "in_progress", "Example owner", null)] };
        var after = Snapshot(December, []) with { Controls = [new ControlItem("AC-1", "Backups", "active", "Example owner", null), new ControlItem("AC-2", "MFA", "to_do", null, null)] };

        Snapshots.Diff(before, after).Controls.ShouldBe([new ControlChange("AC-1", "Backups", "in_progress", "active"), new ControlChange("AC-2", "MFA", null, "to_do")]);
    }

    [Property]
    public Property Diff_of_a_snapshot_with_itself_is_empty()
    {
        var results = Gen.Elements(Enum.GetValues<ComplianceResult>()).ArrayOf(4).ToArbitrary();
        var states = Gen.Elements("accepted", "approved", "revoked", "resolved", "expired").ArrayOf(3).ToArbitrary();
        return Prop.ForAll(results, states, (r, s) =>
        {
            var snapshot = Snapshot(
                November,
                [E8(("E8-1", r[0]), ("E8-2", r[1]), ("E8-3", r[2]), ("E8-4", r[3]))],
                Kiosk with { State = s[0] },
                Macro with { State = s[1] },
                Printer with { State = s[2] });
            return Snapshots.Diff(snapshot, snapshot).IsEmpty;
        });
    }
}
