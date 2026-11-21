using EvidenceHound.Core.Collecting;
using EvidenceHound.Core.Secrets;
using EvidenceHound.Runtime.Collectors;

namespace EvidenceHound.Runtime.Tests;

public class FakeCollectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 2, 0, 0, TimeSpan.Zero);

    private static readonly CollectorContext Context = new(
        "example-fake", new Dictionary<string, string> { ["tenant"] = "example.com" }, new Dictionary<string, Secret>(), Now);

    [Fact]
    public async Task Reports_checks_that_cite_its_own_files()
    {
        var output = (await new FakeCollector().CollectAsync(Context, TestContext.Current.CancellationToken)).Value();

        output.Checks.Select(c => c.CheckId.ToString()).ShouldBe(["identity.mfa.admins", "identity.mfa.users", "backup.daily", "backup.restore_tested"]);
        output.Checks.ShouldAllBe(c => c.CollectedAt == Now && c.Source == "example-fake");
        var files = output.Files.Select(f => f.FileName).ToHashSet(StringComparer.Ordinal);
        output.Checks.SelectMany(c => c.EvidenceRefs).ShouldAllBe(name => files.Contains(name));
        output.ControlStatuses.Select(c => c.ControlKey).ShouldBe(["mfa", "backups"]);
    }

    [Fact]
    public async Task Same_context_gives_the_same_files()
    {
        var first = (await new FakeCollector().CollectAsync(Context, TestContext.Current.CancellationToken)).Value();
        var second = (await new FakeCollector().CollectAsync(Context, TestContext.Current.CancellationToken)).Value();

        second.Files.Select(f => f.Content.ToArray()).ShouldBe(first.Files.Select(f => f.Content.ToArray()));
    }

    [Fact]
    public async Task Names_the_tenant_from_its_settings_in_the_evidence()
    {
        var output = (await new FakeCollector().CollectAsync(Context, TestContext.Current.CancellationToken)).Value();

        System.Text.Encoding.UTF8.GetString(output.Files[0].Content.Span).ShouldContain("example.com");
    }
}
