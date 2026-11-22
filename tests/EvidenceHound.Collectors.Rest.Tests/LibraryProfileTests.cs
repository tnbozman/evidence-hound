using EvidenceHound.Collectors.Rest.Engine;
using EvidenceHound.Collectors.Rest.Profiles;

namespace EvidenceHound.Collectors.Rest.Tests;

/// <summary>Every profile in the public library is valid, has tests, and passes them. CI runs this with the unit suite.</summary>
public sealed class LibraryProfileTests
{
    public static TheoryData<string> LibraryProfiles() => [.. ProfileFolder.Names(Profiles.LibraryFolder)];

    [Theory]
    [MemberData(nameof(LibraryProfiles))]
    public async Task Library_profile_is_valid_and_passes_its_tests(string name)
    {
        var loaded = ProfileFolder.Load(Profiles.LibraryFolder, name).Value();
        ProfileValidator.Validate(loaded.Profile, RestCollector.Validation).ShouldBeEmpty();
        ProfileTestRunner.HasTests(Profiles.LibraryFolder, name).ShouldBeTrue($"library profile '{name}' needs {name}.tests/");

        var cases = (await ProfileTestRunner.RunAsync(Profiles.LibraryFolder, name, TestContext.Current.CancellationToken)).Value();

        cases.ShouldAllBe(c => c.Passed);
    }
}
