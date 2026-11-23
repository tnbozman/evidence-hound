using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Packaging;
using EvidenceHound.Runtime.Storage;

namespace EvidenceHound.Runtime.Tests;

public sealed class FileEvidenceStoreTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 1, 15, 9, 30, 0, 123, TimeSpan.Zero);
    private readonly string _root = Directory.CreateTempSubdirectory("hound-store-").FullName;

    private static readonly CollectedFile[] Files =
    [
        new("mfa-admins.json", "application/json", Encoding.UTF8.GetBytes("{\"admins\": 4}")),
        new("backups.json", "application/json", Encoding.UTF8.GetBytes("{\"servers\": 12}")),
    ];

    private static EvidenceManifest Manifest(DateTimeOffset at) => Manifests.Build("example-fake", "1.0.0", at, Files).Value();

    [Fact]
    public async Task Writes_files_and_manifest()
    {
        var store = new FileEvidenceStore(_root);

        var run = (await store.WriteRunAsync(Manifest(At), Files, TestContext.Current.CancellationToken)).Value();

        run.Location.ShouldBe(Path.Combine(_root, "example-fake", "20260115T093000123Z"));
        run.Complete.ShouldBeTrue();
        run.StartedAt.ShouldBe(At);
        foreach (var file in Files)
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(run.Location, file.FileName), TestContext.Current.CancellationToken);
            bytes.ShouldBe(file.Content.ToArray());
        }

        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(run.Location, "_manifest.json"), TestContext.Current.CancellationToken))!;
        var admins = manifest["files"]!.AsArray().Single(f => f!["file_name"]!.GetValue<string>() == "mfa-admins.json")!;
        admins["sha256"]!.GetValue<string>().ShouldBe(Convert.ToHexStringLower(SHA256.HashData(Files[0].Content.Span)));
    }

    [Fact]
    public async Task Lists_runs_oldest_first_and_marks_runs_without_a_manifest_incomplete()
    {
        var store = new FileEvidenceStore(_root);
        await store.WriteRunAsync(Manifest(At.AddDays(1)), Files, TestContext.Current.CancellationToken);
        await store.WriteRunAsync(Manifest(At), Files, TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(_root, "example-fake", "20260120T000000000Z"));
        Directory.CreateDirectory(Path.Combine(_root, "example-fake", "not-a-run"));

        var runs = await store.ListRunsAsync("example-fake", TestContext.Current.CancellationToken);

        runs.Select(r => (r.StartedAt, r.Complete)).ShouldBe([(At, true), (At.AddDays(1), true), (new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero), false)]);
    }

    [Fact]
    public async Task A_collector_with_no_runs_lists_none()
    {
        (await new FileEvidenceStore(_root).ListRunsAsync("never-ran", TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Delete_removes_the_run_folder()
    {
        var store = new FileEvidenceStore(_root);
        var run = (await store.WriteRunAsync(Manifest(At), Files, TestContext.Current.CancellationToken)).Value();

        await store.DeleteRunAsync(run, TestContext.Current.CancellationToken);

        Directory.Exists(run.Location).ShouldBeFalse();
        (await store.ListRunsAsync("example-fake", TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_second_run_at_the_same_time_is_refused()
    {
        var store = new FileEvidenceStore(_root);
        await store.WriteRunAsync(Manifest(At), Files, TestContext.Current.CancellationToken);

        var second = await store.WriteRunAsync(Manifest(At), Files, TestContext.Current.CancellationToken);

        second.Problems().ShouldHaveSingleItem().Code.ShouldBe("evidence.run_exists");
    }

    [Fact]
    public async Task A_collector_name_that_is_not_a_plain_folder_name_is_refused()
    {
        var manifest = Manifest(At) with { Collector = "../escape" };

        var result = await new FileEvidenceStore(_root).WriteRunAsync(manifest, Files, TestContext.Current.CancellationToken);

        result.Problems().ShouldHaveSingleItem().Code.ShouldBe("evidence.collector_name_invalid");
        Directory.Exists(Path.Combine(_root, "..", "escape")).ShouldBeFalse();
    }

    [Fact]
    public async Task Reads_one_file_of_a_stored_run()
    {
        var store = new FileEvidenceStore(_root);
        var run = (await store.WriteRunAsync(Manifest(At), Files, TestContext.Current.CancellationToken)).Value();

        (await store.ReadFileAsync(run, "backups.json", TestContext.Current.CancellationToken)).ShouldBe(Files[1].Content.ToArray());
        (await store.ReadFileAsync(run, "missing.json", TestContext.Current.CancellationToken)).ShouldBeNull();
        (await store.ReadFileAsync(run, "../backups.json", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
