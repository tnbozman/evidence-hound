using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Tests.Checks;
using FsCheck.Xunit;

namespace EvidenceHound.Core.Tests.Mapping;

public class MappingEngineTests
{
    private static readonly DateTimeOffset T0 = CheckTestData.CollectedAt;

    private static readonly string[] CheckNames = ["identity.mfa.admins", "identity.mfa.users", "backup.restore_tested", "patching.os.critical_48h"];

    [Fact]
    public void All_pass_rule_all_pass_is_compliant()
    {
        var results = Apply(MappingRule.AllPass, Check("identity.mfa.admins", CheckOutcome.Pass), Check("identity.mfa.users", CheckOutcome.Pass));

        results.Single().Result.ShouldBe(ComplianceResult.Compliant);
    }

    [Fact]
    public void All_pass_rule_one_fail_is_non_compliant()
    {
        var results = Apply(MappingRule.AllPass, Check("identity.mfa.admins", CheckOutcome.Pass), Check("identity.mfa.users", CheckOutcome.Fail));

        results.Single().Result.ShouldBe(ComplianceResult.NonCompliant);
    }

    [Fact]
    public void All_pass_rule_partial_without_fail_is_partially_compliant()
    {
        var results = Apply(MappingRule.AllPass, Check("identity.mfa.admins", CheckOutcome.Pass), Check("identity.mfa.users", CheckOutcome.Partial));

        results.Single().Result.ShouldBe(ComplianceResult.PartiallyCompliant);
    }

    [Fact]
    public void Any_pass_rule_mixed_is_partially_compliant()
    {
        var results = Apply(MappingRule.AnyPass, Check("identity.mfa.admins", CheckOutcome.Pass), Check("identity.mfa.users", CheckOutcome.Fail));

        results.Single().Result.ShouldBe(ComplianceResult.PartiallyCompliant);
    }

    [Theory]
    [InlineData(CheckOutcome.Pass, CheckOutcome.Pass, ComplianceResult.Compliant)]
    [InlineData(CheckOutcome.Partial, CheckOutcome.Fail, ComplianceResult.PartiallyCompliant)]
    [InlineData(CheckOutcome.Fail, CheckOutcome.Fail, ComplianceResult.NonCompliant)]
    public void Any_pass_rule_outcomes(CheckOutcome first, CheckOutcome second, ComplianceResult expected)
    {
        Apply(MappingRule.AnyPass, Check("identity.mfa.admins", first), Check("identity.mfa.users", second))
            .Single().Result.ShouldBe(expected);
    }

    [Theory]
    [InlineData(CheckOutcome.Pass, CheckOutcome.Pass, CheckOutcome.Fail, ComplianceResult.Compliant)]
    [InlineData(CheckOutcome.Pass, CheckOutcome.Fail, CheckOutcome.Fail, ComplianceResult.PartiallyCompliant)]
    [InlineData(CheckOutcome.Partial, CheckOutcome.Fail, CheckOutcome.Fail, ComplianceResult.PartiallyCompliant)]
    [InlineData(CheckOutcome.Fail, CheckOutcome.Fail, CheckOutcome.Fail, ComplianceResult.NonCompliant)]
    [InlineData(CheckOutcome.Pass, CheckOutcome.NotApplicable, CheckOutcome.NotApplicable, ComplianceResult.Compliant)]
    public void Threshold_rule_needs_n_passes_counting_only_applicable_checks(CheckOutcome a, CheckOutcome b, CheckOutcome c, ComplianceResult expected)
    {
        Apply(MappingRule.Threshold(2), Check("identity.mfa.admins", a), Check("identity.mfa.users", b), Check("backup.restore_tested", c))
            .Single().Result.ShouldBe(expected);
    }

    [Fact]
    public void Error_outcome_gives_not_assessed()
    {
        var results = Apply(MappingRule.AnyPass, Check("identity.mfa.admins", CheckOutcome.Pass), Check("identity.mfa.users", CheckOutcome.Error));

        results.Single().Result.ShouldBe(ComplianceResult.NotAssessed);
        results.Single().Observation.ShouldContain("identity.mfa.users: unable to assess (");
    }

    [Fact]
    public void Missing_check_gives_not_assessed()
    {
        var results = Apply(
            Requirement("EX-1.1", MappingRule.AllPass, "identity.mfa.admins", "identity.mfa.users"),
            Check("identity.mfa.admins", CheckOutcome.Pass));

        results.Single().Result.ShouldBe(ComplianceResult.NotAssessed);
        results.Single().Observation.ShouldContain("identity.mfa.users: missing");
    }

    [Fact]
    public void Not_applicable_checks_are_ignored_unless_all_are_not_applicable()
    {
        Apply(MappingRule.AllPass, Check("identity.mfa.admins", CheckOutcome.Pass), Check("identity.mfa.users", CheckOutcome.NotApplicable))
            .Single().Result.ShouldBe(ComplianceResult.Compliant);
        Apply(MappingRule.AllPass, Check("identity.mfa.admins", CheckOutcome.NotApplicable), Check("identity.mfa.users", CheckOutcome.NotApplicable))
            .Single().Result.ShouldBe(ComplianceResult.NotApplicable);
    }

    [Fact]
    public void Latest_duplicate_check_wins()
    {
        var results = Apply(
            MappingRule.AllPass,
            Check("identity.mfa.admins", CheckOutcome.Fail, T0.AddHours(2)),
            Check("identity.mfa.admins", CheckOutcome.Pass, T0.AddHours(5)),
            Check("identity.mfa.admins", CheckOutcome.Error, T0.AddHours(1)),
            Check("identity.mfa.users", CheckOutcome.Pass));

        results.Single().Result.ShouldBe(ComplianceResult.Compliant);
    }

    [Fact]
    public void Observation_lists_contributing_checks_and_outcomes()
    {
        var result = Apply(
            MappingRule.AllPass,
            Check("identity.mfa.admins", CheckOutcome.Pass, observation: "All 4 admins use MFA."),
            Check("identity.mfa.users", CheckOutcome.Fail, observation: "3 of 40 users lack MFA.")).Single();

        result.Observation.ShouldBe(
            "Rule all_pass. identity.mfa.admins: pass (All 4 admins use MFA.); identity.mfa.users: fail (3 of 40 users lack MFA.)");
    }

    [Fact]
    public void Evidence_refs_are_the_sorted_union_of_contributing_checks()
    {
        var result = Apply(
            Requirement("EX-1.1", MappingRule.AllPass, "identity.mfa.admins", "identity.mfa.users"),
            Check("identity.mfa.admins", CheckOutcome.Pass, evidence: ["ev-b", "ev-a"]),
            Check("identity.mfa.users", CheckOutcome.Pass, evidence: ["ev-a", "ev-c"]),
            Check("backup.restore_tested", CheckOutcome.Pass, evidence: ["ev-unmapped"])).Single();

        result.EvidenceRefs.ShouldBe(["ev-a", "ev-b", "ev-c"]);
    }

    [Fact]
    public void One_result_per_requirement_in_mapping_order()
    {
        var mapping = FrameworkMapping.Create("urn:example:risk:library:example-baseline", [
            Requirement("EX-2.1", MappingRule.AllPass, "backup.restore_tested"),
            Requirement("EX-1.1", MappingRule.AllPass, "identity.mfa.admins"),
        ]).Value();

        var results = MappingEngine.ApplyMapping(mapping, [Check("identity.mfa.admins", CheckOutcome.Pass)]);

        results.Select(r => (r.RequirementRef, r.Result)).ShouldBe([
            ("EX-2.1", ComplianceResult.NotAssessed),
            ("EX-1.1", ComplianceResult.Compliant),
        ]);
    }

    [Fact]
    public void Result_lists_the_outcome_of_each_check_behind_it_in_mapping_order()
    {
        var results = Apply(
            Requirement("EX-1.1", MappingRule.AnyPass, "identity.mfa.users", "identity.mfa.admins", "backup.restore_tested"),
            Check("identity.mfa.admins", CheckOutcome.Pass),
            Check("identity.mfa.users", CheckOutcome.Fail));

        results.Single().CheckOutcomes.ShouldBe(["identity.mfa.users:fail", "identity.mfa.admins:pass", "backup.restore_tested:missing"]);
    }

    [Property]
    public void Output_order_is_independent_of_input_order((byte Check, byte Outcome, byte Minute)[] picks, int seed)
    {
        var mapping = FrameworkMapping.Create("urn:example:risk:library:example-baseline", [
            Requirement("EX-1", MappingRule.AllPass, CheckNames[0], CheckNames[1]),
            Requirement("EX-2", MappingRule.AnyPass, CheckNames[1], CheckNames[2]),
            Requirement("EX-3", MappingRule.Threshold(2), CheckNames[0], CheckNames[2], CheckNames[3]),
        ]).Value();
        var checks = picks
            .Select(p => Check(CheckNames[p.Check % CheckNames.Length], (CheckOutcome)(p.Outcome % 5), T0.AddMinutes(p.Minute % 4), evidence: [$"ev-{p.Outcome % 3}"]))
            .ToArray();
        var shuffled = checks.ToArray();
        new Random(seed).Shuffle(shuffled);

        Snapshot(MappingEngine.ApplyMapping(mapping, shuffled)).ShouldBe(Snapshot(MappingEngine.ApplyMapping(mapping, checks)));
    }

    private static string[] Snapshot(IReadOnlyList<RequirementResult> results) =>
        [.. results.Select(r => $"{r.RequirementRef}|{r.Result}|{r.Observation}|{string.Join(",", r.EvidenceRefs)}")];

    private static IReadOnlyList<RequirementResult> Apply(MappingRule rule, params CheckResult[] checks) =>
        Apply(Requirement("EX-1.1", rule, [.. checks.Select(c => c.CheckId.Value).Distinct()]), checks);

    private static IReadOnlyList<RequirementResult> Apply(RequirementMapping requirement, params CheckResult[] checks) =>
        MappingEngine.ApplyMapping(FrameworkMapping.Create("urn:example:risk:library:example-baseline", [requirement]).Value(), checks);

    private static RequirementMapping Requirement(string reference, MappingRule rule, params string[] checks) =>
        RequirementMapping.Create(reference, checks.Select(CheckTestData.Id), rule).Value();

    private static CheckResult Check(
        string id,
        CheckOutcome outcome,
        DateTimeOffset? collectedAt = null,
        string observation = "Observed.",
        string[]? evidence = null) =>
        CheckResult.Create(CheckTestData.Id(id), outcome, observation, evidence ?? [], collectedAt ?? T0, "example-collector").Value();
}
