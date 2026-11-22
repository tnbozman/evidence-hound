using EvidenceHound.Collectors.Rest.Profiles;

namespace EvidenceHound.Collectors.Rest.Tests;

public sealed class ProfileValidationTests
{
    private static readonly ValidationContext Context = new("1.0.0", ["example.code_check"]);

    private static IReadOnlyList<string> Codes(string yaml) =>
        [.. ProfileValidator.Validate(ProfileParser.Parse(yaml, "test.yaml").Value(), Context).Select(p => p.Code)];

    [Fact]
    public void Example_profile_is_valid() =>
        ProfileValidator.Validate(Profiles.Example, Context).ShouldBeEmpty();

    [Fact]
    public void Check_over_unknown_dataset_is_reported()
    {
        var problems = ProfileValidator.Validate(ProfileParser.Parse(Profiles.Minimal(checks: """
            checks:
              - id: example.thing.enabled
                over: widgets
                each: { field: $.enabled, equals: true }
                require: all
                when_empty: not_applicable
            """), "test.yaml").Value(), Context);

        var problem = problems.ShouldHaveSingleItem();
        problem.Code.ShouldBe("profile.reference_unknown");
        problem.Message.ShouldContain("widgets");
        problem.Message.ShouldContain("example.thing.enabled");
    }

    [Fact]
    public void Dataset_from_and_join_naming_nothing_are_reported() =>
        Codes(Profiles.Minimal(extra: """
            datasets:
              joined:
                from: nothing
                join:
                  - { with: nowhere, on: { left: $.id, right: $.id }, as: other }
            """)).Count(c => c == "profile.reference_unknown").ShouldBe(2);

    [Fact]
    public void Query_dependency_cycle_is_reported()
    {
        var yaml = Profiles.Minimal().Replace("    items: $.items\n", """
                items: $.items
              first:
                get: v1/first/${second_id}
                vars: { first_id: $.id }
              second:
                get: v1/second/${first_id}
                vars: { second_id: $.id }

            """, StringComparison.Ordinal);

        var problem = ProfileValidator.Validate(ProfileParser.Parse(yaml, "test.yaml").Value(), Context).ShouldHaveSingleItem();

        problem.Code.ShouldBe("profile.dependency_cycle");
        problem.Message.ShouldContain("first");
        problem.Message.ShouldContain("second");
    }

    [Fact]
    public void For_each_naming_itself_is_a_cycle() =>
        Codes(Profiles.Minimal().Replace("    items: $.items", "    items: $.items\n    for_each: things", StringComparison.Ordinal)).ShouldContain("profile.dependency_cycle");

    [Fact]
    public void Host_outside_allowed_hosts_is_reported()
    {
        var problems = ProfileValidator.Validate(ProfileParser.Parse(Profiles.Minimal().Replace("get: v1/things", "get: https://collect.example.net/v1/things", StringComparison.Ordinal), "test.yaml").Value(), Context);

        var problem = problems.ShouldHaveSingleItem();
        problem.Code.ShouldBe("profile.host_not_allowed");
        problem.Message.ShouldContain("collect.example.net");
    }

    [Fact]
    public void Auth_url_outside_allowed_hosts_is_reported() =>
        Codes(Profiles.Minimal().Replace("auth: { type: bearer, token: \"${secrets.token}\" }", "auth: { type: oauth2_client_credentials, token_url: \"https://login.example.net/token\", client_id: abc, client_secret: \"${secrets.token}\" }", StringComparison.Ordinal))
            .ShouldBe(["profile.host_not_allowed"]);

    [Fact]
    public void Wildcard_host_allows_subdomains_only() =>
        Codes(Profiles.Minimal()
            .Replace("allowed_hosts: [api.example.com]", "allowed_hosts: [\"*.example.com\"]", StringComparison.Ordinal)
            .Replace("get: v1/things", "get: https://eu.api.example.com/v1/things", StringComparison.Ordinal)).ShouldBeEmpty();

    [Fact]
    public void Check_without_when_empty_is_reported()
    {
        var codes = Codes(Profiles.Minimal(checks: """
            checks:
              - id: example.thing.enabled
                over: things
                each: { field: $.enabled, equals: true }
                require: all
            """));

        codes.ShouldBe(["profile.when_empty_missing"]);
    }

    [Fact]
    public void Unknown_parameter_and_secret_in_templates_are_reported() =>
        Codes(Profiles.Minimal().Replace("get: v1/things", "get: v1/${tenant}/things?key=${secrets.api_key}", StringComparison.Ordinal))
            .ShouldBe(["profile.parameter_unknown", "profile.secret_unknown"]);

    [Fact]
    public void Bad_path_syntax_is_reported() =>
        Codes(Profiles.Minimal().Replace("each: { field: $.enabled", "each: { field: \"enabled[\"", StringComparison.Ordinal)).ShouldBe(["profile.path_invalid"]);

    [Fact]
    public void Unknown_code_check_is_reported() =>
        Codes(Profiles.Minimal(checks: """
            checks:
              - id: example.thing.coded
                over: things
                code: example.not_built_in
            """)).ShouldBe(["profile.code_check_unknown"]);

    [Fact]
    public void Requires_newer_hound_is_reported() =>
        Codes(Profiles.Minimal(extra: "requires: { hound: \">=2.0\" }")).ShouldBe(["profile.requires_newer"]);

    [Fact]
    public void Reports_all_problems_not_just_the_first()
    {
        var yaml = Profiles.Minimal(extra: "requires: { hound: \">=9.0\" }", checks: """
            checks:
              - id: example.thing.enabled
                over: widgets
                each: { field: "$..enabled", equals: "${colour}" }
                require: all
              - id: example.thing.coded
                over: things
                code: example.unknown
            """).Replace("get: v1/things", "get: https://elsewhere.example.net/things", StringComparison.Ordinal);

        Codes(yaml).Order(StringComparer.Ordinal).ShouldBe(
        [
            "profile.code_check_unknown", "profile.host_not_allowed", "profile.parameter_unknown", "profile.path_invalid", "profile.reference_unknown",
            "profile.requires_newer", "profile.when_empty_missing",
        ]);
    }

    [Fact]
    public void Instance_missing_required_parameter_or_secret_and_unknown_setting_are_reported()
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["profile"] = "example", ["supported_os"] = "15", ["colour"] = "blue" };

        var codes = ProfileValidator.ValidateInstance(Profiles.Example, settings, []).Select(p => p.Code).Order(StringComparer.Ordinal).ToArray();

        codes.ShouldBe(["profile.parameter_missing", "profile.secret_missing", "profile.setting_unknown"]);
    }

    [Fact]
    public void Instance_parameter_of_the_wrong_type_is_reported()
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["client_id"] = "abc", ["supported_os"] = "15", ["admin_max"] = "two", ["stale_after"] = "a month" };

        var problems = ProfileValidator.ValidateInstance(Profiles.Example, settings, ["client_secret"]);

        problems.Select(p => p.Code).ShouldBe(["profile.parameter_invalid", "profile.parameter_invalid"]);
    }
}
