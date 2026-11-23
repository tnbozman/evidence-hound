using EvidenceHound.Core.Checks;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EvidenceHound.Runtime.Tests;

/// <summary>
/// s10-04: <c>examples/manual-sources/essential-eight/</c> holds one manual source per Essential Eight area no collector reads. Each is a
/// collector to paste into hound.yaml and a README that is both the owner's runbook and the evidence plan row's.
/// </summary>
public class ExampleManualSourcesTests
{
    public static TheoryData<string> Templates() => [.. ExampleManualSources.Folders().Select(Path.GetFileName).OfType<string>()];

    [Fact]
    public void There_is_a_template_for_each_area_without_a_collector()
    {
        Templates().Select(t => t.Data).ShouldBe(
            [
                "application-control-review", "asset-discovery", "backups", "event-logging", "mfa-configuration", "office-macro-settings",
                "privileged-access-review", "user-application-hardening",
            ],
            ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Every_template_config_validates(string template)
    {
        var config = ExampleManualSources.Config(template);

        var parsed = ConfigLoader.Parse(config, $"examples/manual-sources/essential-eight/{template}/source.yaml", Registry().Descriptions);

        parsed.Match(_ => [], ps => ps.Select(p => p.Message)).ShouldBeEmpty();
        var collector = parsed.Value().Collectors.Single(c => c.Name == template);
        collector.Type.ShouldBe("manual");
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Every_readme_is_a_runbook_naming_its_checks(string template)
    {
        var readme = File.ReadAllText(Path.Combine(ExampleManualSources.Root, template, "README.md"));

        foreach (var heading in new[] { "## What to export", "## How often", "## Who attests", "## Send it to the GRC team", "## Checks" })
        {
            readme.ShouldContain(heading);
        }

        foreach (var check in ExampleManualSources.Checks(template))
        {
            readme.ShouldContain($"`{check}`");
        }
    }

    private static CollectorRegistry Registry()
    {
        using var services = new ServiceCollection().AddLogging().AddBuiltInCollectors().BuildServiceProvider();
        return CollectorRegistry.FromServices(services).Value();
    }
}

/// <summary>The example manual sources, read from the test's copy of the repository's examples.</summary>
internal static class ExampleManualSources
{
    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "files", "examples", "manual-sources", "essential-eight");

    public static IEnumerable<string> Folders() => Directory.Exists(Root) ? Directory.GetDirectories(Root).Order(StringComparer.Ordinal) : [];

    /// <summary>The example Essential Eight config with this template's collector in place of its own collectors.</summary>
    public static string Config(string template)
    {
        var example = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "examples", "example-essential-eight.yaml"));
        var snippet = string.Join('\n', File.ReadAllLines(Path.Combine(Root, template, "source.yaml")).Where(l => !l.StartsWith('#')).Select(l => "  " + l));
        var start = example.IndexOf("collectors:", StringComparison.Ordinal);
        var end = example.IndexOf("\ntarget:", StringComparison.Ordinal);
        return example[..start] + "collectors:\n" + snippet + "\n" + example[end..];
    }

    public static IEnumerable<CheckId> Checks(string template) =>
        File.ReadAllLines(Path.Combine(Root, template, "source.yaml"))
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("checks:", StringComparison.Ordinal))
            .SelectMany(l => l["checks:".Length..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(c => CheckId.Create(c).Value());

    public static IEnumerable<CheckId> Checks() => Folders().Select(Path.GetFileName).OfType<string>().SelectMany(Checks);
}
