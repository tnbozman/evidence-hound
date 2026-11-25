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

    /// <summary>
    /// The example deployment, with the test repo's scope, its evidence under the repo, and the fake collector's fail setting (check ids,
    /// comma-separated). Waits a second first: evidence folders and run IDs are named by the start time.
    /// </summary>
    public static async Task<string> ExampleDeploymentAsync(string repo, string? fail, CancellationToken ct)
    {
        var files = Path.Combine(AppContext.BaseDirectory, "files");
        var config = Path.Combine(repo, "hound-test.yaml");
        var text = (await File.ReadAllTextAsync(Path.Combine(files, "examples", "example-pty-ltd.yaml"), ct))
            .Replace("root: ../.dev/evidence", $"root: {Path.Combine(repo, "evidence")}", StringComparison.Ordinal)
            .Replace("mapping: ../mappings/example.yaml", $"mapping: {Path.Combine(files, "mappings", "example.yaml")}", StringComparison.Ordinal)
            .Replace("      tenant: example.com\n", fail is null ? "      tenant: example.com\n" : $"      tenant: example.com\n      fail: {fail}\n", StringComparison.Ordinal);
        await File.WriteAllTextAsync(config, $"scope: {Path.Combine(repo, "scope")}\n{text}", ct);
        await Task.Delay(TimeSpan.FromSeconds(1.1), ct);
        return config;
    }
}
