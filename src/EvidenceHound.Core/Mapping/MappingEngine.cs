using EvidenceHound.Core.Checks;

namespace EvidenceHound.Core.Mapping;

/// <summary>Turns framework-neutral check results into requirement results for one framework. Pure.</summary>
public static class MappingEngine
{
    /// <summary>
    /// One result per requirement, in mapping order. Only the latest result of each check counts.
    /// A missing or errored check makes the requirement not assessed; not-applicable checks are ignored unless all are.
    /// </summary>
    public static IReadOnlyList<RequirementResult> ApplyMapping(FrameworkMapping mapping, IReadOnlyList<CheckResult> checkResults)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(checkResults);

        var latest = Latest(checkResults);
        return [.. mapping.Requirements.Select(requirement => Assess(requirement, latest))];
    }

    private static Dictionary<CheckId, CheckResult> Latest(IEnumerable<CheckResult> checkResults) =>
        checkResults
            .GroupBy(result => result.CheckId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(r => r.CollectedAt)
                    .ThenBy(r => r.Source, StringComparer.Ordinal)
                    .ThenBy(r => r.Outcome)
                    .ThenBy(r => r.Observation, StringComparer.Ordinal)
                    .ThenBy(r => string.Join('\n', r.EvidenceRefs), StringComparer.Ordinal)
                    .First());

    private static RequirementResult Assess(RequirementMapping requirement, Dictionary<CheckId, CheckResult> latest)
    {
        var contributing = requirement.Checks.Select(id => (Id: id, Result: latest.GetValueOrDefault(id))).ToArray();
        var found = contributing.Where(c => c.Result is not null).Select(c => c.Result!).ToArray();

        var result = found.Length < contributing.Length || found.Any(r => r.Outcome == CheckOutcome.Error)
            ? ComplianceResult.NotAssessed
            : Roll(requirement.Rule, [.. found.Select(r => r.Outcome).Where(o => o != CheckOutcome.NotApplicable)]);

        var observation = $"Rule {Names.Of(requirement.Rule)}. " + string.Join(
            "; ",
            contributing.Select(c => c.Result is null
                ? $"{c.Id}: missing"
                : $"{c.Id}: {Names.Of(c.Result.Outcome)} ({c.Result.Observation})"));
        var evidence = found.SelectMany(r => r.EvidenceRefs).Distinct().Order(StringComparer.Ordinal).ToArray();

        return new RequirementResult(requirement.RequirementRef, result, observation, evidence);
    }

    private static ComplianceResult Roll(MappingRule rule, CheckOutcome[] applicable)
    {
        if (applicable.Length == 0)
        {
            return ComplianceResult.NotApplicable;
        }

        var passes = applicable.Count(o => o == CheckOutcome.Pass);
        var anyProgress = passes > 0 || applicable.Contains(CheckOutcome.Partial);
        var needed = rule switch
        {
            MappingRule.ThresholdRule threshold => Math.Min(threshold.Count, applicable.Length),
            MappingRule.AnyPassRule or MappingRule.AllPassRule => applicable.Length,
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "Unknown mapping rule."),
        };

        return (passes >= needed, rule, anyProgress, applicable.Contains(CheckOutcome.Fail)) switch
        {
            (true, _, _, _) => ComplianceResult.Compliant,
            (false, MappingRule.AllPassRule, _, true) => ComplianceResult.NonCompliant,
            (false, _, true, _) => ComplianceResult.PartiallyCompliant,
            _ => ComplianceResult.NonCompliant,
        };
    }
}
