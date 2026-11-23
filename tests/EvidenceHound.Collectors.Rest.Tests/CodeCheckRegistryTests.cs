using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Rest.Engine;
using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Collectors.Rest.Tests;

/// <summary>Code checks: named, pure functions compiled into hound, which a profile names with <c>code:</c>.</summary>
public sealed class CodeCheckRegistryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 2, 0, 0, TimeSpan.Zero);

    private static readonly string Admins = """
        [
          { "id": "adm-1", "roleAssignments": [{ "roleId": "r-super" }] },
          { "id": "adm-2", "roleAssignments": [{ "roleId": "r-super" }, { "roleId": "r-help" }] },
          { "id": "adm-3", "roleAssignments": [{ "roleId": "r-help" }] }
        ]
        """;

    private static readonly string Roles = """
        [
          { "id": "r-super", "name": "Super Admin", "type": "predefined" },
          { "id": "r-help", "name": "Helpdesk", "type": "predefined" },
          { "id": "r-fake", "name": "Super Admin", "type": "custom" }
        ]
        """;

    [Fact]
    public void Unknown_name_is_a_validation_problem()
    {
        var codes = Validate(CodeCheck("example.not_built_in"));

        codes.ShouldContain("profile.code_check_unknown");
    }

    [Fact]
    public void Built_in_names_are_valid()
    {
        CodeChecks.Names.ShouldContain("sophos.super_admin_count");
        Validate(CodeCheck("sophos.super_admin_count")).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("2", CheckOutcome.Pass)]
    [InlineData("1", CheckOutcome.Fail)]
    public void Super_admin_count_counts_admins_holding_the_predefined_role(string max, CheckOutcome expected)
    {
        var finding = Evaluate(Check(max), Rows(Admins), Rows(Roles));

        finding.Outcome.ShouldBe(expected);
        finding.Observation.ShouldStartWith($"2 of 3 Sophos Central admins hold Super Admin; at most {max} allowed.");
    }

    [Fact]
    public void Super_admin_count_without_the_predefined_role_is_unable_to_assess()
    {
        var finding = Evaluate(Check("2"), Rows(Admins), Rows("""[{ "id": "r-help", "name": "Helpdesk", "type": "predefined" }]"""));

        finding.Outcome.ShouldBe(CheckOutcome.Error);
        finding.Observation.ShouldStartWith("Unable to assess:");
    }

    [Fact]
    public void Code_check_over_a_failed_query_is_unable_to_assess_naming_it()
    {
        var sources = new Dictionary<string, RowSource>(StringComparer.Ordinal)
        {
            ["admins"] = new RowSource.Rows("admins", Rows(Admins), ["admins"], 0),
            ["roles"] = new RowSource.Failed("roles", "The API answered 500."),
        };

        var finding = CheckEvaluator.Evaluate(Check("2"), sources, Renderer(), Now);

        finding.Outcome.ShouldBe(CheckOutcome.Error);
        finding.Observation.ShouldContain("'roles'");
    }

    private static Finding Evaluate(Check check, JsonArray admins, JsonArray roles) =>
        CheckEvaluator.Evaluate(
            check,
            new Dictionary<string, RowSource>(StringComparer.Ordinal)
            {
                ["admins"] = new RowSource.Rows("admins", admins, ["admins"], 0),
                ["roles"] = new RowSource.Rows("roles", roles, ["roles"], 0),
            },
            Renderer(),
            Now);

    private static Check Check(string max) => ProfileParser.Parse(CodeCheck("sophos.super_admin_count", max), "code.yaml").Value().Checks[0];

    private static string CodeCheck(string name, string max = "${super_admin_max}") => $$"""
        profile: code
        version: "1.0.0"
        parameters:
          super_admin_max: { type: int, default: "2" }
        connection:
          base_url: https://api.example.com/
          allowed_hosts: [api.example.com]
          auth: { type: none }
        queries:
          admins:
            get: v1/admins
          roles:
            get: v1/roles
        checks:
          - id: network.management.super_admin_count
            over: [admins, roles]
            code: {{name}}
            with: { max: "{{max}}" }
        """;

    private static IReadOnlyList<string> Validate(string yaml) =>
        [.. ProfileValidator.Validate(ProfileParser.Parse(yaml, "code.yaml").Value(), RestCollector.Validation).Select(p => p.Code)];

    private static Renderer Renderer() => new(
        new Dictionary<string, string>(StringComparer.Ordinal) { ["super_admin_max"] = "2" },
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, Secret>(StringComparer.Ordinal));

    private static JsonArray Rows(string json) => JsonNode.Parse(json)!.AsArray();
}
