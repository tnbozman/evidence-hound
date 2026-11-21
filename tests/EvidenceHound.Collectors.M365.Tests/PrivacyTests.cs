using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.M365.Tests;

public class PrivacyTests
{
    private static JsonArray Items() =>
    [
        new JsonObject
        {
            ["id"] = "6a1f0c2e-0000-4000-8000-000000000001",
            ["userPrincipalName"] = "alex.example@example.com",
            ["displayName"] = "Alex Example",
            ["mail"] = "alex.example@example.com",
            ["otherMails"] = new JsonArray("alex@example.net"),
            ["accountEnabled"] = true,
        },
        new JsonObject
        {
            ["id"] = "policy-1",
            ["displayName"] = "Require MFA for all users",
            ["state"] = "enabled",
        },
        new JsonObject
        {
            ["id"] = "device-1",
            ["deviceName"] = "ALEX-LAPTOP",
            ["emailAddress"] = "alex.example@example.com",
            ["userDisplayName"] = "Alex Example",
            ["complianceState"] = "compliant",
        },
    ];

    [Fact]
    public void Upns_are_hashed_by_default()
    {
        var user = Privacy.Minimise(Items(), includeIdentities: false)[0]!;

        user["userPrincipalName"]!.GetValue<string>().ShouldBe(Privacy.Hash("alex.example@example.com"));
        user["userPrincipalName"]!.GetValue<string>().ShouldStartWith("sha256:");
        user["mail"]!.GetValue<string>().ShouldBe(Privacy.Hash("alex.example@example.com"));
        user["otherMails"]![0]!.GetValue<string>().ShouldBe(Privacy.Hash("alex@example.net"));
        user["displayName"]!.GetValue<string>().ShouldBe(Privacy.Hash("Alex Example"));
        user["id"]!.GetValue<string>().ShouldBe("6a1f0c2e-0000-4000-8000-000000000001");
        user["accountEnabled"]!.GetValue<bool>().ShouldBeTrue();
        user.ToJsonString().ShouldNotContain("alex", Case.Insensitive);
    }

    [Fact]
    public void Names_of_things_that_are_not_people_are_kept()
    {
        var policy = Privacy.Minimise(Items(), includeIdentities: false)[1]!;

        policy["displayName"]!.GetValue<string>().ShouldBe("Require MFA for all users");
    }

    [Fact]
    public void Device_names_and_their_users_are_hashed()
    {
        var device = Privacy.Minimise(Items(), includeIdentities: false)[2]!;

        device.ToJsonString().ShouldNotContain("alex", Case.Insensitive);
        device["complianceState"]!.GetValue<string>().ShouldBe("compliant");
    }

    [Fact]
    public void Identities_are_kept_when_included()
    {
        var items = Items();

        var kept = Privacy.Minimise(items, includeIdentities: true);

        kept.ToJsonString().ShouldBe(items.ToJsonString());
    }

    [Fact]
    public void Nested_objects_are_minimised()
    {
        JsonArray items = [new JsonObject { ["id"] = "a1", ["principal"] = new JsonObject { ["userPrincipalName"] = "alex.example@example.com", ["displayName"] = "Alex Example" } }];

        Privacy.Minimise(items, includeIdentities: false).ToJsonString().ShouldNotContain("alex", Case.Insensitive);
    }

    [Fact]
    public void Hash_is_stable_and_ignores_case()
    {
        Privacy.Hash("Alex.Example@Example.com").ShouldBe(Privacy.Hash("alex.example@example.com"));
        Privacy.Hash("alex.example@example.com").ShouldNotBe(Privacy.Hash("sam.example@example.com"));
    }

    [Fact]
    public void Minimising_does_not_change_the_input()
    {
        var items = Items();

        Privacy.Minimise(items, includeIdentities: false);

        items[0]!["userPrincipalName"]!.GetValue<string>().ShouldBe("alex.example@example.com");
    }
}
