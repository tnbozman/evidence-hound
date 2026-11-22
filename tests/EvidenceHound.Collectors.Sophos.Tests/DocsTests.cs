namespace EvidenceHound.Collectors.Sophos.Tests;

/// <summary>The operator guide, docs/collectors/sophos.md, keeps up with the collector.</summary>
public class DocsTests
{
    private static readonly string Guide = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "sophos.md"));

    private static readonly SophosCollector Collector = new();

    [Fact]
    public void Every_check_id_declared_in_describe_is_documented()
    {
        Collector.Describe().Checks.Where(id => !Guide.Contains($"`{id}`", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_setting_and_secret_is_documented()
    {
        var description = Collector.Describe();

        description.Settings.Concat(description.Secrets).Select(s => s.Name)
            .Where(name => !Guide.Contains($"`{name}`", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_evidence_file_is_documented()
    {
        SophosSources.All.Select(s => s.EvidenceFile).Where(file => !Guide.Contains($"`{file}`", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void The_least_role_is_documented()
    {
        Guide.ShouldContain("Service Principal Read-Only");
    }

    [Theory]
    [InlineData("sophos.auth_failed")]
    [InlineData("sophos.forbidden")]
    [InlineData("sophos.tenant_id_required")]
    [InlineData("sophos.data_region_untrusted")]
    public void Common_problems_are_documented(string code)
    {
        Guide.ShouldContain($"`{code}`");
    }
}
