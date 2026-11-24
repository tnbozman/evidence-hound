using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Reporting;
using static EvidenceHound.Core.Tests.Reporting.ReportingFixtures;

namespace EvidenceHound.Core.Tests.Reporting;

/// <summary>October and November snapshots of the fictional Example Pty Ltd, for the committee report.</summary>
internal static class CommitteeFixtures
{
    private static readonly string Ca = "https://ciso.example.com";

    public static PostureSnapshot October { get; } = Month(
        November.Previous(),
        e8: [("E8-1", ComplianceResult.NonCompliant), ("E8-2", ComplianceResult.NotAssessed), ("E8-3", ComplianceResult.Compliant)],
        ai6: ComplianceResult.PartiallyCompliant) with
    {
        Controls = [Control("AC-BACKUP", "Daily backups", "in_progress"), Control("AC-MFA", "MFA for all users", "in_progress")],
        Acceptances = [Acceptance(AcceptanceKind.SecurityException, "SE-PRINTER", "Old printer", "approved", new(2026, 1, 10), new(2027, 1, 10))],
        Remediation = [Remediation("FND-1", "Unsupported OS on two laptops", "Example IT lead", new(2026, 10, 20), "in_progress"), Remediation("FND-2", "Stale admin account", "Example IT lead", new(2026, 10, 25), "in_progress")],
        Due = [Due(DueKind.ManualEvidence, "Backups: restore test", "Example backup owner", new(2026, 10, 15), "pending")],
    };

    public static PostureSnapshot NovemberSnapshot { get; } = Month(
        November,
        e8: [("E8-1", ComplianceResult.Compliant), ("E8-2", ComplianceResult.NotAssessed), ("E8-3", ComplianceResult.NonCompliant)],
        ai6: ComplianceResult.PartiallyCompliant) with
    {
        Controls = [Control("AC-BACKUP", "Daily backups", "in_progress"), Control("AC-MFA", "MFA for all users", "active")],
        Acceptances =
        [
            Acceptance(AcceptanceKind.SecurityException, "SE-PRINTER", "Old printer", "approved", new(2026, 1, 10), new(2027, 1, 10)),
            Acceptance(AcceptanceKind.RiskAcceptance, "RA-KIOSK", "Legacy kiosk", "accepted", new(2026, 11, 3), new(2027, 5, 1)),
            Acceptance(AcceptanceKind.RiskAcceptance, "RA-FAX", "Fax gateway", "accepted", new(2025, 11, 1), new(2026, 11, 20)),
        ],
        Remediation =
        [
            Remediation("FND-1", "Unsupported OS on two laptops", "Example IT lead", new(2026, 10, 20), "in_progress"),
            Remediation("FND-3", "Backup job failing on file server", "Example backup owner", new(2026, 11, 10), "in_progress"),
            Remediation("FND-4", "Missing firewall log forwarding", "Example IT lead", new(2026, 12, 15), "to_do"),
        ],
        Incidents =
        [
            new IncidentItem("INC-7", "Phishing email clicked", "closed", "3", new(2026, 11, 5), new(2026, 11, 8)) { Link = $"{Ca}/incidents/in-7" },
            new IncidentItem("INC-8", "Lost laptop", "ongoing", "2", new(2026, 11, 25), null) { Link = $"{Ca}/incidents/in-8" },
        ],
        Collectors =
        [
            new CollectorHealth("example-m365", new DateTimeOffset(2026, 11, 30, 3, 0, 0, TimeSpan.Zero), "Succeeded", 18, 2, []),
            new CollectorHealth("example-sophos", new DateTimeOffset(2026, 11, 12, 3, 0, 0, TimeSpan.Zero), "Failed", 0, 0, ["Sophos Central refused the token (401)."]),
        ],
        Due =
        [
            Due(DueKind.ManualEvidence, "Backups: restore test", "Example backup owner", new(2026, 10, 15), "pending"),
            Due(DueKind.ManualEvidence, "Privileged access review", "Example IT lead", new(2026, 11, 28), "pending"),
            Due(DueKind.SupplierReview, "Example Cloud review 2026", "Example GRC lead", new(2026, 12, 10), "in_progress"),
            Due(DueKind.ManualEvidence, "Incident register", "Example GRC lead", new(2026, 11, 5), "completed"),
        ],
        Warnings = [new ScopeWarning("data.authority_expired", "scope/setup.yaml line 40: supplier SUP-EXMAIL's export control authority expired on 2026-11-01.")],
    };

    private static PostureSnapshot Month(ReportingPeriod period, (string RefId, ComplianceResult Result)[] e8, ComplianceResult ai6)
    {
        var essentialEight = E8(e8) with { Link = $"{Ca}/compliance-assessments/ca-e8" };
        var aiGovernance = Posture.Programme("ai6", "AI governance (AI6 Foundations)", Ai6, "EX-AI6-2026", ["F"], [Row(Ai6, "AI6-1.1", ai6)]) with
        {
            Link = $"{Ca}/compliance-assessments/ca-ai6",
            Register = new RegisterSnapshot("ai_systems", 2, [new("screening", "normal", 1), new("screening", "elevated", 1)]),
        };
        return Snapshot(period, [WithLinks(essentialEight), WithLinks(aiGovernance)]);
    }

    private static ProgrammePosture WithLinks(ProgrammePosture programme) =>
        programme with { Requirements = [.. programme.Requirements.Select(r => r with { Link = $"{Ca}/requirement-assessments/{r.RefId!.ToLowerInvariant()}" })] };

    private static ControlItem Control(string refId, string name, string status) =>
        new(refId, name, status, "Example IT lead", null) { Link = $"{Ca}/applied-controls/{refId.ToLowerInvariant()}" };

    private static AcceptanceItem Acceptance(AcceptanceKind kind, string refId, string name, string state, DateOnly accepted, DateOnly expires) =>
        new(kind, refId, name, state, "Example CTO", accepted, expires) { Link = $"{Ca}/acceptances/{refId.ToLowerInvariant()}" };

    private static RemediationItem Remediation(string refId, string name, string owner, DateOnly due, string status) =>
        new(refId, name, owner, due, status, null) { Link = $"{Ca}/findings/{refId.ToLowerInvariant()}" };

    private static DueItem Due(DueKind kind, string name, string owner, DateOnly due, string status) =>
        new(kind, name, owner, due, status) { Link = $"{Ca}/tasks/{name.Length}" };

}
