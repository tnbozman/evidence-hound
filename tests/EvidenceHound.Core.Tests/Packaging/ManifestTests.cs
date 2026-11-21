using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Packaging;

namespace EvidenceHound.Core.Tests.Packaging;

public class ManifestTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);

    private static CollectedFile File(string name, string content) => new(name, "application/json", Encoding.UTF8.GetBytes(content));

    private static EvidenceManifest Build(params CollectedFile[] files) =>
        Manifests.Build("example-fake", "1.0.0", At, files).Match(m => m, p => throw new InvalidOperationException(p[0].Message));

    [Fact]
    public void Hash_matches_known_vector()
    {
        var entry = Build(File("abc.txt", "abc")).Files.ShouldHaveSingleItem();

        entry.Sha256.ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        entry.SizeBytes.ShouldBe(3);
        entry.MediaType.ShouldBe("application/json");
        entry.CollectedAt.ShouldBe(At);
    }

    [Fact]
    public void Manifest_is_deterministic_for_same_inputs()
    {
        var first = Manifests.ToJson(Build(File("b.json", "{}"), File("a.json", "[]")));
        var second = Manifests.ToJson(Build(File("a.json", "[]"), File("b.json", "{}")));

        second.ShouldBe(first);
        Build(File("b.json", "{}"), File("a.json", "[]")).Files.Select(f => f.FileName).ShouldBe(["a.json", "b.json"]);
    }

    [Fact]
    public void Json_carries_collector_version_time_and_each_file()
    {
        var json = JsonNode.Parse(Manifests.ToJson(Build(File("abc.txt", "abc"))))!;

        json["collector"]!.GetValue<string>().ShouldBe("example-fake");
        json["collector_version"]!.GetValue<string>().ShouldBe("1.0.0");
        json["collected_at"]!.GetValue<string>().ShouldBe("2026-01-15T09:30:00+00:00");
        var file = json["files"]![0]!;
        file["file_name"]!.GetValue<string>().ShouldBe("abc.txt");
        file["size_bytes"]!.GetValue<long>().ShouldBe(3);
        file["sha256"]!.GetValue<string>().ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        file["media_type"]!.GetValue<string>().ShouldBe("application/json");
    }

    [Theory]
    [InlineData("../escape.json", "evidence.file_name_invalid")]
    [InlineData("sub/dir.json", "evidence.file_name_invalid")]
    [InlineData("_manifest.json", "evidence.file_name_reserved")]
    [InlineData("", "evidence.file_name_invalid")]
    public void Unsafe_file_names_are_rejected(string name, string code)
    {
        var problem = Manifests.Build("c", "1", At, [File(name, "x")]).Match(_ => throw new InvalidOperationException(), p => p).ShouldHaveSingleItem();

        problem.Code.ShouldBe(code);
    }

    [Fact]
    public void Duplicate_file_names_are_rejected()
    {
        Manifests.Build("c", "1", At, [File("a.json", "1"), File("a.json", "2")])
            .Match(_ => throw new InvalidOperationException(), p => p).ShouldHaveSingleItem().Code.ShouldBe("evidence.file_name_duplicate");
    }
}
