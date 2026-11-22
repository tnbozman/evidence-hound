using System.Reflection;
using EvidenceHound.Collectors.Rest.Engine;
using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Packaging;
using EvidenceHound.Core.Results;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Collectors.Rest;

/// <summary>The config keys the rest collector reads itself; every other setting is a parameter of the profile.</summary>
public static class RestSettings
{
    public const string Profile = "profile";
    public const string ProfilesPath = "profiles_path";
    public const string IncludeIdentities = "include_identities";
    public const string RequireProfileTests = "require_profile_tests";
}

/// <summary>
/// The built-in <c>rest</c> collector: reads any REST API described by a profile in the organisation's profiles folder. Each run
/// validates the profile and the instance first; a profile that is not valid makes only this instance's checks unable to assess.
/// </summary>
public sealed partial class RestCollector : ICollector, IInstanceValidator
{
    public const string TypeName = "rest";

    private readonly IDelay _delay;

    public RestCollector()
        : this(SystemDelay.Instance)
    {
    }

    internal RestCollector(IDelay delay) => _delay = delay;

    public string Type => TypeName;

    public string Version => "1.0.0";

    /// <summary>The version of hound that profiles' <c>requires</c> is compared with.</summary>
    public static string HoundVersion { get; } =
        typeof(RestCollector).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>What profiles are validated against.</summary>
    public static ValidationContext Validation { get; } = new(HoundVersion, CodeChecks.Names);

    public CollectorDescription Describe() => new(
        [],
        [
            new(RestSettings.Profile, "Name of the profile in the profiles folder, for example sophos-central.", Required: true),
            new(RestSettings.ProfilesPath, $"The profiles folder, mounted read-only. Default: {ProfileFolder.DefaultPath}.", Required: false, IsPath: true),
            new(RestSettings.IncludeIdentities, "true keeps names, addresses and other identities in evidence. Default: false (hashed).", Required: false),
            new(RestSettings.RequireProfileTests, "true makes a profile without tests (<profile>.tests/) unable to assess. Default: false (a warning).", Required: false),
        ],
        [])
    {
        ChecksFrom = settings => Load(settings).Match(loaded => (IReadOnlyList<string>)[.. loaded.Profile.Checks.Select(c => c.Id)], _ => []),
        SettingsFrom = settings => Load(settings).Match(loaded => WithProfile(Describe(), loaded.Profile), _ => Describe()),
    };

    /// <summary>The description with the profile's parameters as settings and its secrets: required when the profile needs one and gives no default.</summary>
    private static CollectorDescription WithProfile(CollectorDescription described, Profile profile) => described with
    {
        Settings = [.. described.Settings, .. profile.Parameters.Select(p => new SettingDescription(p.Name, p.Description, p.Required && p.Default is null))],
        Secrets = [.. profile.Secrets.Select(s => new SettingDescription(s.Name, s.Description, s.Required))],
        SettingsFrom = null,
    };

    public IReadOnlyList<Problem> ValidateInstance(CollectorSettings settings, IReadOnlyCollection<string> secrets) =>
        Load(settings).Match(loaded => Problems(loaded.Profile, settings, secrets), problems => problems);

    /// <summary>The problem when the instance requires profile tests and its profile has none; null otherwise.</summary>
    private static Problem? TestsProblem(Profile profile, CollectorSettings settings) =>
        IsTrue(settings, RestSettings.RequireProfileTests) && !ProfileTestRunner.HasTests(ProfilesPath(settings), profile.Name)
            ? ProfileTestRunner.Missing(profile.Name)
            : null;

    private static bool IsTrue(CollectorSettings settings, string key) => bool.TryParse(settings.Find(key), out var value) && value;

    private static string ProfilesPath(CollectorSettings settings) => settings.GetOrDefault(RestSettings.ProfilesPath, ProfileFolder.DefaultPath);

    public async Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = Load(context.Settings);
        if (loaded is not Result<LoadedProfile>.Ok { Value: var file })
        {
            return Result.Fail<CollectionResult>(((Result<LoadedProfile>.Fail)loaded).Errors);
        }

        var profile = file.Profile;
        if (!ProfileTestRunner.HasTests(ProfilesPath(context.Settings), profile.Name) && !IsTrue(context.Settings, RestSettings.RequireProfileTests))
        {
            ProfileUntested(context.Logger, context.Name, profile.Name, ProfileTestRunner.TestsFolder(ProfilesPath(context.Settings), profile.Name));
        }

        var result = Problems(profile, context.Settings, [.. context.Secrets.Keys]) switch
        {
            [] => await CollectValidAsync(profile, context, cancellationToken).ConfigureAwait(false),
            var problems => UnableToAssess(profile, problems, context),
        };
        return result.Map(r => r with { Profiles = [new ManifestProfile(profile.Name, profile.Version, profile.Source, file.Sha256)] });
    }

    /// <summary>
    /// Collect, extract, assess: runs every query, saves each one read and each dataset built as one evidence file, then judges every
    /// check on exactly those files. A file that would hold a secret is not saved, and what reads it is unable to assess.
    /// </summary>
    private async Task<Result<CollectionResult>> CollectValidAsync(Profile profile, CollectorContext context, CancellationToken cancellationToken)
    {
        var values = InstanceValuesOf(profile, context);
        var engine = new QueryEngine(context.HttpClients.CreateClient(TypeName), context.Clock, _delay);
        var snapshot = await engine.CollectAsync(profile, values, cancellationToken).ConfigureAwait(false);
        var secrets = context.Secrets.Values.ToArray();
        var now = context.Clock.UtcNow;

        var queries = new Dictionary<string, QueryResult>(snapshot.Queries, StringComparer.Ordinal);
        var queryFiles = new Dictionary<string, CollectedEvidence>(StringComparer.Ordinal);
        foreach (var query in snapshot.Queries.Values.OfType<QueryResult.Collected>().Where(q => !q.IsBootstrap))
        {
            switch (QueryEvidence.File(query, values.IncludeIdentities, secrets))
            {
                case Result<CollectedEvidence>.Ok saved:
                    queryFiles[query.Name] = saved.Value;
                    break;
                case Result<CollectedEvidence>.Fail refused:
                    queries[query.Name] = new QueryResult.Failed(query.Name, refused.Errors[0].Message);
                    break;
            }
        }

        var renderer = new Renderer(values.Parameters, snapshot.Vars, values.Secrets);
        var sources = new Dictionary<string, RowSource>(Extraction.Build(profile, queries, values.IncludeIdentities, renderer, now), StringComparer.Ordinal);
        var datasetFiles = new Dictionary<string, CollectedEvidence>(StringComparer.Ordinal);
        foreach (var dataset in profile.Datasets.Select(d => sources[d.Name]).OfType<RowSource.Rows>())
        {
            switch (DatasetEvidence.File(dataset, queryFiles, secrets))
            {
                case Result<CollectedEvidence>.Ok saved:
                    datasetFiles[dataset.Name] = saved.Value;
                    break;
                case Result<CollectedEvidence>.Fail refused:
                    sources[dataset.Name] = new RowSource.Failed(dataset.Name, refused.Errors[0].Message);
                    break;
            }
        }

        var files = queryFiles.Concat(datasetFiles).ToDictionary(StringComparer.Ordinal);
        return Result.Combine(profile.Checks.Select(check => Assess(check, sources, files, renderer, now, context.Name)))
            .Map(checks => new CollectionResult(checks, [.. queryFiles.Values, .. datasetFiles.Values], []));
    }

    private static Result<CheckResult> Assess(
        Check check, Dictionary<string, RowSource> sources, Dictionary<string, CollectedEvidence> files, Renderer renderer, DateTimeOffset now, string source)
    {
        var finding = CheckEvaluator.Evaluate(check, sources, renderer, now, name => files.GetValueOrDefault(name)?.FileName);
        var cited = check.Over.Select(name => files.GetValueOrDefault(name)?.FileName).OfType<string>().ToArray();
        return CheckId.Create(check.Id).Bind(id => CheckResult.Create(id, finding.Outcome, Truncate(finding.Observation), cited, now, source));
    }

    private static InstanceValues InstanceValuesOf(Profile profile, CollectorContext context) => new(
        profile.Parameters
            .Select(p => (p.Name, Value: context.Settings.Find(p.Name) ?? p.Default))
            .Where(p => p.Value is not null)
            .ToDictionary(p => p.Name, p => p.Value!, StringComparer.Ordinal),
        context.Secrets,
        bool.TryParse(context.Settings.Find(RestSettings.IncludeIdentities), out var include) && include);

    private static Result<LoadedProfile> Load(CollectorSettings settings) =>
        settings.Find(RestSettings.Profile) is { Length: > 0 } name
            ? ProfileFolder.Load(ProfilesPath(settings), name)
            : Result.Fail<LoadedProfile>(new Problem("rest.setting_missing", $"Setting '{RestSettings.Profile}' is required."));

    private static IReadOnlyList<Problem> Problems(Profile profile, CollectorSettings settings, IReadOnlyCollection<string> secrets) =>
        [.. ProfileValidator.Validate(profile, Validation), .. ProfileValidator.ValidateInstance(profile, Values(settings, profile), secrets), .. TestsProblem(profile, settings) is { } missing ? [missing] : Array.Empty<Problem>()];

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

    [LoggerMessage(EventId = 3100, Level = LogLevel.Warning, Message = "{Collector}: profile '{Profile}' has no tests in {Folder}; add recordings and expected results and run hound profile test {Profile}.")]
    private static partial void ProfileUntested(ILogger logger, string collector, string profile, string folder);
}
