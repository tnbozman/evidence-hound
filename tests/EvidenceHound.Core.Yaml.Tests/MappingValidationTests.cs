using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary><c>hound mapping validate --framework</c>: a mapping checked offline against a framework snapshot.</summary>
public class MappingValidationTests
{
    private static readonly string Source = "mappings/test.yaml";
    private static readonly FrameworkSnapshot Snapshot = FrameworkLibraryReader.Snapshot(RepoFiles.RepeatedLibrary, "library.yaml").Value();

    [Fact]
    public void Ambiguous_ref_lists_matching_urns()
    {
        var problems = Validate("""
            framework_urn: urn:example:risk:framework:example-repeated
            requirements:
              - ref: ACT-02
                rule: all_pass
                checks: [backup.restore_tested]
              - ref: ACT-01
                rule: all_pass
                checks: [identity.mfa.admins]
            """);

        var problem = problems.ShouldHaveSingleItem();
        problem.Code.ShouldBe("mapping.ref_ambiguous");
        problem.Message.ShouldContain("line 6");
        problem.Message.ShouldContain("urn:example:risk:req_node:example-repeated:bnd:act-01");
        problem.Message.ShouldContain("urn:example:risk:req_node:example-repeated:idn:act-01");
        problem.Message.ShouldContain("rows: all");
    }

    [Fact]
    public void Repeated_ref_with_rows_all_is_valid()
    {
        Validate("""
            framework_urn: urn:example:risk:framework:example-repeated
            requirements:
              - ref: ACT-01
                rows: all
                rule: all_pass
                checks: [identity.mfa.admins]
            """).ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_urn_is_reported_with_line()
    {
        var problems = Validate("""
            framework_urn: urn:example:risk:framework:example-repeated
            requirements:
              - urn: urn:example:risk:req_node:example-repeated:idn:act-01
                rule: all_pass
                checks: [identity.mfa.admins]
              - urn: urn:example:risk:req_node:example-repeated:idn:act-99
                rule: all_pass
                checks: [identity.mfa.users]
            """);

        var problem = problems.ShouldHaveSingleItem();
        problem.Code.ShouldBe("mapping.urn_unknown");
        problem.Message.ShouldBe("mappings/test.yaml line 6: requirement urn 'urn:example:risk:req_node:example-repeated:idn:act-99' is not in framework urn:example:risk:framework:example-repeated.");
    }

    [Fact]
    public void Unknown_ref_is_reported_with_line()
    {
        var problems = Validate("""
            framework_urn: urn:example:risk:framework:example-repeated
            requirements:
              - ref: ACT-99
                rule: all_pass
                checks: [identity.mfa.admins]
            """);

        problems.ShouldHaveSingleItem().Code.ShouldBe("mapping.ref_unknown");
        problems[0].Message.ShouldContain("line 3");
    }

    [Fact]
    public void Manual_only_entries_are_checked_too()
    {
        var problems = Validate("""
            framework_urn: urn:example:risk:framework:example-repeated
            requirements: []
            manual_only:
              - ref: ACT-01
                source: MFA design review record.
            """);

        problems.ShouldHaveSingleItem().Code.ShouldBe("mapping.ref_ambiguous");
        problems[0].Message.ShouldContain("line 4");
    }

    [Fact]
    public void Mapping_for_another_framework_is_reported()
    {
        var problems = Validate("""
            framework_urn: urn:example:risk:framework:example-other
            requirements:
              - ref: ACT-02
                rule: all_pass
                checks: [backup.restore_tested]
            """);

        problems.ShouldHaveSingleItem().Code.ShouldBe("mapping.framework_mismatch");
    }

    private static IReadOnlyList<Results.Problem> Validate(string yaml) =>
        MappingValidator.Validate(MappingLoader.Parse(yaml, Source).Value(), Snapshot);
}
