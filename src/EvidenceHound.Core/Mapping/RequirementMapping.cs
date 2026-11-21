using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Mapping;

/// <summary>Which checks satisfy one framework requirement, and how their outcomes combine.</summary>
public sealed record RequirementMapping
{
    private RequirementMapping(string requirementRef, IReadOnlyList<CheckId> checks, MappingRule rule)
    {
        RequirementRef = requirementRef;
        Checks = checks;
        Rule = rule;
    }

    /// <summary>The requirement's reference in the framework library, for example its ref ID or URN.</summary>
    public string RequirementRef { get; }

    /// <summary>How the requirement is named: ref or URN, and for a ref whether every matching row gets the result.</summary>
    public RequirementKey Key => throw new NotImplementedException();

    public IReadOnlyList<CheckId> Checks { get; }

    public MappingRule Rule { get; }

    public static Result<RequirementMapping> Create(string requirementRef, IEnumerable<CheckId> checks, MappingRule rule)
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(rule);

        var checkList = checks.ToArray();
        return Validation.Validate(
            () => new RequirementMapping(requirementRef, checkList, rule),
            new Invariant(
                !string.IsNullOrWhiteSpace(requirementRef),
                new Problem("mapping.requirement_ref_empty", "A requirement needs a ref.")),
            new Invariant(
                checkList.Length > 0,
                new Problem("mapping.checks_empty", $"Requirement '{requirementRef}' needs at least one check.")),
            new Invariant(
                checkList.Distinct().Count() == checkList.Length,
                new Problem("mapping.duplicate_check", $"Requirement '{requirementRef}' lists a check more than once.")),
            new Invariant(
                rule is not MappingRule.ThresholdRule threshold || (threshold.Count >= 1 && threshold.Count <= checkList.Length),
                new Problem("mapping.threshold_out_of_range", $"Requirement '{requirementRef}' threshold must be between 1 and its number of checks ({checkList.Length}).")));
    }
}
