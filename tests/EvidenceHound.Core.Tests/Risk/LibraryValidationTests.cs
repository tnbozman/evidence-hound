using EvidenceHound.Core.Risk;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Tests.Risk;

public class LibraryValidationTests
{
    private static readonly string Urn = "urn:example:risk:library:example-risk";

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

    [Fact]
    public void Refused_names_each_custom_library_changed_without_a_version_bump()
    {
        var file = new ScopeFile("/repo/libraries/example-risk.yaml", "aaa") { Content = "c-file" };
        var declared = new ScopeObject(ScopeKind.Framework, Urn, new Dictionary<string, string> { ["version"] = "1" }) { Files = [file] };
        var builtIn = new ScopeObject(ScopeKind.Framework, "urn:intuitem:risk:framework:essential-eight-2023", new Dictionary<string, string> { ["version"] = "3" });
        ActualObject held = new(new ScopeObject(ScopeKind.Framework, Urn, new Dictionary<string, string> { ["version"] = "1", ["content"] = "c-stored" }), null);
        ActualObject heldBuiltIn = new(new ScopeObject(ScopeKind.Framework, builtIn.Key, new Dictionary<string, string> { ["version"] = "3", ["content"] = "c-other" }), null);

        var refused = LibraryValidation.Refused([declared, builtIn], [held, heldBuiltIn]);

        var problem = refused.ShouldHaveSingleItem();
        (problem.Code, problem.At.File).ShouldBe(("library.version_not_bumped", file.Path));
        LibraryValidation.Refused([declared], [held with { Held = held.Held with { Fields = new Dictionary<string, string> { ["version"] = "1", ["content"] = "c-file" } } }]).ShouldBeEmpty();
    }
}
