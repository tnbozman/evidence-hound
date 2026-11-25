using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s16-03: the measures a run computes for a metric instance that declares <c>source: hound</c>, as percentages.</summary>
public class MetricMeasureTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 20, 2, 0, 0, TimeSpan.Zero);

    private static CheckResult Result(string id, CheckOutcome outcome, DateTimeOffset? at = null) =>
        CheckResult.Create(CheckId.Create(id).Value(), outcome, "seen", [], at ?? Now, "example-identity").Value();

    [Fact]
    public void Check_pass_rate_over_pattern()
    {
        CheckResult[] latest =
        [
            Result("identity.mfa.admins", CheckOutcome.Pass),
            Result("identity.mfa.users", CheckOutcome.Fail),
            Result("identity.mfa.guests", CheckOutcome.NotApplicable),
            Result("identity.mfa.legacy", CheckOutcome.Partial),
            Result("identity.mfa_report", CheckOutcome.Fail),
            Result("identity.admin_accounts", CheckOutcome.Pass),
            Result("backup.daily", CheckOutcome.Fail),
        ];

        MetricMeasures.CheckPassRate(["identity.mfa.*"], latest).ShouldNotBeNull().ShouldBe(100.0 / 3, 1e-9);
        MetricMeasures.CheckPassRate(["identity.mfa.*", "identity.admin_accounts"], latest).ShouldBe(50);
        MetricMeasures.CheckPassRate(["identity.mfa.admins"], latest).ShouldBe(100);
        MetricMeasures.CheckPassRate(["identity.mfa.guests"], latest).ShouldBeNull();
        MetricMeasures.CheckPassRate(["email.*"], latest).ShouldBeNull();
    }

    [Fact]
    public void The_latest_result_of_each_check_counts_once()
    {
        CheckResult[] latest = [Result("identity.mfa.admins", CheckOutcome.Fail, Now.AddDays(-1)), Result("identity.mfa.admins", CheckOutcome.Pass)];

        MetricMeasures.CheckPassRate(["identity.mfa.*"], latest).ShouldBe(100);
    }

    [Fact]
    public void Requirements_compliant_pct_ignores_not_applicable()
    {
        ComplianceResult[] rows =
        [
            ComplianceResult.Compliant, ComplianceResult.Compliant, ComplianceResult.PartiallyCompliant, ComplianceResult.NonCompliant,
            ComplianceResult.NotAssessed, ComplianceResult.NotApplicable, ComplianceResult.NotApplicable,
        ];

        MetricMeasures.RequirementsCompliantPct(rows).ShouldBe(40);
        MetricMeasures.RequirementsCompliantPct([ComplianceResult.NotApplicable]).ShouldBeNull();
    }

    [Fact]
    public void Evidence_freshness_pct_is_the_share_collected_in_the_last_week()
    {
        CheckResult[] latest =
        [
            Result("identity.mfa.admins", CheckOutcome.Pass, Now.AddDays(-1)),
            Result("identity.mfa.users", CheckOutcome.Fail, Now.AddDays(-7)),
            Result("backup.daily", CheckOutcome.Pass, Now.AddDays(-8)),
            Result("backup.restore_tested", CheckOutcome.NotApplicable, Now.AddDays(-30)),
        ];

        MetricMeasures.EvidenceFreshnessPct([], latest, Now).ShouldBe(50);
        MetricMeasures.EvidenceFreshnessPct(["identity.*"], latest, Now).ShouldBe(100);
        MetricMeasures.EvidenceFreshnessPct([], [], Now).ShouldBeNull();
    }

    [Fact]
    public void Collector_success_rate_is_the_share_of_collectors_that_ran()
    {
        MetricMeasures.CollectorSuccessRate(succeeded: 3, ran: 4).ShouldBe(75);
        MetricMeasures.CollectorSuccessRate(succeeded: 0, ran: 0).ShouldBeNull();
    }
}
