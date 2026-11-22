using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest.Profiles;

/// <summary>A profile read from the profiles folder, with its file and the SHA-256 of its content, as each run's manifest records.</summary>
public sealed record LoadedProfile(Profile Profile, string Path, string Sha256);

/// <summary>
/// The organisation's profiles folder, the only place profiles are read from. A profile is named by a plain lowercase name, and read
/// from <c>&lt;name&gt;.yaml</c> in the folder, so a name can never reach a file outside it.
/// </summary>
public static partial class ProfileFolder
{
    public const string DefaultPath = "/config/profiles";

    private static readonly string[] Extensions = [".yaml", ".yml"];

    public static bool IsValidName(string name) => Name().IsMatch(name);

    public static Result<LoadedProfile> Load(string folder, string name)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(name);
        if (!IsValidName(name))
        {
            return Result.Fail<LoadedProfile>(new Problem("profile.name_invalid", $"Profile name '{name}' must be lowercase letters, digits, hyphens and underscores, such as sophos-central."));
        }

        var path = Extensions.Select(ext => System.IO.Path.Combine(folder, name + ext)).FirstOrDefault(File.Exists);
        if (path is null)
        {
            return Result.Fail<LoadedProfile>(new Problem("profile.not_found", $"No profile '{name}' in the profiles folder {folder}. Copy it from the profile library (profiles/ in the Evidence Hound repo) into that folder."));
        }

        var bytes = File.ReadAllBytes(path);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return ProfileParser.Parse(Encoding.UTF8.GetString(bytes), path).Bind(profile => profile.Name == name
            ? Result.Ok(new LoadedProfile(profile, path, hash))
            : Result.Fail<LoadedProfile>(new Problem("profile.name_mismatch", $"{path} declares profile '{profile.Name}'; the file name must match: {profile.Name}.yaml.")));
    }

    /// <summary>The names of every profile in the folder.</summary>
    public static IReadOnlyList<string> Names(string folder) =>
        Directory.Exists(folder)
            ? [.. Directory.EnumerateFiles(folder, "*.y*ml").Select(System.IO.Path.GetFileNameWithoutExtension).OfType<string>().Where(IsValidName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]
            : [];

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Name();
}
