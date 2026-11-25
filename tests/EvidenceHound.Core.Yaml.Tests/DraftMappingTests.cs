using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s14-01: a derived mapping marks each row <c>draft: true</c> until a person has reviewed it.</summary>
public class DraftMappingTests
{
    [Fact]
    public void Draft_true_marks_a_requirement_as_draft()
    {
        const string yaml = """
            framework_urn: urn:example:risk:framework:example-domains
            requirements:
              - urn: urn:example:risk:req_node:example-domains:bnd:pro-01:pro-01.1
                draft: true
                rule: all_pass
                checks: [identity.mfa.enforced]
              - urn: urn:example:risk:req_node:example-domains:idn:pro-01:pro-01.1
                rule: all_pass
                checks: [identity.mfa.enforced]
            """;

        var mapping = MappingLoader.Parse(yaml, "draft.yaml").Value().Mapping;

        mapping.Requirements.Select(r => r.Draft).ShouldBe([true, false]);
    }

    [Fact]
    public void Draft_must_be_true_or_false()
    {
        const string yaml = """
            framework_urn: urn:example:risk:framework:example-domains
            requirements:
              - urn: urn:example:risk:req_node:example-domains:bnd:pro-01:pro-01.1
                draft: maybe
                rule: all_pass
                checks: [identity.mfa.enforced]
            """;

        MappingLoader.Parse(yaml, "draft.yaml").Problems().Select(p => p.Code).ShouldBe(["mapping.draft_invalid"]);
    }

    [Fact]
    public void A_derived_mapping_written_as_yaml_loads_back_as_draft_rows()
    {
        var derived = new DerivedMapping(
            "urn:example:risk:framework:example-domains",
            [
                new DerivedRequirement(
                    "urn:example:risk:req_node:example-domains:bnd:pro-01:pro-01.1", "PRO-01.1", "bnd", ["OLD-1", "OLD-2"],
                    [CheckId("identity.mfa.enforced"), CheckId("identity.mfa.registered")]),
                new DerivedRequirement("urn:example:risk:req_node:example-domains:idn:pro-01:pro-01.1", "PRO-01.1", "idn", [], []),
            ]);

        var yaml = DerivedMappingWriter.Write(derived, "example-transition-mapping.yaml");
        var loaded = MappingLoader.Parse(yaml, "derived.yaml").Value();

        var row = loaded.Mapping.Requirements.ShouldHaveSingleItem();
        (row.Draft, row.Key.Kind, row.Checks.Count).ShouldBe((true, RequirementKeyKind.Urn, 2));
        yaml.ShouldContain("# PRO-01.1 (bnd), from OLD-1, OLD-2");
        yaml.ShouldContain("#   - urn: urn:example:risk:req_node:example-domains:idn:pro-01:pro-01.1   # PRO-01.1 (idn): no source checks");
    }

    private static Checks.CheckId CheckId(string value) => Checks.CheckId.Create(value).Value();
}
