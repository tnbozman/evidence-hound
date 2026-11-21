using EvidenceHound.Collectors;
using EvidenceHound.Core.Secrets;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceHound.Runtime.Tests;

public class FakeCollectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 2, 0, 0, TimeSpan.Zero);

    private static readonly CollectorContext Context = new(
        "example-fake",
        new CollectorSettings(new Dictionary<string, string> { ["tenant"] = "example.com" }),
        new Dictionary<string, Secret>(),
        new FixedClock(Now),
        NullLogger.Instance,
        new TestHttpClients());

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
    public async Task Describes_every_check_it_reports()
    {
        var output = (await new FakeCollector().CollectAsync(Context, TestContext.Current.CancellationToken)).Value();

        var description = new FakeCollector().Describe();

        description.Checks.ShouldBe(output.Checks.Select(c => c.CheckId.Value), ignoreOrder: true);
        description.Settings.ShouldHaveSingleItem().ShouldBe(new SettingDescription("tenant", "Tenant domain named in the evidence. Default: example.com.", Required: false));
        description.Secrets.ShouldBeEmpty();
    }

    [Fact]
    public async Task Same_context_gives_the_same_files()
    {
        var first = (await new FakeCollector().CollectAsync(Context, TestContext.Current.CancellationToken)).Value();
        var second = (await new FakeCollector().CollectAsync(Context, TestContext.Current.CancellationToken)).Value();

        second.Files.Select(Bytes).ShouldBe(first.Files.Select(Bytes));
    }

    [Fact]
    public async Task Names_the_tenant_from_its_settings_in_the_evidence()
    {
        var output = (await new FakeCollector().CollectAsync(Context, TestContext.Current.CancellationToken)).Value();

        System.Text.Encoding.UTF8.GetString(Bytes(output.Files[0])).ShouldContain("example.com");
    }

    private static byte[] Bytes(CollectedEvidence file)
    {
        using var content = file.Open();
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        return buffer.ToArray();
    }
}
