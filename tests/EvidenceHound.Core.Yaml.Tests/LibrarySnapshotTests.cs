using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Yaml.Tests;

public class LibrarySnapshotTests
{
    private const string Source = "example-risk-library.yaml";

    private static string RiskLibrary => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "files", "frameworks", Source));

    [Fact]
    public void Snapshot_lists_matrix_levels_and_threats()
    {
        var snapshot = RiskLibraryReader.Snapshot(RiskLibrary, Source).Value();

        snapshot.LibraryUrn.ShouldBe("urn:example:risk:library:example-risk");
        snapshot.Version.ShouldBe(1);
        var matrix = snapshot.Matrices.ShouldHaveSingleItem();
        matrix.Urn.ShouldBe("urn:example:risk:matrix:example-4x4");
        matrix.Probability.ShouldBe(["Rare", "Unlikely", "Possible", "Likely"]);
        matrix.Impact.ShouldBe(["Minor", "Moderate", "Major", "Severe"]);
        matrix.Risk.ShouldBe(["Low", "Medium", "High"]);
        snapshot.Threats.Select(t => (t.RefId, t.Urn)).ShouldBe([
            ("THR-01", "urn:example:risk:threat:example-risk:thr-01"),
            ("THR-02", "urn:example:risk:threat:example-risk:thr-02"),
        ]);

        var read = LibrarySnapshotJson.Read(LibrarySnapshotJson.Write(snapshot), "snapshot.json").Value();
        read.Matrices.Single().Risk.ShouldBe(matrix.Risk);
        read.Threats.Select(t => t.Name).ShouldBe(snapshot.Threats.Select(t => t.Name));
    }

    [Fact]
    public void Snapshot_keeps_the_libraries_a_library_brings_with_it()
    {
        // CISO Assistant loads a library's declared dependencies with it, which is how a framework brings a threat catalogue.
        const string yaml = """
            urn: urn:example:risk:library:example-framework
            version: 3
            packager: example
            dependencies:
            - urn:example:risk:library:example-risk
            objects:
              framework:
                urn: urn:example:risk:framework:example
            """;

        RiskLibraryReader.Snapshot(yaml, "x.yaml").Value().Dependencies.ShouldBe(["urn:example:risk:library:example-risk"]);
    }

    [Fact]
    public void Validate_reports_a_bad_matrix_with_its_source()
    {
        var broken = RiskLibrary.Replace("- [1, 2, 2, 2]", "- [1, 2, 2, 5]", StringComparison.Ordinal);

        var problems = RiskLibraryReader.Validate(broken, Source);

        problems.ShouldHaveSingleItem().Code.ShouldBe("library.matrix_grid_unknown_risk");
        problems[0].Message.ShouldStartWith(Source);
        RiskLibraryReader.Validate(RiskLibrary, Source).ShouldBeEmpty();
    }
}
