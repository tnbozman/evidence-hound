using System.Text.Json.Nodes;
using EvidenceHound.Core.Risk;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>
/// A custom library's objects, digested the same way from the repo's YAML file and from the content CISO Assistant stores, so a plan can tell
/// that a file changed without a version bump (s15-01). CISO Assistant v4.3.4 shows no checksum, only the stored objects as JSON.
/// </summary>
public class LibraryContentTests
{
    private const string Yaml = """
        urn: urn:example:risk:library:example-content
        version: 1
        objects:
          risk_matrix:
            - urn: urn:example:risk:matrix:example-2x2
              name: Example 2x2
              description: "Two levels: quoted, with a colon."
              grid:
                - [0, 1]
                - [1, 1]
              is_enabled: true
              annotation: ~
              order: 1.50
          threats:
            - urn: urn:example:risk:threat:example-content:thr-01
              ref_id: "01"
              name: Loss of the only backup administrator
        """;

    // As CISO Assistant serves stored-libraries/<id>/content/: the YAML loaded by Python, then written as JSON, keys in another order.
    private const string Stored = """
        {"threats": [{"name": "Loss of the only backup administrator", "ref_id": "01", "urn": "urn:example:risk:threat:example-content:thr-01"}],
         "risk_matrix": [{"urn": "urn:example:risk:matrix:example-2x2", "name": "Example 2x2", "description": "Two levels: quoted, with a colon.",
           "grid": [[0, 1], [1, 1]], "is_enabled": true, "annotation": null, "order": 1.5}]}
        """;

    [Fact]
    public void A_library_files_objects_digest_as_ciso_assistant_stores_them() =>
        RiskLibraryReader.ContentDigest(Yaml).ShouldBe(LibraryContent.Digest(JsonNode.Parse(Stored)));

    [Theory]
    [InlineData("name: Loss of the only backup administrator", "name: Loss of both backup administrators")]
    [InlineData("- [1, 1]", "- [1, 0]")]
    [InlineData("ref_id: \"01\"", "ref_id: \"02\"")]
    public void A_changed_object_changes_the_digest(string from, string to) =>
        RiskLibraryReader.ContentDigest(Yaml.Replace(from, to, StringComparison.Ordinal)).ShouldNotBe(LibraryContent.Digest(JsonNode.Parse(Stored)));

    [Fact]
    public void A_change_outside_the_objects_leaves_the_digest() =>
        RiskLibraryReader.ContentDigest(Yaml.Replace("version: 1", "version: 2", StringComparison.Ordinal)).ShouldBe(RiskLibraryReader.ContentDigest(Yaml));
}
