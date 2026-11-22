using System.Text;
using System.Text.Json.Nodes;

namespace EvidenceHound.Collectors.Sophos.Tests;

/// <summary>Evidence hashing covers more than today's firewall list, so a later list (such as admins) is minimised too.</summary>
public class SophosEvidenceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task People_addresses_and_locations_are_minimised_in_any_list()
    {
        var item = new JsonObject
        {
            ["id"] = "00000000-0000-4000-8000-0000000000a1",
            ["macAddresses"] = new JsonArray("00-00-5E-00-53-01"),
            ["ipv4Addresses"] = new JsonArray("192.0.2.10"),
            ["associatedPerson"] = new JsonObject { ["name"] = "Sam Example", ["viaLogin"] = "example\\sam" },
            ["geoLocation"] = new JsonObject { ["latitude"] = -33.8 },
        };
        var collection = new SophosCollection(new SophosSource("things", "example/v1/things"), new JsonArray(item), 1);

        var text = Encoding.UTF8.GetString((await SophosEvidence.File(collection, includeIdentities: false).ReadAsync(Token)).Content.Span);

        text.ShouldNotContain("00-00-5E-00-53");
        text.ShouldNotContain("192.0.2.");
        text.ShouldNotContain("Sam Example");
        text.ShouldNotContain("example\\\\sam");
        text.ShouldNotContain("geoLocation");
        text.ShouldContain("00000000-0000-4000-8000-0000000000a1");
    }
}
