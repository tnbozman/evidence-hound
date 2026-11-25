using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Tests.Checks;

namespace EvidenceHound.Core.Tests.Publishing;

/// <summary>s15-05: after collecting, a run sets each BIA recovery flag named by checks, with the checks' evidence linked.</summary>
public class RecoveryPublisherTests
{
    private static readonly DateTimeOffset At = new(2026, 6, 30, 2, 0, 0, TimeSpan.Zero);
    private static readonly SourceLine Line = new("scope/bia/ex-bia-2026.yaml", 1);

    private static readonly ScopeBia Bia = new(
        "EX-BIA-2026", "Example BIA", null, new ScopeRef("Example Pty Ltd", Line), null, new ScopeRef("urn:example:risk:matrix:example-4x4", Line), null, null, [], [], null,
        [
            new ScopeAssetAssessment(new ScopeRef("Example 365", Line), [], [], true, false, false, null, [], Line)
            {
                Checks = new Dictionary<string, IReadOnlyList<ScopeRef>> { ["tested"] = [new ScopeRef("backup.restore_tested", Line)] },
            },
        ],
        Line);

    private static CheckResult Result(CheckOutcome outcome) =>
        CheckResult.Create(CheckId.Create("backup.restore_tested").Value(), outcome, "seen", ["example-backup/restore.json"], At, "example-backup").Value();

    private static InMemoryCisoAssistant Ca(bool tested, params string[] evidence)
    {
        var ca = new InMemoryCisoAssistant();
        ca.Recovery["EX-BIA-2026"] = new(StringComparer.Ordinal)
        {
            ["Example 365"] = new("aa-1", new Dictionary<string, bool> { ["recovery_tested"] = tested }) { EvidenceIds = ["ev-runbook"] },
        };
        foreach (var refId in evidence)
        {
            _ = ca.FindOrCreateEvidenceAsync(refId, refId, CancellationToken.None).Result;
        }

        return ca;
    }

    [Fact]
    public async Task A_flag_follows_its_checks_and_links_their_evidence()
    {
        var ca = Ca(tested: false, "example-backup/restore.json");

        var report = await RecoveryPublisher.PublishAsync(ca, [Bia], [Result(CheckOutcome.Pass)], TestContext.Current.CancellationToken);

        ca.RecoverySet.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            s => (s.Assessment, s.Field, s.Value).ShouldBe(("aa-1", "recovery_tested", true)),
            s => s.Evidence.ShouldBe(["ev-runbook", "uuid-example-backup/restore.json"]));
        report.Set.ShouldHaveSingleItem().AssessmentKey.ShouldBe("EX-BIA-2026/Example 365");
        report.Problems.ShouldBeEmpty();
    }

    [Fact]
    public async Task Evidence_not_in_ciso_assistant_is_named_and_the_flag_is_still_set()
    {
        var ca = Ca(tested: true);

        var report = await RecoveryPublisher.PublishAsync(ca, [Bia], [Result(CheckOutcome.Fail)], TestContext.Current.CancellationToken);

        ca.RecoverySet.ShouldHaveSingleItem().ShouldSatisfyAllConditions(s => s.Value.ShouldBeFalse(), s => s.Evidence.ShouldBe(["ev-runbook"]));
        report.Problems.ShouldHaveSingleItem().Code.ShouldBe("recovery.evidence_not_published");
    }

    [Fact]
    public async Task Nothing_is_read_or_sent_without_flags_named_by_checks()
    {
        var ca = Ca(tested: false);
        var handSet = Bia with { Assessments = [Bia.Assessments[0] with { Checks = new Dictionary<string, IReadOnlyList<ScopeRef>>() }] };

        var report = await RecoveryPublisher.PublishAsync(ca, [handSet], [Result(CheckOutcome.Pass)], TestContext.Current.CancellationToken);

        (report.Set.Count, ca.RecoverySet.Count).ShouldBe((0, 0));
    }
}
