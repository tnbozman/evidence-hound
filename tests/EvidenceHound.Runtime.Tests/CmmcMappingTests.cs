using EvidenceHound.Collectors;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.DependencyInjection;

namespace EvidenceHound.Runtime.Tests;

/// <summary>
/// s17-01: the CMMC Level 2 mapping, onto CISO Assistant's cmmc-2.0 library, from the checks Evidence Hound has today. Every practice is
/// mapped or listed as manual only, and a row claims the whole practice only where the 800-171 crosswalk links an equal or wider
/// Essential Eight requirement.
/// </summary>
public class CmmcMappingTests
{
    private static readonly string Files = Path.Combine(AppContext.BaseDirectory, "files");
    private static readonly string[] ReferenceTypes = ["m365", "vuln-scan"];
    private static readonly string[] ReferenceProfiles = ["sophos-central", "ciso-assistant"];

    private static LoadedMapping Mapping { get; } =
        MappingLoader.Parse(File.ReadAllText(Path.Combine(Files, "mappings", "cmmc-2.0.yaml")), "mappings/cmmc-2.0.yaml").Value();

    private static FrameworkSnapshot Snapshot { get; } =
        FrameworkSnapshotJson.Read(File.ReadAllText(Path.Combine(Files, "frameworks", "cmmc-2.0.json")), "frameworks/cmmc-2.0.json").Value();

    private static CrosswalkDefinition Crosswalk { get; } =
        CrosswalkLoader.Parse(
            File.ReadAllText(Path.Combine(Files, "mappings", "crosswalks", "essential-eight-2023-to-nist-sp-800-171-rev2.yaml")),
            "mappings/crosswalks/essential-eight-2023-to-nist-sp-800-171-rev2.yaml").Value();

    [Fact]
    public void All_requirement_refs_exist_in_library_snapshot()
    {
        Mapping.Mapping.FrameworkUrn.ShouldBe("urn:intuitem:risk:framework:cmmc-2.0");
        Snapshot.Version.ShouldBe(6);
        MappingValidator.Validate(Mapping, Snapshot).ShouldBeEmpty();
    }

    [Fact]
    public void No_collector_changes_needed()
    {
        var known = ReferenceChecks().Concat(ExampleManualSources.Checks()).Concat(ExampleManualSources.LibraryChecks("essentials")).ToHashSet();

        Mapping.Mapping.Requirements.SelectMany(r => r.Checks).Distinct().Where(c => !known.Contains(c)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_level_1_and_2_practice_has_checks_or_is_manual_only_with_a_source()
    {
        var named = Mapping.Mapping.Requirements.Select(r => r.RequirementRef).Concat(Mapping.ManualOnly.Select(m => m.Key.Value)).ToList();

        Snapshot.Nodes.Count.ShouldBe(110);
        named.ShouldBeUnique();
        Snapshot.Nodes.Select(n => n.RefId!).Order(StringComparer.Ordinal).ShouldBe(named.Order(StringComparer.Ordinal));
        Mapping.ManualOnly.Where(m => string.IsNullOrWhiteSpace(m.Source)).ShouldBeEmpty();
    }

    [Fact]
    public void A_row_claims_the_whole_practice_only_where_the_crosswalk_links_an_equal_or_wider_requirement()
    {
        var whole = Crosswalk.Links
            .Where(l => l.Relationship is CrosswalkRelationship.Equal or CrosswalkRelationship.Superset)
            .Select(l => l.TargetRef)
            .ToHashSet(StringComparer.Ordinal);

        Mapping.Mapping.Requirements
            .Where(r => !r.CoversPart && !whole.Contains(Nist171(r.RequirementRef)))
            .Select(r => r.RequirementRef)
            .ShouldBeEmpty();
    }

    [Fact]
    public void The_reference_mapping_has_no_draft_rows()
    {
        Mapping.Mapping.Requirements.Where(r => r.Draft).Select(r => r.RequirementRef).ShouldBeEmpty();
    }

    /// <summary>A CMMC practice's NIST SP 800-171 Rev 2 requirement: <c>AC.L2-3.1.3</c> is <c>3.1.3</c>.</summary>
    private static string Nist171(string practice) => practice[(practice.IndexOf('-', StringComparison.Ordinal) + 1)..];

    private static IEnumerable<CheckId> ReferenceChecks()
    {
        using var services = new ServiceCollection().AddLogging().AddBuiltInCollectors().BuildServiceProvider();
        var descriptions = CollectorRegistry.FromServices(services).Value().Descriptions;
        var profileChecks = ReferenceProfiles.SelectMany(profile => descriptions["rest"].ChecksFrom!(new CollectorSettings(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profile"] = profile,
            ["profiles_path"] = Path.Combine(Files, "profiles"),
        }))).ToList();
        return ReferenceTypes.SelectMany(type => descriptions[type].Checks).Concat(profileChecks).Select(c => CheckId.Create(c).Value());
    }
}
