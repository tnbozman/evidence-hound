using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

/// <summary>s08-09: <c>hound add collector</c> adds a collector of any type from its own description, keeping the config as it was.</summary>
public sealed class AddCollectorCommandTests : IDisposable
{
    private static readonly string Config = """
        # Example Pty Ltd's trial config. Everything here is made up.
        ciso_assistant:
          url: https://ciso.example.com/api/
          token: env:HOUND_CA_TOKEN

        evidence:
          root: ./evidence

        collectors: []   # added with hound add collector

        target:
          mapping: mapping.yaml
          compliance_assessment: EX-E8-2026

        """;

    private static readonly string Secret = "example-sophos-client-secret-4b7d2e";

    private static readonly string ClientId = "00000000-0000-4000-8000-0000000050d1";

    private static readonly string TenantId = "00000000-0000-4000-8000-00000000e365";

    /// <summary>Not a certificate: the shape of one, so the test sees a multi-line secret.</summary>
    private static readonly string Pem = "-----BEGIN CERTIFICATE-----\nEXAMPLE-NOT-A-REAL-KEY\n-----END CERTIFICATE-----\n";

    private readonly string _root = Directory.CreateTempSubdirectory("hound-add-collector-tests-").FullName;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly RecordingLoggerProvider _logs = new();

    private string ConfigFile => Path.Combine(_root, "hound.yaml");

    /// <summary>The library profiles, as the test project copies them.</summary>
    private static readonly string Profiles = Path.Combine(AppContext.BaseDirectory, "files", "profiles");

    private static readonly string[] SophosProfile = ["--set", "profile=sophos-central", "--set", $"profiles_path={Profiles}"];

    [Fact]
    public async Task Asks_for_the_required_settings_from_the_collector_description()
    {
        await WriteConfigAsync(Config);

        // Name, schedule, then the profile's client_id and supported_firewall_firmware, and the client secret's reference.
        var exitCode = await Run($"sophos\n\n{ClientId}\n21.0, 22.0\n\n", ["add", "collector", "rest", "--config", ConfigFile, .. SophosProfile]);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var config = await File.ReadAllTextAsync(ConfigFile, TestContext.Current.CancellationToken);
        config.ShouldStartWith("# Example Pty Ltd's trial config.");
        config.ShouldContain($"""
            collectors:   # added with hound add collector
              - name: sophos
                type: rest
                schedule: "0 3 * * *"
                settings:
                  profile: sophos-central
                  profiles_path: {Profiles}
                  client_id: {ClientId}
                  supported_firewall_firmware: "21.0, 22.0"
                secrets:
                  client_secret: file:/run/secrets/sophos_client_secret
            """.ReplaceLineEndings("\n"));
        _error.ToString().ShouldContain("The API credential's client ID. (client_id)");
        _error.ToString().ShouldNotContain("(super_admin_max)");
        (await Run("", "config", "validate", ConfigFile)).ShouldBe(ExitCodes.Ok, _error.ToString());
    }

    [Fact]
    public async Task Set_gives_any_setting_and_yes_asks_nothing()
    {
        await WriteConfigAsync(Config);

        var exitCode = await Run(
            "", ["add", "collector", "rest", "--config", ConfigFile, "--yes", "--name", "home-sophos", .. SophosProfile,
            "--set", $"client_id={ClientId}", "--set", "supported_firewall_firmware=21.0", "--set", "super_admin_max=1"]);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var config = await File.ReadAllTextAsync(ConfigFile, TestContext.Current.CancellationToken);
        config.ShouldContain("      super_admin_max: \"1\"");
        config.ShouldContain("      client_secret: file:/run/secrets/home-sophos_client_secret");
        _error.ToString().ShouldNotContain("[");
    }

    [Fact]
    public async Task Secrets_dir_stores_the_secret_mode_600_and_never_shows_it()
    {
        await WriteConfigAsync(Config);
        var secrets = Path.Combine(_root, "secrets");

        var exitCode = await Run(
            $"{Secret}\n", ["add", "collector", "rest", "--config", ConfigFile, "--yes", "--name", "sophos", "--secrets-dir", secrets, .. SophosProfile,
            "--set", $"client_id={ClientId}", "--set", "supported_firewall_firmware=21.0"]);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var file = Path.Combine(secrets, "sophos_client_secret");
        (await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken)).ShouldBe(Secret);
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(file).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.GetUnixFileMode(secrets).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        (await File.ReadAllTextAsync(ConfigFile, TestContext.Current.CancellationToken)).ShouldContain($"client_secret: file:{file}");
        (_output.ToString() + _error).ShouldNotContain(Secret);
        _logs.Entries.ShouldNotContain(e => e.Message.Contains(Secret, StringComparison.Ordinal));
        var token = new Dictionary<string, string?> { ["HOUND_CA_TOKEN"] = "fake-ca-token-for-tests" };
        (await HoundCli.RunAsync(["config", "validate", ConfigFile, "--check-secrets"], _output, _error, configureLogging: null, token, TestContext.Current.CancellationToken)).ShouldBe(ExitCodes.Ok, _error.ToString());
    }

    [Fact]
    public async Task Secret_file_is_copied_mode_600_and_referenced()
    {
        await WriteConfigAsync(Config);
        var secrets = Path.Combine(_root, "secrets");
        var pem = Path.Combine(_root, "m365.pem");
        await File.WriteAllTextAsync(pem, Pem, TestContext.Current.CancellationToken);

        var exitCode = await Run(
            "", "add", "collector", "m365", "--config", ConfigFile, "--yes", "--secrets-dir", secrets, "--secret-file", $"client_certificate={pem}",
            "--set", $"tenant_id={TenantId}", "--set", $"client_id={ClientId}", "--set", "supported_windows_builds=10.0.26100");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var file = Path.Combine(secrets, "m365_client_certificate");
        (await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken)).ShouldBe(Pem);
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(file).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        (await File.ReadAllTextAsync(ConfigFile, TestContext.Current.CancellationToken)).ShouldContain($"client_certificate: file:{file}");
        (_output.ToString() + _error).ShouldNotContain("EXAMPLE-NOT-A-REAL-KEY");
        _logs.Entries.ShouldNotContain(e => e.Message.Contains("EXAMPLE-NOT-A-REAL-KEY", StringComparison.Ordinal));
        (await Run("", "config", "validate", ConfigFile)).ShouldBe(ExitCodes.Ok, _error.ToString());
    }

    [Fact]
    public async Task Secret_file_needs_the_secrets_dir_and_a_secret_the_collector_has()
    {
        await WriteConfigAsync(Config);
        var pem = Path.Combine(_root, "m365.pem");
        await File.WriteAllTextAsync(pem, Pem, TestContext.Current.CancellationToken);
        string[] settings = ["--set", $"tenant_id={TenantId}", "--set", $"client_id={ClientId}", "--set", "supported_windows_builds=10.0.26100", "--yes"];

        (await Run("", ["add", "collector", "m365", "--config", ConfigFile, "--secret-file", $"client_certificate={pem}", .. settings])).ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("give --secrets-dir too");
        (await Run("", ["add", "collector", "m365", "--config", ConfigFile, "--secrets-dir", Path.Combine(_root, "secrets"), "--secret-file", $"api_key={pem}", .. settings])).ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("'api_key' is not a secret of this collector");
        (await File.ReadAllTextAsync(ConfigFile, TestContext.Current.CancellationToken)).ShouldBe(Config);
    }

    [Fact]
    public async Task M365_asks_only_its_required_settings()
    {
        await WriteConfigAsync(Config);
        var pem = Path.Combine(_root, "m365.pem");
        await File.WriteAllTextAsync(pem, Pem, TestContext.Current.CancellationToken);

        // Name, schedule, tenant_id, client_id, supported_windows_builds.
        var exitCode = await Run(
            $"\n\n{TenantId}\n{ClientId}\n10.0.22631, 10.0.26100\n", "add", "collector", "m365", "--config", ConfigFile,
            "--secrets-dir", Path.Combine(_root, "secrets"), "--secret-file", $"client_certificate={pem}");

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        _error.ToString().ShouldContain("Directory (tenant) ID of the Entra tenant.");
        _error.ToString().ShouldNotContain("Microsoft Graph address");
        _error.ToString().ShouldNotContain("PEM certificate");
        (await File.ReadAllTextAsync(ConfigFile, TestContext.Current.CancellationToken)).ShouldContain("supported_windows_builds: \"10.0.22631, 10.0.26100\"");
    }

    [Fact]
    public async Task Manual_source_is_added_with_its_drop_folder_checks_and_owner()
    {
        await WriteConfigAsync(Config);

        // Name, schedule, drop_folder, checks, due_days, owner.
        var exitCode = await Run("example-restore-test\n0 6 * * *\n./manual/restore\nbackup.restore_tested\n92\nSam Example\n", "add", "collector", "manual", "--config", ConfigFile);

        exitCode.ShouldBe(ExitCodes.Ok, _error.ToString());
        var config = await File.ReadAllTextAsync(ConfigFile, TestContext.Current.CancellationToken);
        config.ShouldContain("""
              - name: example-restore-test
                type: manual
                schedule: "0 6 * * *"
                settings:
                  drop_folder: ./manual/restore
                  checks: backup.restore_tested
                  due_days: "92"
                  owner: Sam Example
            """.ReplaceLineEndings("\n"));
        (await Run("", "config", "validate", ConfigFile)).ShouldBe(ExitCodes.Ok, _error.ToString());
    }

    [Fact]
    public async Task Duplicate_name_or_unknown_type_is_refused_and_nothing_is_written()
    {
        await WriteConfigAsync(Config);
        (await Run("", ["add", "collector", "rest", "--config", ConfigFile, "--yes", "--name", "sophos", .. SophosProfile, "--set", $"client_id={ClientId}", "--set", "supported_firewall_firmware=21.0"])).ShouldBe(ExitCodes.Ok, _error.ToString());
        var before = await File.ReadAllTextAsync(ConfigFile, TestContext.Current.CancellationToken);

        (await Run("", ["add", "collector", "rest", "--config", ConfigFile, "--yes", "--name", "sophos", .. SophosProfile, "--set", $"client_id={ClientId}", "--set", "supported_firewall_firmware=21.0"])).ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("'sophos' is already used");
        (await Run("", "add", "collector", "no-such-type", "--config", ConfigFile, "--yes")).ShouldBe(ExitCodes.Configuration);
        _error.ToString().ShouldContain("known types");
        (await File.ReadAllTextAsync(ConfigFile, TestContext.Current.CancellationToken)).ShouldBe(before);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _logs.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private async Task WriteConfigAsync(string text)
    {
        await File.WriteAllTextAsync(ConfigFile, text, TestContext.Current.CancellationToken);
        File.Copy(Path.Combine(ExampleRepoTests.RepoRoot, "mappings", "essential-eight-2023.yaml"), Path.Combine(_root, "mapping.yaml"), overwrite: true);
    }

    private Task<int> Run(string input, params string[] args) =>
        HoundCli.RunAsync(args, new StringReader(input), _output, _error, logging => logging.AddProvider(_logs), TestContext.Current.CancellationToken);
}
