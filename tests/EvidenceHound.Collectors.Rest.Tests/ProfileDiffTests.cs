using EvidenceHound.Collectors.Rest.Profiles;

namespace EvidenceHound.Collectors.Rest.Tests;

/// <summary><c>hound profile diff</c>: how a copied profile differs from the library version named in its <c>source</c>.</summary>
public sealed class ProfileDiffTests
{
    [Fact]
    public void Shows_changes_from_library_version()
    {
        const string library = "profile: example\nversion: \"1.0.0\"\nadmin_max: 2\nchecks: []\n";
        const string local = "profile: example\nversion: \"1.0.0\"\nadmin_max: 3\nchecks: []\nextra: true\n";

        var lines = ProfileDiff.Compare(library, local);

        lines.Where(l => l.Kind != DiffKind.Same).Select(l => l.ToString()).ShouldBe(["-3: admin_max: 2", "+3: admin_max: 3", "+5: extra: true"]);
    }

    [Fact]
    public void Same_text_has_no_changes()
    {
        ProfileDiff.Compare("a\nb\n", "a\r\nb\r\n").ShouldAllBe(l => l.Kind == DiffKind.Same);
    }

    [Theory]
    [InlineData("evidence-hound/profiles/example.yaml", "profiles/example.yaml")]
    [InlineData("profiles/example.yaml", "profiles/example.yaml")]
    public void Source_resolves_inside_the_library(string source, string expected)
    {
        ProfileDiff.LibraryFile("/lib", source).Value().ShouldBe(Path.Combine("/lib", expected));
    }

    [Theory]
    [InlineData("evidence-hound/../secrets.yaml")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public void Source_outside_the_library_is_refused(string source)
    {
        ProfileDiff.LibraryFile("/lib", source).Problems().ShouldHaveSingleItem().Code.ShouldBe("profile.source_invalid");
    }
}
