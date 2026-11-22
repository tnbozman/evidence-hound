using System.Diagnostics;

namespace EvidenceHound.Cli.Tests;

/// <summary>s08-09: the config the interactive local trial writes (scripts/lib/trial.sh) is a valid deployment config.</summary>
public sealed class TrialConfigTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hound-trial-config-").FullName;

    private readonly StringWriter _output = new();

    private readonly StringWriter _error = new();

    [Theory]
    [InlineData("no")]
    [InlineData("yes")]
    public async Task Trial_config_validates(string withSophos)
    {
        CopyDirectory(Path.Combine(ExampleRepoTests.RepoRoot, "examples", "compliance-repo"), Path.Combine(_root, "repo"));
        var config = Path.Combine(_root, "repo", "hound.yaml");
        await File.WriteAllTextAsync(config, await RenderAsync(withSophos), TestContext.Current.CancellationToken);

        var exitCode = await HoundCli.RunAsync(["config", "validate", config], _output, _error, configureLogging: null, new Dictionary<string, string?>(), TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString() + _output);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private async Task<string> RenderAsync(string withSophos)
    {
        var library = Path.Combine(ExampleRepoTests.RepoRoot, "scripts", "lib", "trial.sh");
        var start = new ProcessStartInfo("bash")
        {
            ArgumentList =
            {
                "-c", "source \"$0\" && trial_render_config \"$1\" \"$2\" 00000000-0000-4000-8000-0000000050d1 \"21.5, 22.0\" 14", library, _root, withSophos,
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var bash = Process.Start(start)!;
        var text = await bash.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await bash.WaitForExitAsync(TestContext.Current.CancellationToken);
        bash.ExitCode.ShouldBe(0, await bash.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken));
        return text;
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(from, to, StringComparison.Ordinal));
        }

        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(from, to, StringComparison.Ordinal));
        }
    }
}
