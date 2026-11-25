using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s14-01: reads a CISO Assistant requirement mapping library, such as the Essential Eight to ASD Essentials transition mapping.</summary>
public class MappingLibraryReaderTests
{
    [Fact]
    public void Reads_frameworks_and_links_by_urn()
    {
        var library = MappingLibraryReader.Read(RepoFiles.TransitionMapping, "example-transition-mapping.yaml").Value();

        library.SourceFrameworkUrn.ShouldBe("urn:example:risk:framework:example-old");
        library.TargetFrameworkUrn.ShouldBe("urn:example:risk:framework:example-domains");
        library.Links.ShouldBe([
            new RequirementLink("urn:example:risk:req_node:example-old:mfa:old-1", "urn:example:risk:req_node:example-domains:bnd:pro-01:pro-01.1", CrosswalkRelationship.Intersect),
            new RequirementLink("urn:example:risk:req_node:example-old:mfa:old-2", "urn:example:risk:req_node:example-domains:bnd:pro-01:pro-01.1", CrosswalkRelationship.Subset),
        ]);
    }

    [Fact]
    public void A_framework_library_is_not_a_mapping_library()
    {
        var read = MappingLibraryReader.Read(RepoFiles.DomainsLibrary, "example-domains-library.yaml");

        read.Problems().Select(p => p.Code).ShouldBe(["mapping_library.no_mapping_set"]);
    }
}
