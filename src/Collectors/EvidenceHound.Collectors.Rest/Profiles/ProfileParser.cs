using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest.Profiles;

/// <summary>Reads a profile's YAML into a <see cref="Profile"/>, reporting every problem at once with its line.</summary>
public static class ProfileParser
{
    public static Result<Profile> Parse(string yaml, string source) => throw new NotImplementedException();
}
