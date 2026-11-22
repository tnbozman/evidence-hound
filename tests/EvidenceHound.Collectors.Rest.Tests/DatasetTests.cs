using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Rest.Engine;
using EvidenceHound.Collectors.Rest.Profiles;

namespace EvidenceHound.Collectors.Rest.Tests;

/// <summary>Extract phase: datasets built from the snapshot, never from part of their inputs.</summary>
public sealed class DatasetTests
{
    private const string Queries = """
        profile: datasets
        version: "1.0.0"
        connection:
          base_url: https://api.example.com/
          allowed_hosts: [api.example.com]
          auth: { type: none }
        queries:
          things:
            get: v1/things
          users:
            get: v1/users
            items: $.users
            identities: [email]
          roles:
            get: v1/roles
            items: $.roles
        """;

    [Fact]
    public void Join_attaches_matching_rows_and_counts_unmatched()
    {
        var profile = Build("""
            datasets:
              user_roles:
                from: users
                join:
                  - with: roles
                    on: { left: "$.roleIds[*]", right: $.id }
                    as: roles
            """);

        var rows = Extract(profile, Users(), Roles()).ShouldBeOfType<RowSource.Rows>();

        rows.Items.Count.ShouldBe(2);
        rows.Items[0]!["roles"]!.AsArray().Select(r => r!["name"]!.GetValue<string>()).ShouldBe(["Owner"]);
        rows.Items[1]!["roles"]!.AsArray().ShouldBeEmpty();
        rows.Unmatched.ShouldBe(1);
        rows.Queries.ShouldBe(["users", "roles"]);
    }

    [Fact]
    public void Optional_join_does_not_count_unmatched()
    {
        var profile = Build("""
            datasets:
              user_roles:
                from: users
                join:
                  - with: roles
                    on: { left: "$.roleIds[*]", right: $.id }
                    as: roles
                    optional: true
            """);

        Extract(profile, Users(), Roles()).ShouldBeOfType<RowSource.Rows>().Unmatched.ShouldBe(0);
    }

    [Fact]
    public void Failed_input_query_fails_the_dataset_with_its_name()
    {
        var profile = Build("""
            datasets:
              user_roles:
                from: users
                join:
                  - with: roles
                    on: { left: "$.roleIds[*]", right: $.id }
                    as: roles
                    optional: true
            """);

        var failed = Extract(profile, Users(), new QueryResult.Failed("roles", "The API answered 500.")).ShouldBeOfType<RowSource.Failed>();

        failed.Reason.ShouldContain("'roles'");
        failed.Reason.ShouldContain("The API answered 500.");
    }

    [Fact]
    public void Unavailable_input_query_makes_the_dataset_unavailable()
    {
        var profile = Build("""
            datasets:
              active:
                from: users
                where: { field: $.status, equals: active }
            """);

        Extract(profile, new QueryResult.Unavailable("users", "No licence (403)."), Roles())
            .ShouldBeOfType<RowSource.Unavailable>().Reason.ShouldContain("No licence");
    }

    [Fact]
    public void Where_and_fields_pick_rows_and_rename_values()
    {
        var profile = Build("""
            datasets:
              active:
                from: users
                where: { field: $.status, equals: active }
                fields:
                  id: $.id
                  mfa: $.mfa.enabled
                  role_ids: "$.roleIds[*]"
                  nickname: $.nickname
            """);

        var rows = Extract(profile, Users(), Roles()).ShouldBeOfType<RowSource.Rows>();

        rows.Items.Count.ShouldBe(1);
        rows.Items[0]!.ToJsonString().ShouldBe("""{"id":"usr-0001","mfa":true,"role_ids":["role-owner"]}""");
    }

    [Fact]
    public void Flatten_gives_one_row_per_element_with_its_parent()
    {
        var profile = Build("""
            datasets:
              assignments:
                from: users
                flatten: "$.roleIds"
                fields:
                  user: $.parent.id
                  role: $.value
            """);

        var rows = Extract(profile, Users(), Roles()).ShouldBeOfType<RowSource.Rows>();

        rows.Items.Select(r => r!.ToJsonString()).ShouldBe(["""{"user":"usr-0001","role":"role-owner"}""", """{"user":"usr-0002","role":"role-gone"}"""]);
    }

    [Fact]
    public void Identities_stay_hashed_in_rows()
    {
        var profile = Build("""
            datasets:
              emails:
                from: users
                fields:
                  email: $.email
            """);

        var rows = Extract(profile, Users(), Roles()).ShouldBeOfType<RowSource.Rows>();

        rows.Items[0]!["email"]!.GetValue<string>().ShouldBe(QueryEvidence.Hash("alex@example.com"));
    }

    [Fact]
    public void A_query_read_directly_is_its_items()
    {
        var sources = Extraction.Build(Build(""), Snapshot(Users(), Roles()), includeIdentities: true);

        var roles = sources["roles"].ShouldBeOfType<RowSource.Rows>();
        roles.Items.Count.ShouldBe(1);
        roles.Queries.ShouldBe(["roles"]);
    }

    private static Profile Build(string datasets) =>
        ProfileParser.Parse(Queries + "\n" + datasets, "datasets.yaml").Value();

    private static RowSource Extract(Profile profile, QueryResult users, QueryResult roles) =>
        Extraction.Build(profile, Snapshot(users, roles), includeIdentities: false)[profile.Datasets[0].Name];

    private static Dictionary<string, QueryResult> Snapshot(QueryResult users, QueryResult roles) => new(StringComparer.Ordinal)
    {
        ["things"] = new QueryResult.Collected("things", [], 1, 1, "GET v1/things"),
        ["users"] = users,
        ["roles"] = roles,
    };

    private static QueryResult.Collected Users() => new(
        "users",
        JsonNode.Parse("""
            [
              { "id": "usr-0001", "email": "alex@example.com", "status": "active", "mfa": { "enabled": true }, "roleIds": ["role-owner"] },
              { "id": "usr-0002", "email": "sam@example.com", "status": "suspended", "mfa": { "enabled": false }, "roleIds": ["role-gone"] }
            ]
            """)!.AsArray(),
        1,
        1,
        "GET v1/users")
    { Identities = ["email"] };

    private static QueryResult.Collected Roles() =>
        new("roles", JsonNode.Parse("""[{ "id": "role-owner", "name": "Owner" }]""")!.AsArray(), 1, 1, "GET v1/roles");
}
