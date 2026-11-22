using System.Reflection;
using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.Rest;

/// <summary>The config keys the rest collector reads itself; every other setting is a parameter of the profile.</summary>
public static class RestSettings
{
    public const string Profile = "profile";
    public const string ProfilesPath = "profiles_path";
    public const string IncludeIdentities = "include_identities";
}

/// <summary>
/// The built-in <c>rest</c> collector: reads any REST API described by a profile in the organisation's profiles folder. Each run
/// validates the profile and the instance first; a profile that is not valid makes only this instance's checks unable to assess.
/// </summary>
public sealed class RestCollector : ICollector, IInstanceValidator
{
    public const string TypeName = "rest";

    public string Type => TypeName;

    public string Version => "1.0.0";

    /// <summary>The version of hound that profiles' <c>requires</c> is compared with.</summary>
    public static string HoundVersion { get; } =
        typeof(RestCollector).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>What profiles are validated against.</summary>
    public static ValidationContext Validation { get; } = new(HoundVersion, []);

    public CollectorDescription Describe() => new(
        [],
        [
            new(RestSettings.Profile, "Name of the profile in the profiles folder, for example sophos-central.", Required: true),
            new(RestSettings.ProfilesPath, $"The profiles folder, mounted read-only. Default: {ProfileFolder.DefaultPath}.", Required: false, IsPath: true),
            new(RestSettings.IncludeIdentities, "true keeps names, addresses and other identities in evidence. Default: false (hashed).", Required: false),
        ],
        [])
    {
        ChecksFrom = settings => Load(settings).Match(loaded => (IReadOnlyList<string>)[.. loaded.Profile.Checks.Select(c => c.Id)], _ => []),
    };

    public IReadOnlyList<Problem> ValidateInstance(CollectorSettings settings, IReadOnlyCollection<string> secrets) =>
        Load(settings).Match(loaded => Problems(loaded.Profile, settings, secrets), problems => problems);

    public Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Load(context.Settings).Bind(loaded => Problems(loaded.Profile, context.Settings, [.. context.Secrets.Keys]) switch
        {
            [] => Result.Fail<CollectionResult>(new Problem("rest.not_built", "Collection by profile is not built yet.")),
            var problems => UnableToAssess(loaded.Profile, problems, context),
        }));
    }

    private static Result<LoadedProfile> Load(CollectorSettings settings) =>
        settings.Find(RestSettings.Profile) is { Length: > 0 } name
            ? ProfileFolder.Load(settings.GetOrDefault(RestSettings.ProfilesPath, ProfileFolder.DefaultPath), name)
            : Result.Fail<LoadedProfile>(new Problem("rest.setting_missing", $"Setting '{RestSettings.Profile}' is required."));

    private static IReadOnlyList<Problem> Problems(Profile profile, CollectorSettings settings, IReadOnlyCollection<string> secrets) =>
        [.. ProfileValidator.Validate(profile, Validation), .. ProfileValidator.ValidateInstance(profile, Values(settings, profile), secrets)];

    /// <summary>The instance's settings as a dictionary: the reserved ones and every profile parameter it sets.</summary>
    private static Dictionary<string, string> Values(CollectorSettings settings, Profile profile) =>
        ProfileValidator.ReservedSettings.Concat(profile.Parameters.Select(p => p.Name))
            .Select(name => (name, value: settings.Find(name)))
            .Where(pair => pair.value is not null)
            .ToDictionary(pair => pair.name, pair => pair.value!, StringComparer.Ordinal);

    /// <summary>Every check of the profile, unable to assess because the profile or the instance is not valid.</summary>
    private static Result<CollectionResult> UnableToAssess(Profile profile, IReadOnlyList<Problem> problems, CollectorContext context)
    {
        var detail = string.Join("; ", problems.Select(p => $"{p.Code}: {p.Message}"));
        var observation = Truncate($"Unable to assess: profile '{profile.Name}' is not valid for this instance. {detail}");
        var now = context.Clock.UtcNow;
        return Result.Combine(profile.Checks.Select(check => CheckId.Create(check.Id)
                .Bind(id => CheckResult.Create(id, CheckOutcome.Error, observation, [], now, context.Name))))
            .Map(checks => new CollectionResult(checks, [], []));
    }

    private static string Truncate(string text) =>
        text.Length <= CheckResult.MaxObservationLength ? text : text[..(CheckResult.MaxObservationLength - 1)] + "…";
}
