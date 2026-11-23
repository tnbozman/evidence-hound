using EvidenceHound.Collectors;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Runtime.Configuration;

namespace EvidenceHound.Cli;

/// <summary>
/// Which requirements a deployment's runs will write. A run publishes a requirement only when every one of its checks is reported
/// (<see cref="EvidenceHound.Runtime.Pipeline.Ownership"/>), so a requirement mapped to a source the deployment has not configured, such
/// as a manual source not yet set up, stays the GRC team's to write by hand. Pure.
/// </summary>
internal static class ReportedChecks
{
    public static IReadOnlyList<RequirementKey> Computed(FrameworkMapping mapping, DeploymentConfig config, IReadOnlyDictionary<string, CollectorDescription> descriptions)
    {
        var reported = Reported(config, descriptions);
        return [.. mapping.Requirements.Where(r => r.Checks.All(reported.Contains)).Select(r => r.Key)];
    }

    private static HashSet<CheckId> Reported(DeploymentConfig config, IReadOnlyDictionary<string, CollectorDescription> descriptions) =>
    [
        .. config.Collectors
            .Where(c => descriptions.ContainsKey(c.Type))
            .SelectMany(c => Checks(descriptions[c.Type], new CollectorSettings(c.Settings)))
            .SelectMany(id => CheckId.Create(id).Match(check => new[] { check }, _ => [])),
    ];

    private static IEnumerable<string> Checks(CollectorDescription description, CollectorSettings settings) =>
    [
        .. description.Checks,
        .. description.ChecksSetting is { } key && settings.Find(key) is { } listed
            ? listed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : [],
        .. description.ChecksFrom?.Invoke(settings) ?? [],
    ];
}
