namespace EvidenceHound.Collectors.M365.Tests;

/// <summary>The operator guide, docs/collectors/m365.md, keeps up with the collector.</summary>
public class DocsTests
{
    private static readonly string Guide = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "m365.md"));

    private static readonly M365Collector Collector = new(new TestCredentialFactory(), new RecordingWait());

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
    public void Every_permission_the_collector_uses_is_documented()
    {
        GraphSources.All.Select(s => s.Request.Permission).Distinct()
            .Where(permission => !Guide.Contains($"`{permission}`", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_evidence_file_is_documented()
    {
        GraphSources.All.Select(s => s.EvidenceFile).Where(file => !Guide.Contains($"`{file}`", StringComparison.Ordinal)).ShouldBeEmpty();
    }
}
