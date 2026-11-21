using EvidenceHound.Runtime.Status;

namespace EvidenceHound.Runtime.Tests;

public sealed class FileRunRecordStoreTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 6, 30, 2, 0, 0, TimeSpan.Zero);
    private readonly string _root = Directory.CreateTempSubdirectory("hound-status-").FullName;

    private static RunRecord Record(string collector, DateTimeOffset at) => new(collector, at, at.AddSeconds(1), CollectorOutcome.Succeeded, 1, 0, 0, [], null);

    [Fact]
    public async Task Keeps_the_latest_record_per_collector_sorted_by_name()
    {
        var store = new FileRunRecordStore(_root);
        await store.SaveAsync(Record("zeta", Start), TestContext.Current.CancellationToken);
        await store.SaveAsync(Record("alpha", Start), TestContext.Current.CancellationToken);
        await store.SaveAsync(Record("zeta", Start.AddDays(1)), TestContext.Current.CancellationToken);

        var latest = (await store.LatestAsync(TestContext.Current.CancellationToken)).Value();

        latest.ShouldBe([Record("alpha", Start), Record("zeta", Start.AddDays(1))]);
        File.Exists(Path.Combine(_root, "_status", "zeta.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task No_records_yet_is_an_empty_list()
    {
        (await new FileRunRecordStore(_root).LatestAsync(TestContext.Current.CancellationToken)).Value().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unreadable_record_is_reported()
    {
        Directory.CreateDirectory(Path.Combine(_root, "_status"));
        await File.WriteAllTextAsync(Path.Combine(_root, "_status", "broken.json"), "{", TestContext.Current.CancellationToken);

        var problem = (await new FileRunRecordStore(_root).LatestAsync(TestContext.Current.CancellationToken)).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("status.record_invalid");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
