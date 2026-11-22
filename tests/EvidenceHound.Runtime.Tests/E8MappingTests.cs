using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EvidenceHound.Runtime.Tests;

/// <summary>s06-02: the Essential Eight (November 2023) ML2 mapping for the checks Evidence Hound has today.</summary>
public class E8MappingTests
{
    private static readonly string Files = Path.Combine(AppContext.BaseDirectory, "files");
    private static readonly string[] Ml2Groups = ["1", "2"];

    /// <summary>The reference collectors whose checks the mapping must cover: Microsoft 365 (s05) and Sophos Central (s08). Veeam joins in s09.</summary>
    private static readonly string[] ReferenceTypes = ["m365", "sophos"];

    private static LoadedMapping Mapping { get; } =
        MappingLoader.Parse(File.ReadAllText(Path.Combine(Files, "mappings", "essential-eight-2023.yaml")), "mappings/essential-eight-2023.yaml").Value();

    private static FrameworkSnapshot Snapshot { get; } =
        FrameworkSnapshotJson.Read(File.ReadAllText(Path.Combine(Files, "frameworks", "essential-eight-2023.json")), "frameworks/essential-eight-2023.json").Value();

    [Fact]
    public void Mapping_targets_the_2023_framework_not_the_legacy_one()
    {
        Mapping.Mapping.FrameworkUrn.ShouldBe("urn:intuitem:risk:framework:essential-eight-2023");
    }

    [Fact]
    public void All_requirement_urns_exist_in_framework_snapshot()
    {
        MappingValidator.Validate(Mapping, Snapshot).ShouldBeEmpty();
    }

    [Fact]
    public void Every_reference_check_is_mapped()
    {
        var mapped = Mapping.Mapping.Requirements.SelectMany(r => r.Checks).ToHashSet();

        var collectorChecks = ReferenceChecks().Concat(ExampleManualChecks()).Distinct().ToList();

        collectorChecks.ShouldNotBeEmpty();
        collectorChecks.Where(c => !mapped.Contains(c)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_mapped_check_is_reported_by_a_reference_or_example_manual_collector()
    {
        var known = ReferenceChecks().Concat(ExampleManualChecks()).ToHashSet();

        Mapping.Mapping.Requirements.SelectMany(r => r.Checks).Distinct().Where(c => !known.Contains(c)).ShouldBeEmpty();
    }

    [Fact]
    public void Office_macro_antivirus_scanning_is_manual_because_sophos_reads_network_devices_only()
    {
        Mapping.Mapping.Requirements.ShouldNotContain(r => r.RequirementRef == "ISM-1672");
        Mapping.ManualOnly.ShouldContain(m => m.Key.Value == "ISM-1672");
    }

    [Fact]
    public void Repeated_ism_refs_use_rows_all()
    {
        var keys = Mapping.Mapping.Requirements.Select(r => r.Key).Concat(Mapping.ManualOnly.Select(m => m.Key));

        keys.Where(k => k.Kind == RequirementKeyKind.Ref && Snapshot.Resolve(k).Count > 1 && !k.AllRows).ShouldBeEmpty();
    }

    [Fact]
    public void Mapped_requirements_are_in_maturity_levels_one_or_two()
    {
        Mapping.Mapping.Requirements
            .Where(r => !Snapshot.Resolve(r.Key).Any(n => n.Groups.Intersect(Ml2Groups).Any()))
            .Select(r => r.RequirementRef)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Every_ml1_and_ml2_requirement_is_mapped_or_manual_only()
    {
        var covered = Mapping.Mapping.Requirements.Select(r => r.RequirementRef).Concat(Mapping.ManualOnly.Select(m => m.Key.Value)).ToHashSet(StringComparer.Ordinal);

        Snapshot.Nodes
            .Where(n => n.Groups.Intersect(Ml2Groups).Any() && !covered.Contains(n.RefId!))
            .Select(n => n.RefId)
            .Distinct()
            .ShouldBeEmpty();
    }

    [Fact]
    public void Manual_only_requirements_are_not_also_mapped_and_name_a_source()
    {
        var mapped = Mapping.Mapping.Requirements.Select(r => r.RequirementRef).ToHashSet(StringComparer.Ordinal);

        Mapping.ManualOnly.Where(m => mapped.Contains(m.Key.Value)).ShouldBeEmpty();
        Mapping.ManualOnly.Where(m => string.IsNullOrWhiteSpace(m.Source)).ShouldBeEmpty();
    }

    [Fact]
    public void Example_essential_eight_config_targets_the_ml2_audit_with_this_mapping()
    {
        var config = Config();

        config.Targets[0].ComplianceAssessmentRefId.ShouldBe("EX-E8-2026");
        Path.GetFileName(config.Targets[0].MappingPath).ShouldBe("essential-eight-2023.yaml");
    }

    /// <summary>The checks of every reference collector: Microsoft 365 and Sophos Central.</summary>
    private static IEnumerable<CheckId> ReferenceChecks()
    {
        var descriptions = Registry().Descriptions;
        return ReferenceTypes
            .Where(descriptions.ContainsKey)
            .SelectMany(type => descriptions[type].Checks)
            .Select(c => CheckId.Create(c).Value());
    }

    private static IEnumerable<CheckId> ExampleManualChecks() =>
        Config().Collectors
            .Where(c => c.Type == "manual")
            .SelectMany(c => c.Settings["checks"].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(c => CheckId.Create(c).Value());

    private static DeploymentConfig Config() =>
        ConfigLoader.Parse(File.ReadAllText(Path.Combine(Files, "examples", "example-essential-eight.yaml")), "examples/example-essential-eight.yaml", Registry().Descriptions).Value();

    private static CollectorRegistry Registry()
    {
        using var services = new ServiceCollection().AddLogging().AddBuiltInCollectors().BuildServiceProvider();
        return CollectorRegistry.FromServices(services).Value();
    }
}
