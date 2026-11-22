using System.Text.Json.Nodes;
using EvidenceHound.Onboarding.Guard;

namespace EvidenceHound.Onboarding.Tests;

public class ResponseShapeTests
{
    [Fact]
    public void Keeps_enumerations_drops_identities_and_free_text()
    {
        var response = JsonNode.Parse("""
            {
              "items": [
                { "hostname": "fw-01", "status": "active", "managed": true, "note": "Installed in the comms room on the second floor", "owner": "sam@example.com", "os": { "version": "21.0.1" } },
                { "hostname": "fw-02", "status": "inactive", "managed": false, "note": "Spare unit kept in its box until needed", "owner": "alex@example.com", "os": { "version": "21.5.0" } }
              ],
              "pages": { "total": 1 }
            }
            """)!;

        var shape = ResponseShape.Of(response, ["hostname"]);

        var item = shape["items"]!["items"]!;
        shape["items"]!["type"]!.GetValue<string>().ShouldBe("array");
        shape["items"]!["count"]!.GetValue<int>().ShouldBe(2);
        Values(item["status"]).ShouldBe(["active", "inactive"]);
        Values(item["managed"]).ShouldBe(["false", "true"]);
        Values(item["os"]!["version"]).ShouldBe(["21.0.1", "21.5.0"]);
        item["hostname"]!.AsObject().ShouldNotContainKey("values");
        item["note"]!.AsObject().ShouldNotContainKey("values");
        item["owner"]!.AsObject().ShouldNotContainKey("values");
        shape["pages"]!["total"]!["type"]!.GetValue<string>().ShouldBe("number");
        var text = shape.ToJsonString();
        text.ShouldNotContain("fw-01");
        text.ShouldNotContain("example.com");
        text.ShouldNotContain("comms room");
    }

    private static string[] Values(JsonNode? field) => [.. field!["values"]!.AsArray().Select(v => v!.GetValue<string>())];
}
