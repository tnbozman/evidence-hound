using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>How a mapping names its requirements: by URN, or by ref (optionally every row that has it).</summary>
public class MappingTests
{
    private static readonly string Source = "mappings/test.yaml";

    [Fact]
    public void Requirement_named_by_urn_is_accepted()
    {
        const string yaml = """
            framework_urn: urn:example:risk:framework:example-repeated
            requirements:
              - urn: urn:example:risk:req_node:example-repeated:idn:act-01
                rule: all_pass
                checks: [identity.mfa.admins]
              - ref: ACT-02
                rule: any_pass
                checks: [backup.restore_tested]
              - ref: ACT-01
                rows: all
                rule: any_pass
                checks: [identity.mfa.users]
            """;

        var mapping = MappingLoader.Parse(yaml, Source).Value().Mapping;

        mapping.Requirements.Select(r => r.Key).ShouldBe([
            RequirementKey.Urn("urn:example:risk:req_node:example-repeated:idn:act-01"),
            RequirementKey.Ref("ACT-02"),
            RequirementKey.Ref("ACT-01", allRows: true),
        ]);
    }

    [Fact]
    public void Requirement_needs_exactly_one_of_ref_and_urn()
    {
        const string yaml = """
            framework_urn: urn:example:risk:framework:example-repeated
            requirements:
              - ref: ACT-01
                urn: urn:example:risk:req_node:example-repeated:idn:act-01
                rule: all_pass
                checks: [identity.mfa.admins]
              - rule: all_pass
                checks: [identity.mfa.users]
            """;

        var problems = MappingLoader.Parse(yaml, Source).Problems();

        problems.Select(p => p.Code).ShouldBe(["mapping.requirement_name_invalid", "mapping.requirement_name_invalid"]);
        problems[0].Message.ShouldContain("line 3");
    }

    [Fact]
    public void Urn_must_start_with_urn()
    {
        const string yaml = """
            framework_urn: urn:example:risk:framework:example-repeated
            requirements:
              - urn: act-01
                rule: all_pass
                checks: [identity.mfa.admins]
            """;

        MappingLoader.Parse(yaml, Source).Problems().Select(p => p.Code).ShouldBe(["mapping.requirement_urn_invalid"]);
    }

    [Fact]
    public void Rows_only_takes_all_and_only_with_ref()
    {
        const string yaml = """
            framework_urn: urn:example:risk:framework:example-repeated
            requirements:
              - ref: ACT-01
                rows: some
                rule: all_pass
                checks: [identity.mfa.admins]
              - urn: urn:example:risk:req_node:example-repeated:idn:act-01
                rows: all
                rule: all_pass
                checks: [identity.mfa.users]
            """;

        MappingLoader.Parse(yaml, Source).Problems().Select(p => p.Code).ShouldBe(["mapping.rows_invalid", "mapping.rows_invalid"]);
    }

    [Fact]
    public void Loaded_mapping_keeps_each_requirement_line()
    {
        const string yaml = """
            framework_urn: urn:example:risk:framework:example-repeated
            requirements:
              - ref: ACT-02
                rule: all_pass
                checks: [backup.restore_tested]
              - ref: ACT-03
                rule: all_pass
                checks: [identity.unused_disabled]
            """;

        var loaded = MappingLoader.Parse(yaml, Source).Value();

        loaded.Source.ShouldBe(Source);
        loaded.Lines.ShouldBe([3, 6]);
    }

    [Fact]
    public void Manual_only_lists_requirements_with_their_suggested_source()
    {
        const string yaml = """
            framework_urn: urn:example:risk:framework:example-repeated
            requirements:
              - ref: ACT-02
                rule: all_pass
                checks: [backup.restore_tested]
            manual_only:
              - ref: ACT-01
                rows: all
                source: MFA design review record, attested by the identity owner.
              - urn: urn:example:risk:req_node:example-repeated:idn:act-03
                source: Quarterly account review.
            """;

        var loaded = MappingLoader.Parse(yaml, Source).Value();

        loaded.ManualOnly.ShouldBe([
            new ManualOnlyRequirement(RequirementKey.Ref("ACT-01", allRows: true), "MFA design review record, attested by the identity owner.", 7),
            new ManualOnlyRequirement(RequirementKey.Urn("urn:example:risk:req_node:example-repeated:idn:act-03"), "Quarterly account review.", 10),
        ]);
    }

    [Fact]
    public void Manual_only_entry_needs_a_source_and_known_keys()
    {
        const string yaml = """
            framework_urn: urn:example:risk:framework:example-repeated
            requirements: []
            manual_only:
              - ref: ACT-01
                rows: all
              - ref: ACT-02
                source: Restore test record.
                owner: Alex Example
            """;

        MappingLoader.Parse(yaml, Source).Problems().Select(p => p.Code).ShouldBe(["mapping.key_missing", "mapping.key_unknown"]);
    }
}
