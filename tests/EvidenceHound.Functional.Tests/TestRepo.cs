namespace EvidenceHound.Functional.Tests;

/// <summary>A working copy of the example compliance repo, for a test that changes it.</summary>
internal static class TestRepo
{
    public static void CopyExample(string repo)
    {
        var example = Path.Combine(CisoAssistantFixture.RepoRoot, "examples", "compliance-repo");
        foreach (var file in Directory.GetFiles(example, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(repo, Path.GetRelativePath(example, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
    }
}
