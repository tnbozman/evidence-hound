using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Rest.Engine;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Collectors.Rest.Tests;

public sealed class QueryEvidenceTests
{
    private static readonly Secret Key = new("engine-test-key-91be44");

    private static readonly string[] Identities = ["email", "hostname"];

    private static QueryResult.Collected Collected(params JsonObject[] items) =>
        new("users", [.. items], Pages: 2, Calls: 1, Request: "GET https://api.example.com/v1/users?key=${secrets.key}") { Identities = Identities };

    private static JsonNode Read(CollectedEvidence file) =>
        JsonNode.Parse(Encoding.UTF8.GetString(file.ReadAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult().Content.Span))!;

    [Fact]
    public void Identities_hashed_and_secrets_never_saved()
    {
        var users = Collected(new JsonObject { ["id"] = "u1", ["email"] = "alex@example.com", ["device"] = new JsonObject { ["hostname"] = "laptop-01.example.com" } });

        var file = QueryEvidence.File(users, includeIdentities: false, [Key]).Value();

        file.FileName.ShouldBe("users.json");
        var json = Read(file);
        json["request"]!.GetValue<string>().ShouldBe("GET https://api.example.com/v1/users?key=${secrets.key}");
        json["pages"]!.GetValue<int>().ShouldBe(2);
        json["item_count"]!.GetValue<int>().ShouldBe(1);
        json["identities"]!.GetValue<string>().ShouldBe("hashed");
        var item = json["items"]![0]!;
        item["id"]!.GetValue<string>().ShouldBe("u1");
        item["email"]!.GetValue<string>().ShouldStartWith("sha256:");
        item["device"]!["hostname"]!.GetValue<string>().ShouldStartWith("sha256:");
        json.ToJsonString().ShouldNotContain("alex@example.com");
    }

    [Fact]
    public void Identities_are_kept_when_included()
    {
        var file = QueryEvidence.File(Collected(new JsonObject { ["email"] = "alex@example.com" }), includeIdentities: true, [Key]).Value();

        Read(file)["items"]![0]!["email"]!.GetValue<string>().ShouldBe("alex@example.com");
        Read(file)["identities"]!.GetValue<string>().ShouldBe("included");
    }

    [Fact]
    public void A_file_holding_a_secret_in_use_is_refused()
    {
        var problem = QueryEvidence.File(Collected(new JsonObject { ["echo"] = "the key was engine-test-key-91be44" }), includeIdentities: true, [Key]).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("rest.secret_in_evidence");
        problem.Message.ShouldNotContain(Key.Reveal());
    }

    [Fact]
    public void Hash_is_stable_and_case_insensitive() =>
        QueryEvidence.Hash("Alex@Example.com").ShouldBe(QueryEvidence.Hash("alex@example.com"));
}
