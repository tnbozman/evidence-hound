using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.M365.Tests;

public class GraphEvidenceTests
{
    private static readonly GraphCollection Users = new(
        new GraphRequest("v1.0/users?$select=id,userPrincipalName", "User.Read.All"),
        [new JsonObject { ["id"] = "u1", ["userPrincipalName"] = "alex.example@example.com" }, new JsonObject { ["id"] = "u2", ["userPrincipalName"] = "sam.example@example.com" }],
        Pages: 3);

    [Fact]
    public async Task One_file_holds_every_page_minimised()
    {
        var file = GraphEvidence.File("users.json", Users, includeIdentities: false);

        file.FileName.ShouldBe("users.json");
        file.MediaType.ShouldBe("application/json");
        var content = JsonNode.Parse((await file.ReadAsync(TestContext.Current.CancellationToken)).Content.Span)!;
        content["request"]!.GetValue<string>().ShouldBe("v1.0/users?$select=id,userPrincipalName");
        content["pages"]!.GetValue<int>().ShouldBe(3);
        content["identities"]!.GetValue<string>().ShouldBe("hashed");
        content["value"]!.AsArray().Count.ShouldBe(2);
        content.ToJsonString().ShouldNotContain("example.com");
    }

    [Fact]
    public async Task Content_is_the_same_every_run()
    {
        var first = await GraphEvidence.File("users.json", Users, includeIdentities: false).ReadAsync(TestContext.Current.CancellationToken);
        var second = await GraphEvidence.File("users.json", Users, includeIdentities: false).ReadAsync(TestContext.Current.CancellationToken);

        first.Content.ToArray().ShouldBe(second.Content.ToArray());
    }

    [Fact]
    public async Task Identities_are_marked_when_included()
    {
        var content = JsonNode.Parse((await GraphEvidence.File("users.json", Users, includeIdentities: true).ReadAsync(TestContext.Current.CancellationToken)).Content.Span)!;

        content["identities"]!.GetValue<string>().ShouldBe("included");
        content.ToJsonString().ShouldContain("alex.example@example.com");
    }
}
