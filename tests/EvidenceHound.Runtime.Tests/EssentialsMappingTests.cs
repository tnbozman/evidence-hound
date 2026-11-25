using EvidenceHound.Collectors;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EvidenceHound.Runtime.Tests;

/// <summary>s14-01: the ASD Essentials for enterprise IT mapping, priority phase 1 first, for the checks Evidence Hound has today.</summary>
public class EssentialsMappingTests
{
    private static readonly string Files = Path.Combine(AppContext.BaseDirectory, "files");
    private static readonly string[] Phase1 = ["1"];
    private static readonly string[] ReferenceTypes = ["m365", "vuln-scan"];
    private static readonly string[] ReferenceProfiles = ["sophos-central", "ciso-assistant"];

    /// <summary>Of the ciso-assistant profile's checks, the supplier, risk and recovery ones answer the Essentials; its AI ones answer AI6.</summary>
    private static bool Relevant(string profile, string check) => profile != "ciso-assistant" || check.StartsWith("supplier.", StringComparison.Ordinal) || check.StartsWith("risk.", StringComparison.Ordinal) || check.StartsWith("bia.", StringComparison.Ordinal);

    private static LoadedMapping Mapping { get; } =
        MappingLoader.Parse(File.ReadAllText(Path.Combine(Files, "mappings", "asd-essentials-enterprise-it.yaml")), "mappings/asd-essentials-enterprise-it.yaml").Value();

    private static FrameworkSnapshot Snapshot { get; } =
        FrameworkSnapshotJson.Read(File.ReadAllText(Path.Combine(Files, "frameworks", "asd-essentials-enterprise-it.json")), "frameworks/asd-essentials-enterprise-it.json").Value();

    [Fact]
    public void All_requirement_urns_exist_in_framework_snapshot()
    {
        Mapping.Mapping.FrameworkUrn.ShouldBe("urn:intuitem:risk:framework:asd-essentials-enterprise-it");
        MappingValidator.Validate(Mapping, Snapshot).ShouldBeEmpty();
        Mapping.Mapping.Requirements.Select(r => r.Key.Kind).Concat(Mapping.ManualOnly.Select(m => m.Key.Kind)).ShouldAllBe(k => k == RequirementKeyKind.Urn);
    }

    [Fact]
    public void Every_reference_check_is_mapped()
    {
        // Every check maps to at least one priority phase 1 action, so a new deployment's first run fills phase 1.
        var phase1 = Mapping.Mapping.Requirements
            .Where(r => Snapshot.Resolve(r.Key).Any(n => n.Groups.Intersect(Phase1).Any()))
            .SelectMany(r => r.Checks)
            .ToHashSet();

        var known = ReferenceChecks().Concat(ExampleManualChecks()).Distinct().ToList();

        known.ShouldNotBeEmpty();
        known.Where(c => !phase1.Contains(c)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_mapped_check_is_reported_by_a_reference_or_example_manual_collector()
    {
        var known = ReferenceChecks().Concat(ExampleManualChecks()).ToHashSet();

        Mapping.Mapping.Requirements.SelectMany(r => r.Checks).Distinct().Where(c => !known.Contains(c)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_phase_1_action_has_checks_or_is_manual_only_with_a_source()
    {
        var named = Mapping.Mapping.Requirements.Select(r => r.RequirementRef).Concat(Mapping.ManualOnly.Select(m => m.Key.Value)).ToHashSet(StringComparer.Ordinal);

        Snapshot.Nodes.Where(n => n.Groups.Intersect(Phase1).Any() && !named.Contains(n.Urn)).Select(n => n.Urn).ShouldBeEmpty();
        Mapping.ManualOnly.Where(m => string.IsNullOrWhiteSpace(m.Source)).ShouldBeEmpty();
    }

    [Fact]
    public void The_reference_mapping_has_no_draft_rows()
    {
        Mapping.Mapping.Requirements.Where(r => r.Draft).Select(r => r.RequirementRef).ShouldBeEmpty();
    }

    [Fact]
    public void Example_config_runs_the_essentials_audit_beside_the_essential_eight_one()
    {
        var config = Config();

        config.Targets.Select(t => (t.ComplianceAssessmentRefId, Path.GetFileName(t.MappingPath))).ShouldBe([
            ("EX-E8-2026", "essential-eight-2023.yaml"),
            ("EX-ESSENTIALS-2026", "asd-essentials-enterprise-it.yaml"),
        ]);
    }

    private static IEnumerable<CheckId> ReferenceChecks()
    {
        var descriptions = Registry().Descriptions;
        var profileChecks = ReferenceProfiles.SelectMany(profile => descriptions["rest"].ChecksFrom!(new CollectorSettings(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profile"] = profile,
            ["profiles_path"] = Path.Combine(Files, "profiles"),
        })).Where(c => Relevant(profile, c))).ToList();
        return ReferenceTypes
            .SelectMany(type => descriptions[type].Checks)
            .Concat(profileChecks)
            .Select(c => CheckId.Create(c).Value());
    }

    private static IEnumerable<CheckId> ExampleManualChecks() =>
        Config().Collectors
            .Where(c => c.Type == "manual")
            .SelectMany(c => c.Settings["checks"].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(c => CheckId.Create(c).Value())
            .Concat(ExampleManualSources.Checks())
            .Concat(ExampleManualSources.LibraryChecks("essentials"));

    private static DeploymentConfig Config() =>
        ConfigLoader.Parse(File.ReadAllText(Path.Combine(Files, "examples", "example-essential-eight.yaml")), "examples/example-essential-eight.yaml", Registry().Descriptions).Value();

    private static CollectorRegistry Registry()
    {
        using var services = new ServiceCollection().AddLogging().AddBuiltInCollectors().BuildServiceProvider();
        return CollectorRegistry.FromServices(services).Value();
    }
}
