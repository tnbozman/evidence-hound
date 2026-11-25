using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s15-05: recovery flags set by each run from the checks the BIA names.</summary>
public class BiaPlanTests
{
    private static readonly DateTimeOffset At = new(2026, 6, 30, 2, 0, 0, TimeSpan.Zero);

    private static readonly ScopeBia Bia = BiaScopeTests.Parse(BiaScopeTests.Bia.Replace(
        "{documented: true, tested: false,", "{documented: true, tested_by: [backup.restore_tested, backup.daily],", StringComparison.Ordinal)).Scope.Bias[0];

    private static CheckResult Result(string id, CheckOutcome outcome, string file) =>
        CheckResult.Create(CheckId.Create(id).Value(), outcome, "seen", [file], At, "example-backup").Value();

    private static Dictionary<string, HeldRecovery> Held(bool tested) => new(StringComparer.Ordinal)
    {
        ["EX-BIA-2026/Example 365"] = new("aa-365", new Dictionary<string, bool> { ["recovery_documented"] = true, ["recovery_tested"] = tested, ["recovery_targets_met"] = false }),
    };

    [Fact]
    public void Flag_follows_named_checks()
    {
        CheckResult[] passing = [Result("backup.restore_tested", CheckOutcome.Pass, "example-backup/restore.json"), Result("backup.daily", CheckOutcome.Pass, "example-backup/jobs.json")];
        CheckResult[] oneFails = [passing[0], Result("backup.daily", CheckOutcome.Fail, "example-backup/jobs.json")];

        var raised = RecoveryFlags.Plan([Bia], passing, Held(tested: false)).ShouldHaveSingleItem();
        var lowered = RecoveryFlags.Plan([Bia], oneFails, Held(tested: true)).ShouldHaveSingleItem();

        (raised.AssessmentId, raised.Field, raised.Value).ShouldBe(("aa-365", "recovery_tested", true));
        raised.EvidenceRefs.ShouldBe(["example-backup/jobs.json", "example-backup/restore.json"]);
        raised.Why.ShouldContain("backup.daily, backup.restore_tested");
        (lowered.Field, lowered.Value).ShouldBe(("recovery_tested", false));
        lowered.Why.ShouldContain("backup.daily: fail");
    }

    [Fact]
    public void A_named_check_without_a_result_does_not_pass()
    {
        var update = RecoveryFlags.Plan([Bia], [Result("backup.restore_tested", CheckOutcome.Pass, "example-backup/restore.json")], Held(tested: true)).ShouldHaveSingleItem();

        update.Value.ShouldBeFalse();
        update.Why.ShouldContain("backup.daily: no result");
    }

    [Fact]
    public void Unchanged_flag_is_not_sent()
    {
        CheckResult[] passing = [Result("backup.restore_tested", CheckOutcome.Pass, "example-backup/restore.json"), Result("backup.daily", CheckOutcome.Pass, "example-backup/jobs.json")];

        RecoveryFlags.Plan([Bia], passing, Held(tested: true)).ShouldBeEmpty();
    }

    [Fact]
    public void An_assessment_not_yet_in_ciso_assistant_waits_for_scope_apply()
    {
        CheckResult[] passing = [Result("backup.restore_tested", CheckOutcome.Pass, "example-backup/restore.json"), Result("backup.daily", CheckOutcome.Pass, "example-backup/jobs.json")];

        RecoveryFlags.Plan([Bia], passing, new Dictionary<string, HeldRecovery>()).ShouldBeEmpty();
    }
}
