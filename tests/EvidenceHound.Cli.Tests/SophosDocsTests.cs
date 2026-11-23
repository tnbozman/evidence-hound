using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Cli.Tests;

/// <summary>The operator guide, docs/collectors/sophos.md, keeps up with the sophos-central library profile (s09-13).</summary>
public class SophosDocsTests
{
    private static readonly string Guide = File.ReadAllText(Path.Combine(ExampleRepoTests.RepoRoot, "docs", "collectors", "sophos.md"));

    private static readonly Profile Profile =
        ((Result<LoadedProfile>.Ok)ProfileFolder.Load(Path.Combine(ExampleRepoTests.RepoRoot, "profiles"), "sophos-central")).Value.Profile;

    [Fact]
    public void Guide_configures_the_rest_collector_with_the_profile()
    {
        Guide.ShouldContain("type: rest");
        Guide.ShouldContain("profile: sophos-central");
    }

    [Fact]
    public void Every_check_parameter_secret_and_evidence_file_is_documented()
    {
        string[] names =
        [
            .. Profile.Checks.Select(c => c.Id),
            .. Profile.Parameters.Select(p => p.Name),
            .. Profile.Secrets.Select(s => s.Name),
            .. Profile.Queries.Select(q => q.Name + ".json"),
        ];

        names.Where(name => !Guide.Contains($"`{name}`", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void The_least_role_and_the_retired_type_are_documented()
    {
        Guide.ShouldContain("Service Principal Read-Only");
        Guide.ShouldContain("`config.collector_type_retired`");
    }
}
