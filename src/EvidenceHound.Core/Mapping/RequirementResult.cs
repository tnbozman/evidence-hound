namespace EvidenceHound.Core.Mapping;

/// <summary>The result for one framework requirement, worked out from its mapped checks.</summary>
/// <param name="RequirementRef">The requirement's reference in the framework library.</param>
/// <param name="Result">The assessed state.</param>
/// <param name="Observation">Generated text listing the rule and each contributing check with its outcome.</param>
/// <param name="EvidenceRefs">Sorted, distinct evidence IDs from the contributing checks.</param>
public sealed record RequirementResult(string RequirementRef, ComplianceResult Result, string Observation, IReadOnlyList<string> EvidenceRefs)
{
    /// <summary>How the mapping names the requirement. Defaults to <see cref="RequirementRef"/> as a ref.</summary>
    public RequirementKey Key { get; init; } = RequirementKey.Ref(RequirementRef);

    /// <summary>Each contributing check as <c>check.id:outcome</c> (<c>missing</c> when it has no result), in mapping order.</summary>
    public IReadOnlyList<string> CheckOutcomes { get; init; } = [];
}
