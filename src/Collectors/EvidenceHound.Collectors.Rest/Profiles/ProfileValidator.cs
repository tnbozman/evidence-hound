using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest.Profiles;

/// <summary>What a profile is validated against: this hound's version and the names of its built-in code checks.</summary>
public sealed record ValidationContext(string HoundVersion, IReadOnlyCollection<string> CodeChecks);

/// <summary>Checks a parsed profile before anything is called, and each collector instance's settings against it.</summary>
public static class ProfileValidator
{
    public static IReadOnlyList<Problem> Validate(Profile profile, ValidationContext context) => throw new NotImplementedException();

    public static IReadOnlyList<Problem> ValidateInstance(Profile profile, IReadOnlyDictionary<string, string> settings, IReadOnlyCollection<string> secrets) =>
        throw new NotImplementedException();
}
