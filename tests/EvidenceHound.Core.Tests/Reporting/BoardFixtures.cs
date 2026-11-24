using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Reporting;
using static EvidenceHound.Core.Tests.Reporting.ReportingFixtures;

namespace EvidenceHound.Core.Tests.Reporting;

/// <summary>Quarter-end snapshots of the fictional Example Pty Ltd for 2025-Q4 to 2026-Q4, for the board report.</summary>
internal static class BoardFixtures
{
    private static readonly string Ca = "https://ciso.example.com";

    public static readonly ReportingPeriod Q4 = new(PeriodKind.Quarter, 2026, 4);

    /// <summary>Oldest first: 2025-12, 2026-03, 2026-06, 2026-09, 2026-12.</summary>
    public static IReadOnlyList<PostureSnapshot> QuarterEnds { get; } =
    [
        QuarterEnd(2025, 12, ComplianceResult.NonCompliant, ComplianceResult.NonCompliant, ComplianceResult.NonCompliant),
        QuarterEnd(2026, 3, ComplianceResult.NonCompliant, ComplianceResult.NotAssessed, ComplianceResult.Compliant),
        QuarterEnd(2026, 6, ComplianceResult.PartiallyCompliant, ComplianceResult.NotAssessed, ComplianceResult.Compliant),
        QuarterEnd(2026, 9, ComplianceResult.Compliant, ComplianceResult.PartiallyCompliant, ComplianceResult.Compliant),
        QuarterEnd(2026, 12, ComplianceResult.Compliant, ComplianceResult.NonCompliant, ComplianceResult.Compliant) with
        {
            Acceptances =
            [
                new AcceptanceItem(AcceptanceKind.RiskAcceptance, "RA-KIOSK", "Legacy kiosk", "accepted", "Example CTO", new(2026, 11, 3), new(2027, 5, 1)) { Link = $"{Ca}/risk-acceptances/ra-1" },
                new AcceptanceItem(AcceptanceKind.SecurityException, "SE-CAD", "CAD workstation without application control", "approved", "Example CTO", new(2026, 10, 14), new(2028, 4, 1)) { Link = $"{Ca}/security-exceptions/se-1" },
                new AcceptanceItem(AcceptanceKind.SecurityException, "SE-PRINTER", "Old printer", "approved", "Example CTO", new(2026, 1, 10), new(2027, 1, 10)) { Link = $"{Ca}/security-exceptions/se-2" },
            ],
            Risks =
            [
                Risk("R-1", "Ransomware on file server", "High", "Medium", 2, "Example CTO", "mitigate", new(2027, 3, 31)),
                Risk("R-2", "Supplier breach exposes drawings", "High", "High", 3, "Example CTO", "transfer", new(2027, 6, 30)),
                Risk("R-3", "Phishing leads to account takeover", "Medium", "Low", 1, "Example IT lead", "mitigate", new(2026, 12, 31)),
                Risk("R-4", "Laptop theft", "Medium", "Low", 1, "Example IT lead", "mitigate", null),
                Risk("R-5", "Insider misuse", "Low", "Low", 1, "Example CTO", "accept", null),
                Risk("R-6", "Cloud outage", "Low", "Very low", 0, "Example IT lead", "accept", null),
            ],
            Collectors =
            [
                new CollectorHealth("example-m365", new DateTimeOffset(2026, 12, 31, 3, 0, 0, TimeSpan.Zero), "Succeeded", 18, 2, []),
                new CollectorHealth("example-sophos", new DateTimeOffset(2026, 12, 1, 3, 0, 0, TimeSpan.Zero), "Failed", 0, 0, ["Sophos Central refused the token (401)."]),
            ],
            Due =
            [
                new DueItem(DueKind.SupplierReview, "Example Cloud review 2026", "Example GRC lead", new(2026, 12, 10), "completed"),
                new DueItem(DueKind.SupplierReview, "Example Mail review 2026", "Example GRC lead", new(2026, 11, 30), "in_progress"),
                new DueItem(DueKind.ManualEvidence, "Backups: restore test", "Example backup owner", new(2026, 10, 15), "pending"),
            ],
        },
    ];

    private static PostureSnapshot QuarterEnd(int year, int month, ComplianceResult e81, ComplianceResult e82, ComplianceResult e83)
    {
        var e8 = E8(("E8-1", e81), ("E8-2", e82), ("E8-3", e83)) with
        {
            Link = $"{Ca}/compliance-assessments/ca-e8",
            Coverage = [new CoverageCount(Mfa, "2", 1, 1, 0), new CoverageCount(Backups, "2", 0, 1, 0)],
        };
        var ai6 = Posture.Programme("ai6", "AI governance (AI6 Foundations)", Ai6, "EX-AI6-2026", ["F"], [Row(Ai6, "AI6-1.1", ComplianceResult.Compliant)]) with
        {
            Link = $"{Ca}/compliance-assessments/ca-ai6",
            Register = new RegisterSnapshot("ai_systems", 2, [new("screening", "normal", 1), new("screening", "elevated", 1)]),
        };
        return Snapshot(new ReportingPeriod(PeriodKind.Month, year, month), [e8, ai6]);
    }

    private static RiskItem Risk(string refId, string name, string current, string residual, int rank, string owner, string treatment, DateOnly? target) =>
        new(refId, name, current, residual, rank, owner, treatment, target) { Link = $"{Ca}/risk-scenarios/{refId.ToLowerInvariant()}" };
}
