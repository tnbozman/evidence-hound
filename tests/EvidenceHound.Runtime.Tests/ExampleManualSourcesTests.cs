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
    private static readonly string[] OtherLibraries = ["ai6", "essentials", "disp"];

    /// <summary>The Essential Eight templates by name, and the other libraries' (s11-10: AI6; s14: Essentials and DISP) as <c>library/name</c>.</summary>
    public static TheoryData<string> Templates() =>
        [.. ExampleManualSources.Folders().Select(Path.GetFileName).OfType<string>(), .. OtherLibraries.SelectMany(library => ExampleManualSources.Folders(library).Select(f => library + "/" + Path.GetFileName(f)))];

    [Fact]
    public void There_is_a_template_for_each_area_without_a_collector()
    {
        Templates().Select(t => t.Data).Where(t => !t.Contains('/', StringComparison.Ordinal)).ShouldBe(
            [
                "application-control-review", "asset-discovery", "backups", "event-logging", "mfa-configuration", "office-macro-settings",
                "privileged-access-review", "user-application-hardening",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void There_is_a_disp_template_for_each_record_no_system_holds()
    {
        // s14-02: security officer appointment, clearances register review, facility and visitor controls and the annual security report;
        // security awareness training records reuse the Essentials template.
        ExampleManualSources.Folders("disp").Select(Path.GetFileName).ShouldBe(
            ["annual-security-report", "clearances-register", "facility-security", "security-officer"], ignoreOrder: true);
        ExampleManualSources.Folders("essentials").Select(Path.GetFileName).ShouldContain("security-awareness-training");
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Every_template_config_validates(string template)
    {
        var config = ExampleManualSources.Config(template);

        var parsed = ConfigLoader.Parse(config, $"examples/manual-sources/{template}/source.yaml", Registry().Descriptions);

        parsed.Match(_ => [], ps => ps.Select(p => p.Message)).ShouldBeEmpty();
        var collector = parsed.Value().Collectors.Single(c => c.Name == Path.GetFileName(template));
        collector.Type.ShouldBe("manual");
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Every_readme_is_a_runbook_naming_its_checks(string template)
    {
        var readme = File.ReadAllText(Path.Combine(ExampleManualSources.Folder(template), "README.md"));

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

    public static IEnumerable<string> Folders(string library = "essential-eight")
    {
        var root = Path.Combine(Path.GetDirectoryName(Root)!, library);
        return Directory.Exists(root) ? Directory.GetDirectories(root).Order(StringComparer.Ordinal) : [];
    }

    /// <summary>A template's folder: an Essential Eight one by name, another library's as <c>library/name</c>.</summary>
    public static string Folder(string template) =>
        template.Contains('/', StringComparison.Ordinal) ? Path.Combine(Path.GetDirectoryName(Root)!, template) : Path.Combine(Root, template);

    /// <summary>The example Essential Eight config with this template's collector in place of its own collectors.</summary>
    public static string Config(string template)
    {
        var example = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "examples", "example-essential-eight.yaml"));
        var snippet = string.Join('\n', File.ReadAllLines(Path.Combine(Folder(template), "source.yaml")).Where(l => !l.StartsWith('#')).Select(l => "  " + l));
        var start = example.IndexOf("collectors:", StringComparison.Ordinal);
        var end = example.IndexOf("\ntargets:", StringComparison.Ordinal);
        return example[..start] + "collectors:\n" + snippet + "\n" + example[end..];
    }

    public static IEnumerable<CheckId> Checks(string template) =>
        File.ReadAllLines(Path.Combine(Folder(template), "source.yaml"))
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("checks:", StringComparison.Ordinal))
            .SelectMany(l => l["checks:".Length..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(c => CheckId.Create(c).Value());

    public static IEnumerable<CheckId> Checks() => Folders().Select(Path.GetFileName).OfType<string>().SelectMany(Checks);

    /// <summary>The checks of every template of another library, such as <c>essentials</c> (s14-01).</summary>
    public static IEnumerable<CheckId> LibraryChecks(string library) =>
        Folders(library).Select(f => library + "/" + Path.GetFileName(f)).SelectMany(Checks);
}
