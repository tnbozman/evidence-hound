using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Review;

namespace EvidenceHound.Core.Tests.Review;

/// <summary>
/// s10-07: a person may approve a rule instead of each instance: a manual source whose new export has the same checks and the same
/// results as its last attestation is re-attested without review, for a year. Nothing else is ever covered.
/// </summary>
public class StandingApprovalTests
{
    private static readonly DateOnly Today = new(2026, 11, 24);

    private static readonly StandingApproval Rule = new(StandingApprovals.UnchangedReattestation, "example-firewall-config", "Sam Example", new DateOnly(2026, 6, 1), new DateOnly(2027, 6, 1));

    private static readonly Dictionary<string, CheckOutcome> Last = new() { ["network.firewall.rules_reviewed"] = CheckOutcome.Pass, ["network.firewall.config_exported"] = CheckOutcome.Pass };

    [Fact]
    public void Unchanged_reattestation_is_accepted_and_nothing_else_is()
    {
        StandingApprovals.Covers(Rule, ReviewKind.ManualEvidence, "example-firewall-config", new Dictionary<string, CheckOutcome>(Last), Last, Today).ShouldBeTrue();

        // A changed result, a changed set of checks, a first attestation, another source, an expired rule, or any other kind of item.
        StandingApprovals.Covers(Rule, ReviewKind.ManualEvidence, "example-firewall-config", new Dictionary<string, CheckOutcome>(Last) { ["network.firewall.rules_reviewed"] = CheckOutcome.Fail }, Last, Today).ShouldBeFalse();
        StandingApprovals.Covers(Rule, ReviewKind.ManualEvidence, "example-firewall-config", new Dictionary<string, CheckOutcome> { ["network.firewall.rules_reviewed"] = CheckOutcome.Pass }, Last, Today).ShouldBeFalse();
        StandingApprovals.Covers(Rule, ReviewKind.ManualEvidence, "example-firewall-config", Last, null, Today).ShouldBeFalse();
        StandingApprovals.Covers(Rule, ReviewKind.ManualEvidence, "example-backup-restore", Last, Last, Today).ShouldBeFalse();
        StandingApprovals.Covers(Rule, ReviewKind.ManualEvidence, "example-firewall-config", Last, Last, new DateOnly(2027, 6, 2)).ShouldBeFalse();
        Enum.GetValues<ReviewKind>().Where(k => k != ReviewKind.ManualEvidence)
            .ShouldAllBe(k => !StandingApprovals.Covers(Rule, k, "example-firewall-config", Last, Last, Today));
        StandingApprovals.Covers(Rule with { Rule = "anything_else" }, ReviewKind.ManualEvidence, "example-firewall-config", Last, Last, Today).ShouldBeFalse();
    }

    [Fact]
    public void A_rule_lasts_twelve_months_from_the_day_it_is_signed()
    {
        StandingApprovals.Unchanged("example-firewall-config", "Sam Example", Today).Expires.ShouldBe(new DateOnly(2027, 11, 24));
    }
}
