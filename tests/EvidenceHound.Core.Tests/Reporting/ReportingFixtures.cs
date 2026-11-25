using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Reporting;

namespace EvidenceHound.Core.Tests.Reporting;

/// <summary>Small fictional frameworks and snapshots for the reporting tests.</summary>
internal static class ReportingFixtures
{
    public const string Mfa = "urn:example:node:e8:mfa";
    public const string Backups = "urn:example:node:e8:rb";
    public const string Govern = "urn:example:node:ai6:1";

    public static readonly ReportingPeriod November = new(PeriodKind.Month, 2026, 11);
    public static readonly ReportingPeriod December = new(PeriodKind.Month, 2026, 12);

    public static FrameworkSnapshot EssentialEight { get; } = new(
        "urn:example:library:e8",
        "urn:example:framework:e8",
        3,
        [new("1", "Maturity Level One"), new("2", "Maturity Level Two"), new("3", "Maturity Level Three")],
        [
            Node("E8-1", Mfa, "1", "2", "3"),
            Node("E8-2", Mfa, "2", "3"),
            Node("E8-3", Backups, "1", "2", "3"),
            Node("E8-4", Backups, "3"),
        ])
    {
        // Only one parent is named, so a snapshot made before parent names (s13-08) still reports by the URN's last part.
        Parents = [new SnapshotParent(Mfa, "MFA", "Multi-factor authentication")],
    };

    public static FrameworkSnapshot Ai6 { get; } = new(
        "urn:example:library:ai6",
        "urn:example:framework:ai6",
        1,
        [new("F", "Foundations"), new("I", "Implementation")],
        [Node("AI6-1.1", Govern, "F", "I"), Node("AI6-1.2", Govern, "I")]);

    public static CurrentRow Row(FrameworkSnapshot framework, string refId, ComplianceResult result)
    {
        var node = framework.Nodes.Single(n => n.RefId == refId);
        return new CurrentRow("id-" + refId, node.Urn, refId, node.Groups, result, "", []);
    }

    public static PostureSnapshot Snapshot(ReportingPeriod period, IReadOnlyList<ProgrammePosture> programmes, params AcceptanceItem[] acceptances) =>
        new(period, new DateTimeOffset(period.End.ToDateTime(new TimeOnly(18, 0)), TimeSpan.Zero), programmes, [], acceptances, [], [], [], [], []);

    public static ProgrammePosture E8(params (string RefId, ComplianceResult Result)[] rows) =>
        Posture.Programme("essential-eight", "Essential Eight", EssentialEight, "EX-E8-2026", ["1", "2"], [.. rows.Select(r => Row(EssentialEight, r.RefId, r.Result))]);

    private static SnapshotNode Node(string refId, string parent, params string[] groups) =>
        new($"{parent}:{refId.ToLowerInvariant()}", refId, parent, groups, refId);
}
