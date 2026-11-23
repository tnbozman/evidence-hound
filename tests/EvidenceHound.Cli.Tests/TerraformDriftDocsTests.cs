using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Cli.Tests;

/// <summary>The guide, docs/collectors/terraform-drift.md, keeps up with both terraform-drift library profiles (s09-33).</summary>
public class TerraformDriftDocsTests
{
    private static readonly string Guide = File.ReadAllText(Path.Combine(ExampleRepoTests.RepoRoot, "docs", "collectors", "terraform-drift.md"));

    [Theory]
    [InlineData("terraform-drift")]
    [InlineData("terraform-drift-trigger")]
    public void Every_check_parameter_and_secret_is_documented(string name)
    {
        var profile = ((Result<LoadedProfile>.Ok)ProfileFolder.Load(Path.Combine(ExampleRepoTests.RepoRoot, "profiles"), name)).Value.Profile;
        string[] names = [name, .. profile.Checks.Select(c => c.Id), .. profile.Parameters.Select(p => p.Name), .. profile.Secrets.Select(s => s.Name)];

        names.Where(n => !Guide.Contains($"`{n}`", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void Guide_covers_the_pipeline_the_opt_in_and_the_repository_layout()
    {
        foreach (var expected in new[] { "terraform plan -refresh-only", "plan.json", "allow_writes: true", "Actions: read", "workflow_dispatch", "separate", "monorepo", "never runs Terraform" })
        {
            Guide.ShouldContain(expected, Case.Sensitive);
        }
    }
}
