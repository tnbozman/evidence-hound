using EvidenceHound.Core.Risk;

namespace EvidenceHound.Core.Tests.Risk;

public class LibraryValidationTests
{
    private const string Urn = "urn:example:risk:library:example-risk";

    [Fact]
    public void Changed_content_without_version_bump_is_reported()
    {
        // CISO Assistant only re-publishes a stored library when its version goes up, so an edit that keeps the version is never seen.
        var problem = LibraryValidation.VersionBump(new LibraryVersion(Urn, 1, "aaa"), new LibraryVersion(Urn, 1, "bbb"));

        problem.ShouldNotBeNull().Code.ShouldBe("library.version_not_bumped");
        problem.Message.ShouldContain(Urn);
        problem.Message.ShouldContain("version 2");
    }

    [Fact]
    public void Same_content_or_a_raised_version_is_fine()
    {
        LibraryValidation.VersionBump(new LibraryVersion(Urn, 1, "aaa"), new LibraryVersion(Urn, 1, "aaa")).ShouldBeNull();
        LibraryValidation.VersionBump(new LibraryVersion(Urn, 2, "bbb"), new LibraryVersion(Urn, 1, "aaa")).ShouldBeNull();
        LibraryValidation.VersionBump(new LibraryVersion(Urn, 1, "aaa"), null).ShouldBeNull();
    }

    [Fact]
    public void Unknown_held_hash_is_not_reported() =>
        LibraryValidation.VersionBump(new LibraryVersion(Urn, 1, "aaa"), new LibraryVersion(Urn, 1, null)).ShouldBeNull();
}
