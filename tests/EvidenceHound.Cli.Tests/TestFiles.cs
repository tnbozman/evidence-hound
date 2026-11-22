namespace EvidenceHound.Cli.Tests;

internal static class TestFiles
{
    /// <summary>A copy of the example compliance repo in <paramref name="to"/>, for tests that change it.</summary>
    public static string CopyExampleRepo(string to)
    {
        CopyDirectory(Path.Combine(ExampleRepoTests.RepoRoot, "examples", "compliance-repo"), to);
        return to;
    }

    public static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }
    }
}
