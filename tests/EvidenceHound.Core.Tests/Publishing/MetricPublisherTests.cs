using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Tests.Checks;

namespace EvidenceHound.Core.Tests.Publishing;

/// <summary>s16-03: after collecting, a run measures each metric instance with <c>source: hound</c> and records the current period's value.</summary>
public class MetricPublisherTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 20, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset June = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly SourceLine Line = new("scope/metrics.yaml", 1);

    private static ScopeMetricInstance Instance(string refId, string measure, IReadOnlyList<string>? checks = null, string? audit = null) =>
        new(refId, refId, null, new ScopeRef("EX-MET-PASS", Line), new ScopeRef("Example Pty Ltd", Line), "monthly", null, null, [], [], Line)
        {
            Source = new MetricSource(measure, checks ?? [], audit is null ? null : new ScopeRef(audit, Line), Line),
        };

    private static CheckResult Result(string id, CheckOutcome outcome) =>
        CheckResult.Create(CheckId.Create(id).Value(), outcome, "seen", [], Now.AddHours(-1), "example-identity").Value();

    private static RunMeasures Run(params CheckResult[] latest) => new(latest, CollectorsRan: 2, CollectorsSucceeded: 2, Now);

    [Fact]
    public async Task A_run_records_each_changed_measure_for_its_period()
    {
        var ca = new InMemoryCisoAssistant();
        ca.Metrics["EX-MET-PASS-CORP"] = new("mi-pass", "monthly");
        ca.Metrics["EX-MET-RUNS-CORP"] = new("mi-runs", "monthly");
        ca.Samples["mi-pass"] = [new HeldSample(June, 50)];
        ca.Samples["mi-runs"] = [new HeldSample(June, 100)];
        ScopeMetricInstance[] instances = [Instance("EX-MET-PASS-CORP", "check_pass_rate", ["identity.mfa.*"]), Instance("EX-MET-RUNS-CORP", "collector_success_rate")];

        var report = await MetricPublisher.PublishAsync(ca, instances, Run(Result("identity.mfa.admins", CheckOutcome.Pass)), TestContext.Current.CancellationToken);

        var recorded = ca.Recorded.ShouldHaveSingleItem();
        (recorded.Sample.InstanceId, recorded.Sample.PeriodStart, recorded.Sample.Value, recorded.At).ShouldBe(("mi-pass", June, 100, Now));
        recorded.Sample.Why.ShouldContain("identity.mfa.*");
        report.Recorded.ShouldHaveSingleItem().Instance.ShouldBe("EX-MET-PASS-CORP");
        report.Problems.ShouldBeEmpty();
    }

    [Fact]
    public async Task Requirements_compliant_pct_reads_the_audit()
    {
        var ca = new InMemoryCisoAssistant();
        ca.Metrics["EX-MET-E8-CORP"] = new("mi-e8", "monthly");
        ca.Rows.Add(new RemoteRequirementAssessment("ra-1", "urn:example:req:1", "1", []));
        ca.Rows.Add(new RemoteRequirementAssessment("ra-2", "urn:example:req:2", "2", []));
        ca.Requirements["ra-1"] = new RequirementAssessmentValues(ComplianceResult.Compliant, "seen", []);

        await MetricPublisher.PublishAsync(ca, [Instance("EX-MET-E8-CORP", "requirements_compliant_pct", audit: "EX-E8-2026")], Run(), TestContext.Current.CancellationToken);

        ca.Recorded.ShouldHaveSingleItem().Sample.Value.ShouldBe(50);
        ca.Calls.ShouldContain("list EX-E8-2026");
    }

    [Fact]
    public async Task An_instance_not_applied_or_with_nothing_to_measure_is_named_and_not_recorded()
    {
        var ca = new InMemoryCisoAssistant();
        ca.Metrics["EX-MET-PASS-CORP"] = new("mi-pass", "monthly");
        ScopeMetricInstance[] instances = [Instance("EX-MET-PASS-CORP", "check_pass_rate", ["email.*"]), Instance("EX-MET-NEW-CORP", "collector_success_rate")];

        var report = await MetricPublisher.PublishAsync(ca, instances, Run(Result("identity.mfa.admins", CheckOutcome.Pass)), TestContext.Current.CancellationToken);

        ca.Recorded.ShouldBeEmpty();
        report.Problems.Select(p => p.Code).ShouldBe(["metric.nothing_measured", "metric.not_applied"]);
        report.Problems[0].Message.ShouldContain("EX-MET-PASS-CORP");
        report.Problems[1].Message.ShouldContain("hound scope apply");
    }
}
