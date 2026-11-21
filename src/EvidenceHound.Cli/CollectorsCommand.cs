using System.CommandLine;
using EvidenceHound.Collectors;
using EvidenceHound.Runtime.Collectors;

namespace EvidenceHound.Cli;

/// <summary><c>hound collectors list</c>: the registered collector types, with the checks they report and the settings they read.</summary>
internal static class CollectorsCommand
{
    public static Command Build(TextWriter output, CollectorRegistry registry)
    {
        var list = new Command("list", "List the registered collector types with their check IDs, settings and secrets.");
        list.SetAction(async (_, cancellationToken) =>
        {
            foreach (var collector in registry.ByType.Values.OrderBy(c => c.Type, StringComparer.Ordinal))
            {
                await output.WriteAsync(Describe(collector, registry.Descriptions[collector.Type]).AsMemory(), cancellationToken);
            }

            return ExitCodes.Ok;
        });

        return new Command("collectors", "Work with the collector types this build knows.") { list };
    }

    private static string Describe(ICollector collector, CollectorDescription description) => $"""
        {collector.Type} {collector.Version}
          checks: {Checks(description)}
          settings: {Settings(description.Settings)}
          secrets: {Settings(description.Secrets)}

        """;

    private static string Checks(CollectorDescription description) => (description.Checks.Count, description.ChecksSetting) switch
    {
        (0, { } setting) => $"set per instance by setting '{setting}'",
        (_, { } setting) => $"{Names(description.Checks.Order(StringComparer.Ordinal))}, plus those in setting '{setting}'",
        _ => Names(description.Checks.Order(StringComparer.Ordinal)),
    };

    private static string Settings(IEnumerable<SettingDescription> settings) =>
        Names(settings.Select(s => $"{s.Name} ({(s.Required ? "required" : "optional")})"));

    private static string Names(IEnumerable<string> names) => names.ToArray() is [_, ..] some ? string.Join(", ", some) : "none";
}
