using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Mapping;

/// <summary>Which checks satisfy one framework requirement, and how their outcomes combine.</summary>
public sealed record RequirementMapping
{
    private RequirementMapping(RequirementKey key, IReadOnlyList<CheckId> checks, MappingRule rule)
    {
        Key = key;
        Checks = checks;
        Rule = rule;
    }

    /// <summary>The requirement's ref_id or URN, as the mapping names it.</summary>
    public string RequirementRef => Key.Value;

    /// <summary>How the requirement is named: ref or URN, and for a ref whether every matching row gets the result.</summary>
    public RequirementKey Key { get; }

    public IReadOnlyList<CheckId> Checks { get; }

    public MappingRule Rule { get; }

    /// <summary>s14-01: derived by <c>hound mapping derive</c> and not yet reviewed (<c>draft: true</c>); runs do not publish it.</summary>
    public bool Draft { get; init; }

    /// <summary>s17-01: <c>covers: part</c>, the checks answer only part of the requirement, so it is at most partially compliant.</summary>
    public bool CoversPart { get; init; }

    /// <summary>s19-07: on a row covering part, what a person assesses to finish the requirement (<c>rest:</c>).</summary>
    public string? Rest { get; init; }

    /// <summary>A requirement named by its ref_id.</summary>
    public static Result<RequirementMapping> Create(string requirementRef, IEnumerable<CheckId> checks, MappingRule rule) =>
        Create(RequirementKey.Ref(requirementRef), checks, rule);

    public static Result<RequirementMapping> Create(RequirementKey key, IEnumerable<CheckId> checks, MappingRule rule)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(rule);

        var checkList = checks.ToArray();
        var requirementRef = key.Value;
        return Validation.Validate(
            () => new RequirementMapping(key, checkList, rule),
            new Invariant(
                !string.IsNullOrWhiteSpace(requirementRef),
                new Problem("mapping.requirement_ref_empty", "A requirement needs a ref.")),
            new Invariant(
                key.Kind != RequirementKeyKind.Urn || requirementRef.StartsWith("urn:", StringComparison.Ordinal),
                new Problem("mapping.requirement_urn_invalid", $"Requirement urn '{requirementRef}' must start with 'urn:'.")),
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
