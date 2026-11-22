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
        var warnings = _logs.Entries.Where(e => e.Level == LogLevel.Warning).ToArray();
        warnings.Length.ShouldBe(2, "one for each check over the missing dataset");
        warnings.ShouldAllBe(w => w.Message.Contains("example-rest") && w.Message.Contains("'people'") && w.State[0].Value!.Equals("profile.reference_unknown"));
    }

    [Fact]
    public async Task Test_runs_the_example_profile_cases_offline()
    {
        CopyExample();
        CopyTests();

        var exitCode = await Run("profile", "test", "example", "--profiles", Profiles);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString() + _output);
        var text = _output.ToString();
        text.ShouldContain("example/all-pass: passed (4 checks)");
        text.ShouldContain("example/roles-unreadable: passed (4 checks)");
    }

    [Fact]
    public async Task Test_fails_naming_the_check_whose_result_changed()
    {
        CopyExample();
        CopyTests();
        var allPass = Path.Combine(Profiles, "example.tests", "all-pass.yaml");
        await File.WriteAllTextAsync(allPass, (await File.ReadAllTextAsync(allPass, TestContext.Current.CancellationToken))
            .Replace("example.identity.mfa_users: pass", "example.identity.mfa_users: fail", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        var exitCode = await Run("profile", "test", "example", "--profiles", Profiles);

        exitCode.ShouldBe(ExitCodes.ProfileTestsFailed);
        _output.ToString().ShouldContain("example/all-pass: FAILED");
        _output.ToString().ShouldContain("example.identity.mfa_users: expected fail, got pass.");
    }

    [Fact]
    public async Task Test_without_tests_says_how_to_add_them()
    {
        CopyExample();

        var exitCode = await Run("profile", "test", "example", "--profiles", Profiles);

        exitCode.ShouldBe(ExitCodes.Configuration);
        _logs.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("example.tests/"));
    }

    [Fact]
    public async Task Diff_shows_how_a_copy_differs_from_its_library_version()
    {
        CopyExample(yaml => yaml.Replace("    default: 2\n    description: Most users", "    default: 3\n    description: Most users", StringComparison.Ordinal));
        var library = Path.Combine(_workDir, "evidence-hound");
        Directory.CreateDirectory(Path.Combine(library, "profiles"));
        File.Copy(Library("example.yaml"), Path.Combine(library, "profiles", "example.yaml"));

        var exitCode = await Run("profile", "diff", "example", "--profiles", Profiles, "--library", library);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var text = _output.ToString();
        text.ShouldStartWith("example: local 1.0.0, library 1.0.0 (evidence-hound/profiles/example.yaml)");
        text.ShouldContain("-33:     default: 2");
        text.ShouldContain("+33:     default: 3");
    }

    private void CopyExample(Func<string, string>? edit = null)
    {
        Directory.CreateDirectory(Profiles);
        var yaml = File.ReadAllText(Library("example.yaml"));
        File.WriteAllText(Path.Combine(Profiles, "example.yaml"), edit is null ? yaml : edit(yaml));
    }

    private void CopyTests()
    {
        var source = Library("example.tests");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(Profiles, "example.tests", Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private Task<int> Run(params string[] args) =>
        HoundCli.RunAsync(args, _output, _error, logging => logging.AddProvider(_logs), TestContext.Current.CancellationToken);
}
