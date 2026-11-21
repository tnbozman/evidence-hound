using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Yaml.Tests;

public class MappingLoaderTests
{
    private static readonly string Source = "mappings/test.yaml";

    [Fact]
    public void Loads_valid_example_mapping()
    {
        var loaded = MappingLoader.Parse(RepoFiles.ExampleMapping, "mappings/example.yaml").Value();

        loaded.Mapping.FrameworkUrn.ShouldStartWith("urn:");
        loaded.Mapping.Requirements.Count.ShouldBeGreaterThanOrEqualTo(3);
        loaded.Mapping.Requirements.Select(r => r.Rule).Distinct().Count().ShouldBe(3);
        loaded.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Parses_every_rule_and_keeps_order()
    {
        const string yaml = """
            framework_urn: urn:example:risk:library:example-baseline
            requirements:
              - ref: "1.1"
                rule: all_pass
                checks: [identity.mfa.admins, identity.mfa.users]
              - ref: "1.2"
                rule: any_pass
                checks: [backup.restore_tested]
              - ref: "1.3"
                rule: threshold
                threshold: 2
                checks: [patching.os.critical, patching.apps.critical, patching.os.supported]
            """;

        var mapping = MappingLoader.Parse(yaml, Source).Value().Mapping;

        mapping.FrameworkUrn.ShouldBe("urn:example:risk:library:example-baseline");
        mapping.Requirements.Select(r => r.RequirementRef).ShouldBe(["1.1", "1.2", "1.3"]);
        mapping.Requirements.Select(r => r.Rule).ShouldBe([MappingRule.AllPass, MappingRule.AnyPass, MappingRule.Threshold(2)]);
        mapping.Requirements[0].Checks.Select(c => c.Value).ShouldBe(["identity.mfa.admins", "identity.mfa.users"]);
    }

    [Fact]
    public void Duplicate_requirement_ref_fails_with_line_number()
    {
        const string yaml = """
            framework_urn: urn:example:risk:library:example-baseline
            requirements:
              - ref: "1.1"
                rule: all_pass
                checks: [identity.mfa.admins]
              - ref: "1.1"
                rule: any_pass
                checks: [identity.mfa.users]
            """;

        var problem = MappingLoader.Parse(yaml, Source).Problems().Single();

        problem.Code.ShouldBe("mapping.duplicate_requirement_ref");
        problem.Message.ShouldContain($"{Source} line 6");
        problem.Message.ShouldContain("'1.1'");
        problem.Message.ShouldContain("line 3");
    }

    [Fact]
    public void Unknown_rule_fails()
    {
        const string yaml = """
            framework_urn: urn:example:risk:library:example-baseline
            requirements:
              - ref: "1.1"
                rule: most_pass
                checks: [identity.mfa.admins]
            """;

        var problem = MappingLoader.Parse(yaml, Source).Problems().Single();

        problem.Code.ShouldBe("mapping.rule_unknown");
        problem.Message.ShouldContain($"{Source} line 4");
        problem.Message.ShouldContain("most_pass");
    }

    [Fact]
    public void Every_problem_is_reported_with_its_line()
    {
        const string yaml = """
            framework_urn: urn:example:risk:library:example-baseline
            requirements:
              - ref: "1.1"
                rule: threshold
                checks: [identity.mfa.admins]
              - ref: "1.2"
                rule: all_pass
                checks: [Identity.MFA]
              - ref: "1.3"
                rule: all_pass
                colour: blue
                checks: [identity.mfa.admins]
            """;

        var problems = MappingLoader.Parse(yaml, Source).Problems();

        problems.Select(p => p.Code).ShouldBe(["mapping.threshold_missing", "check_id.invalid", "mapping.key_unknown"]);
        problems.Select(p => p.Message).ShouldAllBe(m => m.StartsWith($"{Source} line ", StringComparison.Ordinal));
        problems[1].Message.ShouldContain("line 8");
        problems[2].Message.ShouldContain("line 11");
    }

    [Theory]
    [InlineData("framework_urn: [", "mapping.yaml_invalid")]
    [InlineData("", "mapping.document_empty")]
    [InlineData("- a\n- b\n", "mapping.document_not_a_map")]
    [InlineData("requirements: []\n", "mapping.key_missing")]
    [InlineData("framework_urn: urn:example:x\n", "mapping.key_missing")]
    [InlineData("framework_urn: urn:example:x\nrequirements: {}\n", "mapping.requirements_not_a_list")]
    public void Malformed_documents_fail(string yaml, string code)
    {
        MappingLoader.Parse(yaml, Source).Problems()[0].Code.ShouldBe(code);
    }

    [Fact]
    public void Unknown_check_ids_are_allowed_with_a_warning()
    {
        const string yaml = """
            framework_urn: urn:example:risk:library:example-baseline
            requirements:
              - ref: "1.1"
                rule: all_pass
                checks: [identity.mfa.admins, identity.mfa.contractors]
            """;
        var known = new HashSet<CheckId> { CheckId.Create("identity.mfa.admins").Value() };

        var loaded = MappingLoader.Parse(yaml, Source, known).Value();

        loaded.Mapping.Requirements.Single().Checks.Count.ShouldBe(2);
        loaded.Warnings.Single().ShouldBe($"{Source} line 5: check 'identity.mfa.contractors' is not a known check.");
    }

    [Theory]
    [InlineData("- {ref: '1', rule: all_pass, threshold: 2, checks: [a.b]}", "mapping.threshold_unexpected")]
    [InlineData("- {ref: '1', rule: threshold, threshold: two, checks: [a.b]}", "mapping.threshold_invalid")]
    [InlineData("- {ref: '1', rule: threshold, threshold: 3, checks: [a.b]}", "mapping.threshold_out_of_range")]
    [InlineData("- just text", "mapping.requirement_not_a_map")]
    [InlineData("- {ref: '1', rule: all_pass, checks: a.b}", "mapping.checks_not_a_list")]
    [InlineData("- {ref: '1', rule: all_pass}", "mapping.key_missing")]
    [InlineData("- {ref: [x], rule: all_pass, checks: [a.b]}", "mapping.value_invalid")]
    [InlineData("- {ref: '1', rule: all_pass, checks: [{a: b}]}", "check_id.invalid")]
    public void Requirement_problems_name_their_line(string requirement, string code)
    {
        var yaml = $"framework_urn: urn:example:x\nrequirements:\n  {requirement}\n";

        var problem = MappingLoader.Parse(yaml, Source).Problems().Single();

        problem.Code.ShouldBe(code);
        problem.Message.ShouldStartWith($"{Source} line 3: ");
    }

    [Fact]
    public void Framework_urn_that_is_not_a_urn_fails_at_its_line()
    {
        var problem = MappingLoader.Parse("requirements: []\nframework_urn: example\n", Source).Problems().Single();

        problem.Code.ShouldBe("mapping.framework_urn_invalid");
        problem.Message.ShouldStartWith($"{Source} line 2: ");
    }

    [Fact]
    public void Empty_document_names_the_file_without_a_line()
    {
        MappingLoader.Parse("# only a comment\n", Source).Problems().Single().Message.ShouldBe($"{Source}: the mapping file is empty.");
    }
}
