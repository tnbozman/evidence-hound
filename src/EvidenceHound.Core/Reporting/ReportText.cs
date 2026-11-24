using System.Globalization;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Reporting;

/// <summary>Small text helpers shared by the committee and board reports.</summary>
internal static class ReportText
{
    public const string None = "None.";

    public static string Linked(string text, string? link) => link is null ? text : $"[{text}]({link})";

    public static string Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "not set";

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Result(ComplianceResult result) => result switch
    {
        ComplianceResult.Compliant => "compliant",
        ComplianceResult.PartiallyCompliant => "partially compliant",
        ComplianceResult.NonCompliant => "non-compliant",
        ComplianceResult.NotAssessed => "not assessed",
        _ => "not applicable",
    };

    /// <summary>How good a result is, for better or worse; null for not applicable.</summary>
    public static int? Rank(ComplianceResult? result) => result switch
    {
        ComplianceResult.Compliant => 3,
        ComplianceResult.PartiallyCompliant => 2,
        ComplianceResult.NonCompliant => 1,
        ComplianceResult.NotAssessed => 0,
        _ => null,
    };

    public static string Kind(AcceptanceKind kind) => kind == AcceptanceKind.RiskAcceptance ? "risk acceptance" : "security exception";

    public static string Cell(string? text) => string.IsNullOrWhiteSpace(text) ? "-" : text.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");

    public static string MonthName(ReportingPeriod period) => period.Start.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
}
