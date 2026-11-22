using EvidenceHound.Collectors.Rest.Profiles;

namespace EvidenceHound.Collectors.Rest.Tests;

public sealed class ProfileParserTests
{
    [Fact]
    public void Reads_example_profile_into_records()
    {
        var profile = ProfileParser.Parse(Profiles.ExampleYaml, "example.yaml").Value();

        profile.Name.ShouldBe("example");
        profile.Version.ShouldBe("1.0.0");
        profile.Source.ShouldBe("evidence-hound/profiles/example.yaml");
        profile.Requires.ShouldBe(">=0.1");
        profile.Parameters.Select(p => p.Name).ShouldBe(["client_id", "base_url", "token_url", "supported_os", "stale_after", "admin_max"]);
        profile.Parameters.Single(p => p.Name == "admin_max").ShouldBe(new ParameterSpec("admin_max", ParameterType.WholeNumber, Required: false, Default: "2", "Most users allowed to hold the Owner role."));
        profile.Secrets.ShouldHaveSingleItem().ShouldBe(new SecretSpec("client_secret", Required: true, "Client secret of the read-only API client."));

        profile.Connection.BaseUrl.ShouldBe("${base_url}");
        profile.Connection.AllowedHosts.ShouldBe(["api.example.com", "login.example.com", "${base_url}", "${token_url}"]);
        var auth = profile.Connection.Auth.ShouldBeOfType<OAuthClientCredentials>();
        auth.TokenUrl.ShouldBe("${token_url}");
        auth.ClientSecret.ShouldBe("${secrets.client_secret}");
        auth.Scope.ShouldBe("read");
        profile.Connection.RateLimit.ShouldBe(new RateLimit(5, 3));

        var whoami = profile.Bootstrap.ShouldHaveSingleItem();
        whoami.Name.ShouldBe("whoami");
        whoami.Vars["org_id"].ShouldBe("$.organisation.id");

        profile.Queries.Select(q => q.Name).ShouldBe(["devices", "users", "roles"]);
        var devices = profile.Queries[0];
        devices.Method.ShouldBe("GET");
        devices.Path.ShouldBe("v1/orgs/${org_id}/devices");
        devices.Paging.ShouldBe(new NextLinkPaging("$.next"));
        devices.Items.ShouldBe("$.items");
        devices.Identities.ShouldBe(["hostname", "serial", "owner"]);
        profile.Queries[1].Paging.ShouldBe(new OffsetPaging("offset", "limit", 100, "$.total"));

        var dataset = profile.Datasets.ShouldHaveSingleItem();
        dataset.Name.ShouldBe("user_roles");
        dataset.From.ShouldBe("users");
        dataset.Joins.ShouldHaveSingleItem().ShouldBe(new Join("roles", "$.roleIds[*]", "$.id", "roles", Optional: true));
        dataset.Where.ShouldHaveSingleItem().ShouldBe(new FieldTest("$.status", TestOperator.EqualTo, ["active"]));
        dataset.Fields.Keys.ShouldBe(["id", "mfa", "role_names"]);

        profile.Checks.Select(c => c.Id).ShouldBe(["example.device.os_supported", "example.device.recently_seen", "example.identity.mfa_users", "example.identity.owner_count"]);
        var os = profile.Checks[0];
        os.Over.ShouldBe(["devices"]);
        os.Each.ShouldBe(new FieldTest("$.os.version", TestOperator.VersionIn, ["${supported_os}"]));
        os.Require.ShouldBe(new RequireAll());
        os.WhenEmpty.ShouldBe(EmptyResult.NotApplicable);
        profile.Checks[1].Require.ShouldBe(new RequireRatio(0.95, 0.8));
        profile.Checks[3].Require.ShouldBe(new RequireCountAtMost("${admin_max}"));
        profile.Checks[3].Where.ShouldHaveSingleItem().Operator.ShouldBe(TestOperator.AnyMatches);
    }

    [Fact]
    public void Missing_and_negate_are_read_from_a_test()
    {
        var yaml = Profiles.Minimal(checks: """
            checks:
              - id: example.thing.locked
                over: things
                each: { field: $.lockedAt, exists: true, missing: fail, negate: true }
                require: none
                when_empty: unable_to_assess
            """);

        var check = ProfileParser.Parse(yaml, "minimal.yaml").Value().Checks.ShouldHaveSingleItem();

        check.Each.ShouldBe(new FieldTest("$.lockedAt", TestOperator.Exists, ["true"]) { Missing = MissingRule.Fail, Negate = true });
        check.Require.ShouldBe(new RequireNone());
        check.WhenEmpty.ShouldBe(EmptyResult.UnableToAssess);
    }

    [Fact]
    public void Not_yaml_is_a_problem_with_its_line()
    {
        var problem = ProfileParser.Parse("profile: [unclosed", "broken.yaml").Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("profile.yaml_invalid");
        problem.Message.ShouldStartWith("broken.yaml line ");
    }

    [Fact]
    public void Unknown_keys_and_operators_are_reported_together()
    {
        var yaml = Profiles.Minimal(extra: "colour: blue", checks: """
            checks:
              - id: example.thing.enabled
                over: things
                each: { field: $.enabled, roughly: true }
                require: all
                when_empty: not_applicable
            """);

        var codes = ProfileParser.Parse(yaml, "minimal.yaml").Problems().Select(p => p.Code).ToArray();

        codes.ShouldContain("profile.key_unknown");
        codes.ShouldContain("profile.operator_unknown");
    }
}
