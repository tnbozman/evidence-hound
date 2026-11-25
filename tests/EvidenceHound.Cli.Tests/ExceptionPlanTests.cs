using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Cli.Tests;

/// <summary>s15-03: the plan shows each security exception and risk acceptance with its CISO Assistant status, and what needs renewing.</summary>
public sealed class ExceptionPlanTests
{
    private static readonly ScopePlan Empty = new([], []);

    private static readonly PlanNotes Notes = PlanNotes.None with
    {
        Exceptions =
        [
            new ExceptionNote("exception.status", "security exception", "EXC-01", "Legacy finance app without MFA", "approved", "2027-01-20", "approved, expires 2027-01-20"),
            new ExceptionNote("exception.expiring", "security exception", "EXC-01", "Legacy finance app without MFA", "approved", "2027-01-20", "expires in 56 days: renew it."),
            new ExceptionNote("exception.status", "risk acceptance", "EX-RISK-2026/R-02", "Accept: backup administrator leaves", "submitted", null, "submitted, no expiry"),
        ],
    };

    [Fact]
    public void Markdown_lists_each_exception_with_its_status_then_what_needs_renewing()
    {
        var markdown = ScopePlanOutput.Markdown(Empty, Notes);

        markdown.ShouldContain("### Security exceptions and risk acceptances");
        markdown.ShouldContain("| security exception | EXC-01 | Legacy finance app without MFA | approved | 2027-01-20 |");
        markdown.ShouldContain("| risk acceptance | EX-RISK-2026/R-02 | Accept: backup administrator leaves | submitted |  |");
        markdown.ShouldContain("- EXC-01: expires in 56 days: renew it. [exception.expiring]");
    }

    [Fact]
    public void Json_lists_each_note()
    {
        var notes = JsonNode.Parse(ScopePlanOutput.Json(Empty, Notes))!["exceptions"]!.AsArray();

        notes.Select(n => (n!["code"]!.GetValue<string>(), n["ref_id"]!.GetValue<string>(), n["status"]!.GetValue<string>())).ShouldBe([
            ("exception.status", "EXC-01", "approved"), ("exception.expiring", "EXC-01", "approved"), ("exception.status", "EX-RISK-2026/R-02", "submitted"),
        ]);
    }

    [Fact]
    public void Kinds_are_named_in_snake_case()
    {
        (ScopePlanOutput.Kind(ScopeKind.SecurityException), ScopePlanOutput.Kind(ScopeKind.RiskAcceptance)).ShouldBe(("security_exception", "risk_acceptance"));
        (ScopePlanOutput.Kind(ScopeKind.AssetAssessment), ScopePlanOutput.Kind(ScopeKind.EscalationThreshold)).ShouldBe(("asset_assessment", "escalation_threshold"));
    }

    [Fact]
    public void Without_exceptions_there_is_no_section()
    {
        ScopePlanOutput.Markdown(Empty, PlanNotes.None).ShouldNotContain("Security exceptions");
    }
}
