using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

/// <summary><c>hound profile</c>: work with REST profiles in the organisation's profiles folder.</summary>
public sealed class ProfileCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly string _workDir = Directory.CreateTempSubdirectory("hound-profile-tests-").FullName;

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        Directory.Delete(_workDir, recursive: true);
    }

    private string Profiles => Path.Combine(_workDir, "profiles");

    private static string Library(string file) => Path.Combine(AppContext.BaseDirectory, "files", "profiles", file);

    [Fact]
    public async Task Validate_passes_for_the_example_library_profile()
    {
        CopyExample();

        var exitCode = await Run("profile", "validate", "example", "--profiles", Profiles);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        _output.ToString().Trim().ShouldBe("example 1.0.0: valid (3 queries, 1 dataset, 4 checks)");
    }

    [Fact]
    public async Task Validate_lists_each_problem_of_a_broken_copy_with_its_code()
    {
        CopyExample(yaml => yaml
            .Replace("over: user_roles", "over: people", StringComparison.Ordinal)
            .Replace("    when_empty: fail\n", "", StringComparison.Ordinal));

        var exitCode = await Run("profile", "validate", "example", "--profiles", Profiles);

        exitCode.ShouldBe(ExitCodes.Configuration);
        var codes = _logs.Entries.Where(e => e.Level == LogLevel.Error).Select(e => e.State[0].Value).ToArray();
        codes.ShouldBe(["profile.reference_unknown", "profile.reference_unknown", "profile.when_empty_missing"], ignoreOrder: true);
    }

    [Fact]
    public async Task Plan_warns_about_an_invalid_rest_profile_and_carries_on()
    {
        CopyExample(yaml => yaml.Replace("over: user_roles", "over: people", StringComparison.Ordinal));
        var config = Path.Combine(_workDir, "hound.yaml");
        var example = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "files", "examples", "example-pty-ltd.yaml"), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(config, example
            .Replace("../mappings/example.yaml", Path.Combine(AppContext.BaseDirectory, "files", "mappings", "example.yaml"), StringComparison.Ordinal)
            .Replace("    secrets: {}\n", """
                    secrets: {}
                  - name: example-rest
                    type: rest
                    schedule: "0 3 * * *"
                    settings:
                      profile: example
                      profiles_path: ./profiles
                      client_id: 00000000-0000-4000-8000-0000000000c1
                      supported_os: "14, 15"
                    secrets:
                      client_secret: env:EXAMPLE_CLIENT_SECRET

                """, StringComparison.Ordinal), TestContext.Current.CancellationToken);

        var exitCode = await Run("plan", "--dry-run", "--config", config, "--checks", Path.Combine(AppContext.BaseDirectory, "files", "fixtures", "checks-sample.json"));

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var warning = _logs.Entries.Where(e => e.Level == LogLevel.Warning).ShouldHaveSingleItem();
        warning.Message.ShouldContain("example-rest");
        warning.Message.ShouldContain("people");
    }

    private void CopyExample(Func<string, string>? edit = null)
    {
        Directory.CreateDirectory(Profiles);
        var yaml = File.ReadAllText(Library("example.yaml"));
        File.WriteAllText(Path.Combine(Profiles, "example.yaml"), edit is null ? yaml : edit(yaml));
    }

    private Task<int> Run(params string[] args) =>
        HoundCli.RunAsync(args, _output, _error, logging => logging.AddProvider(_logs), TestContext.Current.CancellationToken);
}
