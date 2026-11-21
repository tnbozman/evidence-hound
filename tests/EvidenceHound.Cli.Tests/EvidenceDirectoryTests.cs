using System.Text;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Cli.Tests;

public sealed class EvidenceDirectoryTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("hound-evidence-");

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public async Task Reads_the_file_named_by_the_evidence()
    {
        await File.WriteAllTextAsync(Path.Combine(_root.FullName, "mfa-admins.json"), "{}", TestContext.Current.CancellationToken);

        var content = await Read("mfa-admins.json");

        content.Match(bytes => Encoding.UTF8.GetString(bytes.Span), _ => "").ShouldBe("{}");
    }

    [Fact]
    public async Task Missing_file_is_a_problem()
    {
        (await Read("missing.json")).Match(_ => "", p => p[0].Code).ShouldBe("evidence.file_not_found");
    }

    [Theory]
    [InlineData("../secret.json")]
    [InlineData("sub/file.json")]
    public async Task File_name_with_a_path_is_refused(string fileName)
    {
        (await Read(fileName)).Match(_ => "", p => p[0].Code).ShouldBe("evidence.file_name_invalid");
    }

    private Task<Result<ReadOnlyMemory<byte>>> Read(string fileName)
    {
        var evidence = Evidence.Create("ev-0001", fileName, "application/json", new string('a', 64), 2, DateTimeOffset.UnixEpoch)
            .Match(e => e, p => throw new InvalidOperationException(p[0].Message));
        return new EvidenceDirectory(_root).ReadAsync(evidence, TestContext.Current.CancellationToken);
    }
}
