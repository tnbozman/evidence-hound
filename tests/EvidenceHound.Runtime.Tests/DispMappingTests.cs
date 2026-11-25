using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Yaml;

namespace EvidenceHound.Runtime.Tests;

/// <summary>
/// s14-02: the DISP membership readiness mapping reuses evidence: the Essential Eight checks for the cyber area, supplier records for the
/// supply chain and the incident register for reporting; manual sources cover what no system holds.
/// </summary>
public class DispMappingTests
{
    private static readonly string Files = Path.Combine(AppContext.BaseDirectory, "files");

    private static LoadedMapping Mapping { get; } = Load("disp.yaml");

    private static LoadedMapping EssentialEight { get; } = Load("essential-eight-2023.yaml");

    private static FrameworkSnapshot Snapshot { get; } = Read("disp.json");

    private static FrameworkSnapshot EssentialEightSnapshot { get; } = Read("essential-eight-2023.json");

    [Fact]
    public void Cyber_area_reuses_essential_eight_checks()
    {
        // One DISP cyber requirement per Essential Eight strategy, named as the strategy, with every check the Essential Eight mapping
        // gives that strategy's maturity level 1 and 2 rows.
        var cyber = Snapshot.Nodes.Where(n => n.RefId!.StartsWith("DISP-CYB", StringComparison.Ordinal)).ToList();
        cyber.Count.ShouldBe(8);
        foreach (var strategy in EssentialEightSnapshot.Parents)
        {
            var node = cyber.Single(n => n.Title.StartsWith(strategy.Name, StringComparison.Ordinal));
            var expected = EssentialEight.Mapping.Requirements
                .Where(r => EssentialEightSnapshot.Resolve(r.Key).Any(n => n.ParentUrn == strategy.Urn && n.Groups.Intersect(["1", "2"]).Any()))
                .SelectMany(r => r.Checks)
                .ToHashSet();
            var mapped = Mapping.Mapping.Requirements.Single(r => r.RequirementRef == node.RefId);
            mapped.Checks.ToHashSet().SetEquals(expected).ShouldBeTrue(node.RefId);
            mapped.Rule.ShouldBe(MappingRule.AllPass);
        }
    }

    [Fact]
    public void Supply_chain_and_reporting_reuse_supplier_records_and_the_incident_register()
    {
        Checks("DISP-GOV-05").ShouldBe(["supplier.assessments_current", "supplier.contracts_current"]);
        Checks("DISP-INF-02").ShouldContain("incident.reported_to_asd");
    }

    [Fact]
    public void Every_requirement_is_mapped_or_manual_only_and_exists_in_the_library()
    {
        MappingValidator.Validate(Mapping, Snapshot).ShouldBeEmpty();
        var named = Mapping.Mapping.Requirements.Select(r => r.RequirementRef).Concat(Mapping.ManualOnly.Select(m => m.Key.Value)).ToHashSet(StringComparer.Ordinal);
        Snapshot.Nodes.Select(n => n.RefId!).Where(r => !named.Contains(r)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_disp_template_check_is_mapped()
    {
        var mapped = Mapping.Mapping.Requirements.SelectMany(r => r.Checks).ToHashSet();

        ExampleManualSources.LibraryChecks("disp").Where(c => !mapped.Contains(c)).ShouldBeEmpty();
    }

    private static List<string> Checks(string refId) => [.. Mapping.Mapping.Requirements.Single(r => r.RequirementRef == refId).Checks.Select(c => c.Value)];

    private static LoadedMapping Load(string name) =>
        MappingLoader.Parse(File.ReadAllText(Path.Combine(Files, "mappings", name)), $"mappings/{name}").Value();

    private static FrameworkSnapshot Read(string name) =>
        FrameworkSnapshotJson.Read(File.ReadAllText(Path.Combine(Files, "frameworks", name)), $"frameworks/{name}").Value();
}
