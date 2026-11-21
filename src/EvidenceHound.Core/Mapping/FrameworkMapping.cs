using EvidenceHound.Core.Results;

namespace EvidenceHound.Core.Mapping;

/// <summary>How framework-neutral checks satisfy one framework's requirements. One per framework, loaded from a file.</summary>
public sealed record FrameworkMapping
{
    private FrameworkMapping(string frameworkUrn, IReadOnlyList<RequirementMapping> requirements)
    {
        FrameworkUrn = frameworkUrn;
        Requirements = requirements;
    }

    /// <summary>URN of the framework library in CISO Assistant.</summary>
    public string FrameworkUrn { get; }

    /// <summary>Requirements in file order; each ref appears once.</summary>
    public IReadOnlyList<RequirementMapping> Requirements { get; }

    public static Result<FrameworkMapping> Create(string frameworkUrn, IEnumerable<RequirementMapping> requirements)
    {
        ArgumentNullException.ThrowIfNull(frameworkUrn);
        ArgumentNullException.ThrowIfNull(requirements);

        var list = requirements.ToArray();
        var duplicates = list.GroupBy(r => r.RequirementRef, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        return Validation.Validate(
            () => new FrameworkMapping(frameworkUrn, list),
            new Invariant(
                frameworkUrn.StartsWith("urn:", StringComparison.Ordinal),
                new Problem("mapping.framework_urn_invalid", $"Framework URN '{frameworkUrn}' must start with 'urn:'.")),
            new Invariant(
                duplicates.Length == 0,
                new Problem("mapping.duplicate_requirement_ref", $"Requirement refs appear more than once: {string.Join(", ", duplicates)}.")));
    }
}
